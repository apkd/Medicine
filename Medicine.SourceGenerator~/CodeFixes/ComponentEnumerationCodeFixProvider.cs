using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

[Shared]
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ComponentEnumerationCodeFixProvider))]
public sealed class ComponentEnumerationCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(ComponentEnumerationAnalyzer.MED010.Id);

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document
            .GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);

        var diagnostic = context.Diagnostics.First();

        if (root!.FindNode(diagnostic.Location.SourceSpan) is not InvocationExpressionSyntax node)
            return;

        context.RegisterCodeFix(
            Microsoft.CodeAnalysis.CodeActions.CodeAction.Create(
                title: $"Use {diagnostic.Properties["Replacement"]}",
                createChangedDocument: ct => ApplyFixAsync(context.Document, node, ct),
                equivalenceKey: "UseEnumerateComponents"
            ),
            diagnostic
        );
    }

    static async Task<Document> ApplyFixAsync(Document doc, InvocationExpressionSyntax target, CancellationToken ct)
    {
        var editor = await DocumentEditor.CreateAsync(doc, ct)
            .ConfigureAwait(false);

        static SyntaxToken ReplaceMethodName(SyntaxToken oldId)
            => SyntaxFactory.Identifier(oldId.ValueText.Replace("GetComponents", "EnumerateComponents"));

        var newExpr = target.Expression switch
        {
            // receiver.GetComponents<…>()
            MemberAccessExpressionSyntax { Name: var name } ma
                => ma.WithName(name.WithIdentifier(ReplaceMethodName(name.Identifier))),

            // GetComponents<…>()  ––   add explicit receiver: this.EnumerateComponents<…>()
            GenericNameSyntax gn
                => SyntaxFactory.MemberAccessExpression(
                    kind: SyntaxKind.SimpleMemberAccessExpression,
                    expression: SyntaxFactory.ThisExpression(),
                    operatorToken: SyntaxFactory.Token(SyntaxKind.DotToken),
                    name: gn.WithIdentifier(ReplaceMethodName(gn.Identifier))
                ),

            // GetComponents()  ––   add explicit receiver: this.EnumerateComponents()
            IdentifierNameSyntax idn
                => SyntaxFactory.MemberAccessExpression(
                    kind: SyntaxKind.SimpleMemberAccessExpression,
                    expression: SyntaxFactory.ThisExpression(),
                    operatorToken: SyntaxFactory.Token(SyntaxKind.DotToken),
                    name: idn.WithIdentifier(ReplaceMethodName(idn.Identifier))
                ),

            _ => throw new InvalidOperationException("Unexpected invocation expression shape."),
        };

        editor.ReplaceNode(target, target.WithExpression(newExpr).WithTriviaFrom(target));
        editor.EnsureNamespaceIsImported("Medicine");
        return editor.GetChangedDocument();
    }
}
