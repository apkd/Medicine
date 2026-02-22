using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(InjectFixProvider)), Shared]
public sealed class InjectFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; }
        = ImmutableArray.Create("MED034");

    public override FixAllProvider GetFixAllProvider()
        => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticNode = root.FindNode(diagnostic.Location.SourceSpan);

        if (diagnostic.Id is "MED034")
        {
            var typeDeclaration = diagnosticNode as TypeDeclarationSyntax
                                  ?? diagnosticNode.FirstAncestorOrSelf<TypeDeclarationSyntax>();

            if (typeDeclaration is null)
                return;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Add teardown call to Cleanup()",
                    createChangedDocument: ct => AddCleanupTeardownCallAsync(context.Document, typeDeclaration, ct),
                    equivalenceKey: "MED034.AddCleanupCall"
                ),
                diagnostic
            );

            return;
        }

    }

    static async Task<Document> AddCleanupTeardownCallAsync(
        Document document,
        TypeDeclarationSyntax typeDeclaration,
        CancellationToken ct
    )
    {
        var editor = await DocumentEditor.CreateAsync(document, ct).ConfigureAwait(false);

        static bool IsParameterlessMethod(MethodDeclarationSyntax method, string name)
            => method.Identifier.ValueText == name
               && method.ParameterList.Parameters.Count == 0;

        var onDestroy = typeDeclaration.Members
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(x => IsParameterlessMethod(x, "OnDestroy"));

        var dispose = typeDeclaration.Members
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(x => IsParameterlessMethod(x, "Dispose"));

        var teardownMethod = onDestroy ?? dispose;

        if (teardownMethod is not null)
        {
            if (ContainsCleanupInvocation(teardownMethod))
                return document;

            editor.ReplaceNode(teardownMethod, AppendCleanupCall(teardownMethod));
            return editor.GetChangedDocument();
        }

        var newMethod = await CreateTeardownMethodAsync(document, typeDeclaration, ct).ConfigureAwait(false);
        editor.AddMember(typeDeclaration, newMethod);
        return editor.GetChangedDocument();
    }

    static async Task<MethodDeclarationSyntax> CreateTeardownMethodAsync(
        Document document,
        TypeDeclarationSyntax typeDeclaration,
        CancellationToken ct
    )
    {
        var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        var typeSymbol = model?.GetDeclaredSymbol(typeDeclaration, ct);
        bool isDisposable = typeSymbol?.HasInterface(x => x.Is("global::System.IDisposable")) ?? false;

        var methodName = isDisposable ? "Dispose" : "OnDestroy";
        var modifiers = isDisposable
            ? SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
            : default;

        return SyntaxFactory.MethodDeclaration(
                attributeLists: default,
                modifiers: modifiers,
                returnType: SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)),
                explicitInterfaceSpecifier: null,
                identifier: SyntaxFactory.Identifier(methodName),
                typeParameterList: null,
                parameterList: SyntaxFactory.ParameterList(),
                constraintClauses: default,
                body: SyntaxFactory.Block(CleanupStatement()),
                expressionBody: null,
                semicolonToken: default
            );
    }

    static MethodDeclarationSyntax AppendCleanupCall(MethodDeclarationSyntax method)
    {
        if (method.Body is { } body)
            return method.WithBody(body.AddStatements(CleanupStatement()));

        if (method.ExpressionBody is not { Expression: { } expression })
            return method
                .WithBody(SyntaxFactory.Block(CleanupStatement()))
                .WithExpressionBody(null)
                .WithSemicolonToken(default);

        var statement = SyntaxFactory.ExpressionStatement(expression);
        return method
            .WithExpressionBody(null)
            .WithSemicolonToken(default)
            .WithBody(SyntaxFactory.Block(statement, CleanupStatement()));
    }

    static bool ContainsCleanupInvocation(MethodDeclarationSyntax method)
        => method
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(IsCleanupInvocation);

    static bool IsCleanupInvocation(InvocationExpressionSyntax invocation)
    {
        if (invocation.ArgumentList.Arguments.Count != 0)
            return false;

        if (invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "Cleanup" })
            return true;

        return invocation.Expression is MemberAccessExpressionSyntax
        {
            Name.Text: "Cleanup",
            Expression: ThisExpressionSyntax or BaseExpressionSyntax
        };
    }

    static StatementSyntax CleanupStatement()
        => SyntaxFactory.ExpressionStatement(
            SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName("Cleanup"))
        );

}
