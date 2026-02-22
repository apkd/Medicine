using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;
using static Constants;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(TrackingAttributesFixProvider)), Shared]
public sealed class TrackingAttributesFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; }
        = ImmutableArray.Create("MED001", "MED002", "MED017", "MED035", "MED036");

    public override FixAllProvider GetFixAllProvider()
        => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

        if (root is null)
            return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;
        var diagnosticNode = root.FindNode(diagnosticSpan);

        if (diagnostic.Id is "MED035")
        {
            var forEach = diagnosticNode as ForEachStatementSyntax
                          ?? diagnosticNode.FirstAncestorOrSelf<ForEachStatementSyntax>();

            if (forEach is null)
                return;

            if (!TryRewriteWithCopy(forEach.Expression, out _))
                return;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Use .WithCopy for mutation-safe enumeration",
                    createChangedDocument: ct => AddWithCopyAsync(context.Document, forEach, ct),
                    equivalenceKey: "MED035.UseWithCopy"
                ),
                diagnostic
            );

            return;
        }

        if (diagnostic.Id is "MED036")
        {
            var typeDeclaration = diagnosticNode as TypeDeclarationSyntax
                                  ?? diagnosticNode.FirstAncestorOrSelf<TypeDeclarationSyntax>();

            if (typeDeclaration is not ClassDeclarationSyntax)
                return;

            if (typeDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                return;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Add partial modifier",
                    createChangedDocument: ct => AddPartialModifierToClassAsync(context.Document, typeDeclaration, ct),
                    equivalenceKey: "MED036.AddPartial"
                ),
                diagnostic
            );

            return;
        }

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken);

        INamedTypeSymbol? typeSymbol = diagnostic.Id switch
        {
            "MED001" or "MED002" => semanticModel?.GetTypeInfo(diagnosticNode, context.CancellationToken).ConvertedType as INamedTypeSymbol,
            "MED017"             => semanticModel?.GetDeclaredSymbol(diagnosticNode, context.CancellationToken) as INamedTypeSymbol,
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

        if (diagnostic.Id == "MED017")
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: $"Add the [{attributeName}] attribute",
                    createChangedDocument: ct => AddAttributeToCurrentDocumentAsync(context.Document, typeSymbol, attributeName, ct),
                    equivalenceKey: $"Add{attributeName}"
                ),
                diagnostic
            );
        }
        else
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: $"Add the [{attributeName}] attribute",
                    createChangedSolution: ct => AddAttributeAsync(context.Document.Project.Solution, typeSymbol, attributeName, ct),
                    equivalenceKey: $"Add{attributeName}"
                ),
                diagnostic
            );
        }

        if (diagnostic.Id == "MED017")
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Remove the {IInstanceIndexInterfaceName} interface",
                    createChangedDocument: ct => RemoveInterfaceFromCurrentDocumentAsync(context.Document, typeSymbol, IInstanceIndexInterfaceName, ct),
                    equivalenceKey: $"Remove{IInstanceIndexInterfaceName}"
                ),
                diagnostic
            );
        }
    }

    static async Task<Document> RemoveInterfaceFromCurrentDocumentAsync(Document document, INamedTypeSymbol typeSymbol, string interfaceName, CancellationToken ct)
    {
        var solution = await RemoveInterfaceAsync(document.Project.Solution, typeSymbol, interfaceName, ct).ConfigureAwait(false);
        return solution.GetDocument(document.Id) ?? document;
    }

    static async Task<Document> AddAttributeToCurrentDocumentAsync(Document document, INamedTypeSymbol typeSymbol, string attributeName, CancellationToken ct)
    {
        var solution = await AddAttributeAsync(document.Project.Solution, typeSymbol, attributeName, ct).ConfigureAwait(false);
        return solution.GetDocument(document.Id) ?? document;
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
        ).WithAdditionalAnnotations(Formatter.Annotation, Simplifier.Annotation);

        var newRoot = root.ReplaceNode(typeDeclaration, newTypeDeclaration)
            .WithAdditionalAnnotations(Formatter.Annotation, Simplifier.Annotation);
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

        newTypeDeclaration = newTypeDeclaration.WithAdditionalAnnotations(Formatter.Annotation, Simplifier.Annotation);
        var newRoot = root.ReplaceNode(typeDeclaration, newTypeDeclaration)
            .WithAdditionalAnnotations(Formatter.Annotation, Simplifier.Annotation);

        if (newRoot is CompilationUnitSyntax compilationUnit)
        {
            const string medicineNamespace = "Medicine";
            if (compilationUnit.Usings.All(x => x.Name.ToString() != medicineNamespace))
                newRoot = compilationUnit.AddUsings(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(medicineNamespace)));
        }

        return solution.WithDocumentSyntaxRoot(targetDocument.Id, newRoot);
    }

    static async Task<Document> AddPartialModifierToClassAsync(
        Document document,
        TypeDeclarationSyntax typeDeclaration,
        CancellationToken ct
    )
    {
        if (typeDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            return document;

        var editor = await DocumentEditor.CreateAsync(document, ct).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        var typeSymbol = model?.GetDeclaredSymbol(typeDeclaration, ct);

        if (typeSymbol is null)
        {
            var partialType = typeDeclaration
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PartialKeyword))
                .WithAdditionalAnnotations(Formatter.Annotation, Simplifier.Annotation);

            editor.ReplaceNode(typeDeclaration, partialType);
            return editor.GetChangedDocument();
        }

        editor.SetModifiers(typeDeclaration, DeclarationModifiers.From(typeSymbol).WithPartial(true));
        return editor.GetChangedDocument();
    }

    static async Task<Document> AddWithCopyAsync(
        Document document,
        ForEachStatementSyntax forEach,
        CancellationToken ct
    )
    {
        if (!TryRewriteWithCopy(forEach.Expression, out var rewrittenExpression))
            return document;

        var editor = await DocumentEditor.CreateAsync(document, ct).ConfigureAwait(false);
        editor.ReplaceNode(
            forEach,
            forEach.WithExpression(rewrittenExpression.WithTriviaFrom(forEach.Expression))
        );

        return editor.GetChangedDocument();
    }

    static bool TryRewriteWithCopy(ExpressionSyntax expression, out ExpressionSyntax rewrittenExpression)
    {
        rewrittenExpression = expression;
        if (ContainsWithCopy(expression))
            return false;

        if (expression is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax
                {
                    Name.Text: "WithStride",
                    Expression: { } receiver,
                } methodAccess,
            } invocation)
        {
            var withCopyReceiver = AppendWithCopy(receiver);
            rewrittenExpression = invocation.WithExpression(methodAccess.WithExpression(withCopyReceiver));
            return true;
        }

        rewrittenExpression = AppendWithCopy(expression);
        return true;
    }

    static bool ContainsWithCopy(ExpressionSyntax expression)
        => expression
            .DescendantNodesAndSelf()
            .OfType<MemberAccessExpressionSyntax>()
            .Any(x => x.Name.Text is "WithCopy");

    static ExpressionSyntax AppendWithCopy(ExpressionSyntax expression)
    {
        var receiver = PrepareReceiver(expression.WithoutTrivia());
        return SyntaxFactory.MemberAccessExpression(
            kind: SyntaxKind.SimpleMemberAccessExpression,
            expression: receiver,
            name: SyntaxFactory.IdentifierName("WithCopy")
        );
    }

    static ExpressionSyntax PrepareReceiver(ExpressionSyntax expression)
        => NeedsParentheses(expression)
            ? SyntaxFactory.ParenthesizedExpression(expression)
            : expression;

    static bool NeedsParentheses(ExpressionSyntax expression)
        => expression is not
            (
                IdentifierNameSyntax or
                GenericNameSyntax or
                MemberAccessExpressionSyntax or
                ElementAccessExpressionSyntax or
                InvocationExpressionSyntax or
                ThisExpressionSyntax or
                BaseExpressionSyntax or
                ParenthesizedExpressionSyntax
            );
}
