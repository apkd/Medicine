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

            var symbol = symbolInfo.Symbol as IMethodSymbol
                         ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();

            return symbol is
            {
                Name: "Optional",
                ContainingType:
                {
                    Name: "MedicineExtensions",
                    ContainingNamespace: { Name: "Medicine", ContainingNamespace.IsGlobalNamespace: true },
                }
            };
        }

        static bool IsInsideInjectMethod(SyntaxNode node, SemanticModel model, CancellationToken ct)
        {
            foreach (var ancestor in node.AncestorsAndSelf())
            {
                switch (ancestor)
                {
                    case MethodDeclarationSyntax method when HasInjectAttribute(method, model, ct):
                    case LocalFunctionStatementSyntax localFunction when HasInjectAttribute(localFunction, model, ct):
                        return true;
                }
            }

            return false;
        }
    }

    static bool HasInjectAttribute(MethodDeclarationSyntax method, SemanticModel model, CancellationToken ct)
        => method.HasAttribute(IsInjectAttributeName) ||
           model.GetDeclaredSymbol(method, ct) is { } symbol && symbol.HasAttribute(Constants.InjectAttributeFQN);

    static bool HasInjectAttribute(LocalFunctionStatementSyntax localFunction, SemanticModel model, CancellationToken ct)
        => localFunction.HasAttribute(IsInjectAttributeName) ||
           model.GetDeclaredSymbol(localFunction, ct) is IMethodSymbol symbol && symbol.HasAttribute(Constants.InjectAttributeFQN);

    static bool IsInjectAttributeName(string name)
        => name is "Inject" or "InjectAttribute" or "Medicine.Inject" or "Medicine.InjectAttribute";
}