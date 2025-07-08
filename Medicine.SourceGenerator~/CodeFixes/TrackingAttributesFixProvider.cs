using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(TrackingAttributesFixProvider)), Shared]
public sealed class TrackingAttributesFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; }
        = ImmutableArray.Create("MED001", "MED002");

    public override FixAllProvider GetFixAllProvider()
        => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        if (root is null)
            return;

        var typeArgument = root.FindNode(diagnosticSpan);

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken);

        if (semanticModel?.GetTypeInfo(typeArgument, context.CancellationToken).Type is not INamedTypeSymbol typeSymbol)
            return;

        if (typeSymbol.DeclaringSyntaxReferences.IsEmpty)
            return;

        string? attributeName = diagnostic.Id switch
        {
            "MED001" => "Singleton",
            "MED002" => "Track",
            _        => null,
        };

        if (attributeName is null)
            return;

        string title = $"Add [{attributeName}] attribute";
        context.RegisterCodeFix(
            CodeAction.Create(
                title: title,
                createChangedSolution: ct => AddAttributeAsync(context.Document.Project.Solution, typeSymbol, attributeName, ct),
                equivalenceKey: title
            ),
            diagnostic
        );
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
            .AddAttributeLists(newAttrList)
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PartialKeyword));
        var newRoot = root.ReplaceNode(typeDeclaration, newTypeDeclaration);

        if (newRoot is CompilationUnitSyntax compilationUnit)
        {
            const string medicineNamespace = "Medicine";
            if (compilationUnit.Usings.All(x => x.Name.ToString() != medicineNamespace))
                newRoot = compilationUnit.AddUsings(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(medicineNamespace)));
        }

        return solution.WithDocumentSyntaxRoot(targetDocument.Id, newRoot.NormalizeWhitespace());
    }
}