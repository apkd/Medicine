using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Editing;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

#pragma warning disable RS2008

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ComponentEnumerationAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor MED010 =
        new(
            id: nameof(MED010),
            title: "Use EnumerateComponents instead of GetComponents when enumerating",
            messageFormat: "Replace '{0}' with '{1}' to avoid array allocation",
            category: "Performance",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true
        );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }
        = ImmutableArray.Create(MED010);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var model = context.SemanticModel;

        if (!IsPotentialTarget(invocation))
            return;

        if (!IsUnityGetComponentsCall(model, invocation, context.CancellationToken))
            return;

        if (!IsDirectlyEnumerated(invocation))
            return;

        var methodName = GetInvokedName(invocation);
        var replacement = methodName.Replace("GetComponents", "EnumerateComponents");

        var diagnostic
            = Diagnostic.Create(
                descriptor: MED010,
                location: invocation.GetLocation(),
                messageArgs: [methodName, replacement],
                properties: new Dictionary<string, string?>
                {
                    ["Method"] = methodName,
                    ["Replacement"] = replacement,
                }.ToImmutableDictionary()
            );

        context.ReportDiagnostic(diagnostic);
        return;

        static bool IsPotentialTarget(InvocationExpressionSyntax inv)
            => GetInvokedName(inv).Contains("GetComponents");

        static string GetInvokedName(InvocationExpressionSyntax inv) =>
            inv.Expression switch
            {
                MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
                GenericNameSyntax gn     => gn.Identifier.ValueText,
                IdentifierNameSyntax idn => idn.Identifier.ValueText,

                _ => string.Empty,
            };

        static bool IsUnityGetComponentsCall(SemanticModel sm, InvocationExpressionSyntax inv, CancellationToken ct)
        {
            if (sm.GetSymbolInfo(inv, ct).Symbol is not IMethodSymbol symbol)
                return false;

            if (!symbol.IsGenericMethod)
                return false;

            if (!symbol.Name.StartsWith("GetComponents"))
                return false;

            var containingType = symbol.ConstructedFrom.ContainingType;
            return containingType.Is("global::UnityEngine.Component") ||
                   containingType.Is("global::UnityEngine.GameObject");
        }

        static bool IsDirectlyEnumerated(InvocationExpressionSyntax inv)
            => inv.Parent switch
            {
                // foreach
                ForEachStatementSyntax foreachStmt
                    when foreachStmt.Expression == inv
                    => true,
                // linq chains
                MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax }
                    => true,
                _ => false,
            };
    }
}

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
            => Identifier(oldId.ValueText.Replace("GetComponents", "EnumerateComponents"));

        var newExpr = target.Expression switch
        {
            // receiver.GetComponents<…>()
            MemberAccessExpressionSyntax { Name: var name } ma
                => ma.WithName(name.WithIdentifier(ReplaceMethodName(name.Identifier))),

            // GetComponents<…>()  ––   add explicit receiver: this.EnumerateComponents<…>()
            GenericNameSyntax gn
                => MemberAccessExpression(
                    kind: SyntaxKind.SimpleMemberAccessExpression,
                    expression: ThisExpression(),
                    operatorToken: Token(SyntaxKind.DotToken),
                    name: gn.WithIdentifier(ReplaceMethodName(gn.Identifier))
                ),

            // GetComponents()  ––   add explicit receiver: this.EnumerateComponents()
            IdentifierNameSyntax idn
                => MemberAccessExpression(
                    kind: SyntaxKind.SimpleMemberAccessExpression,
                    expression: ThisExpression(),
                    operatorToken: Token(SyntaxKind.DotToken),
                    name: idn.WithIdentifier(ReplaceMethodName(idn.Identifier))
                ),

            _ => throw new InvalidOperationException("Unexpected invocation expression shape."),
        };

        editor.ReplaceNode(target, target.WithExpression(newExpr).WithTriviaFrom(target));
        return editor.GetChangedDocument();
    }
}