using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Constants;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(TrackingAttributesFixProvider)), Shared]
public sealed class TrackingAttributesFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; }
        = ImmutableArray.Create("MED001", "MED002", "MED017");

    public override FixAllProvider GetFixAllProvider()
        => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

        if (root is null)
            return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;
        var typeArgument = root.FindNode(diagnosticSpan);

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken);

        INamedTypeSymbol? typeSymbol = diagnostic.Id switch
        {
            "MED001" or "MED002" => semanticModel?.GetTypeInfo(typeArgument, context.CancellationToken).ConvertedType as INamedTypeSymbol,
            "MED017"             => semanticModel?.GetDeclaredSymbol(typeArgument, context.CancellationToken) as INamedTypeSymbol,
            _                    => null,
        };

        if (typeSymbol is not { DeclaringSyntaxReferences.Length: > 0 })
            return;

        string? attributeName = diagnostic.Id switch
        {
            "MED001"             => "Singleton",
            "MED002" or "MED017" => "Track",
            _                    => null,
        };

        if (attributeName is null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Add the [{attributeName}] attribute",
                createChangedSolution: ct => AddAttributeAsync(context.Document.Project.Solution, typeSymbol, attributeName, ct),
                equivalenceKey: $"Add{attributeName}"
            ),
            diagnostic
        );

        if (diagnostic.Id == "MED017")
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Remove the {IInstanceIndexInterfaceName} interface",
                    createChangedSolution: ct => RemoveInterfaceAsync(context.Document.Project.Solution, typeSymbol, IInstanceIndexInterfaceName, ct),
                    equivalenceKey: $"Remove{IInstanceIndexInterfaceName}"
                ),
                diagnostic
            );
        }
    }

    static async Task<Solution> RemoveInterfaceAsync(Solution solution, INamedTypeSymbol typeSymbol, string interfaceName, CancellationToken ct)
    {
        var declarationSyntaxReference = typeSymbol.DeclaringSyntaxReferences.First();

        if (solution.GetDocument(declarationSyntaxReference.SyntaxTree) is not { } targetDocument)
            return solution;

        if (await targetDocument.GetSyntaxRootAsync(ct).ConfigureAwait(false) is not { } root)
            return solution;

        if (await declarationSyntaxReference.GetSyntaxAsync(ct) is not TypeDeclarationSyntax typeDeclaration)
            return solution;

        var baseList = typeDeclaration.BaseList;
        if (baseList is null)
            return solution;

        var newBaseList = baseList.WithTypes(
            SyntaxFactory.SeparatedList(
                baseList.Types.Where(t => t.Type.ToString() != interfaceName)
            )
        );

        var newTypeDeclaration = typeDeclaration.WithBaseList(
            newBaseList.Types.Count > 0 ? newBaseList.WithTriviaFrom(baseList) : null
        );

        var newRoot = root.ReplaceNode(typeDeclaration, newTypeDeclaration);
        return solution.WithDocumentSyntaxRoot(targetDocument.Id, newRoot);
    }

    static async Task<Solution> AddAttributeAsync(Solution solution, INamedTypeSymbol typeSymbol, string attributeName, CancellationToken ct)
    {
        var declarationSyntaxReference = typeSymbol.DeclaringSyntaxReferences.First();

        if (solution.GetDocument(declarationSyntaxReference.SyntaxTree) is not { } targetDocument)
            return solution;

        if (await targetDocument.GetSyntaxRootAsync(ct).ConfigureAwait(false) is not { } root)
            return solution;

        if (await declarationSyntaxReference.GetSyntaxAsync(ct) is not TypeDeclarationSyntax typeDeclaration)
            return solution;

        var filteredAttributeLists = typeDeclaration.AttributeLists
            .Select(x => x.WithAttributes(SyntaxFactory.SeparatedList(x.Attributes.Where(y => y.Name.ToString() is not ("Register.Single" or "Register.All")))))
            .Where(x => x.Attributes.Count > 0)
            .ToList();

        var newAttr = SyntaxFactory.Attribute(SyntaxFactory.IdentifierName(attributeName));
        var newAttrList = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(newAttr));
        var newTypeDeclaration = typeDeclaration
            .WithAttributeLists(SyntaxFactory.List(filteredAttributeLists))
            .AddAttributeLists(newAttrList);

        if (!typeDeclaration.IsKind(SyntaxKind.InterfaceDeclaration))
            if (!typeDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                newTypeDeclaration = newTypeDeclaration.AddModifiers(SyntaxFactory.Token(SyntaxKind.PartialKeyword));

        var newRoot = root.ReplaceNode(typeDeclaration, newTypeDeclaration);

        if (newRoot is CompilationUnitSyntax compilationUnit)
        {
            const string medicineNamespace = "Medicine";
            if (compilationUnit.Usings.All(x => x.Name.ToString() != medicineNamespace))
                newRoot = compilationUnit.AddUsings(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(medicineNamespace)));
        }

        return solution.WithDocumentSyntaxRoot(targetDocument.Id, newRoot);
    }
}