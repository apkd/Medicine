using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

#pragma warning disable RS2008

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OptionalUsageAnalyzer : DiagnosticAnalyzer
{
    static readonly DiagnosticDescriptor MED030 = new(
        id: nameof(MED030),
        title: "Invalid Optional() usage",
        messageFormat: "{0}",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Optional() is only valid inside [Inject] methods."
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(MED030);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation)
            return;

        if (!IsMedicineOptionalInvocation(invocation, context))
            return;

        if (IsInsideInjectMethod(invocation, context.SemanticModel, context.CancellationToken))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(
                descriptor: MED030,
                location: invocation.GetLocation(),
                messageArgs: ".Optional() only has meaning when used inside an [Inject] method."
            )
        );

        return;

        static bool IsMedicineOptionalInvocation(InvocationExpressionSyntax invocation, SyntaxNodeAnalysisContext context)
        {
            if (invocation.ArgumentList.Arguments.Count != 0)
                return false;

            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                return false;

            if (memberAccess.Name.Identifier.ValueText != "Optional")
                return false;

            var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);

            var symbol = symbolInfo.Symbol as IMethodSymbol;

            if (symbol is null)
            {
                foreach (var methodSymbol in symbolInfo.CandidateSymbols)
                {
                    if (methodSymbol is IMethodSymbol x)
                    {
                        symbol = x;
                        break;
                    }
                }
            }

            return symbol is
            {
                Name: "Optional",
                ContainingType: { Name: "MedicineExtensions", IsInMedicineNamespace: true }
            };
        }

        static bool IsInsideInjectMethod(SyntaxNode node, SemanticModel model, CancellationToken ct)
        {
            foreach (var ancestor in node.AncestorsAndSelf())
            {
                switch (ancestor)
                {
                    case MethodDeclarationSyntax method when method.HasAttribute(MatchInjectAttribute):
                    case LocalFunctionStatementSyntax localFunction when localFunction.HasAttribute(MatchInjectAttribute):
                        return true;
                }
            }

            static bool MatchInjectAttribute(NameSyntax name)
                => name.MatchesQualifiedNamePattern("Medicine.InjectAttribute", namespaceSegments: 1, skipEnd: "Attribute");

            return false;
        }
    }

}
