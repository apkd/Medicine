using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

#pragma warning disable RS2008

[DiagnosticAnalyzer(LanguageNames.CSharp)]
sealed class FindObjectsAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor MED011 =
        new(
            id: nameof(MED011),
            title: "Replace FindObjectsOfType<T>()",
            messageFormat: "Replace FindObjectsOfType<T>() with faster alternative",
            category: "Performance",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true
        );

    public static readonly DiagnosticDescriptor MED013 =
        new(
            id: nameof(MED013),
            title: "Replace singleton access",
            messageFormat: "Replace {0} with direct singleton access",
            category: "Performance",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true
        );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(MED011, MED013);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(startContext =>
        {
            var knownSymbols = new KnownSymbols(startContext.Compilation);
            startContext.RegisterSyntaxNodeAction(
                action: syntaxContext => AnalyzeInvocation(syntaxContext, knownSymbols),
                syntaxKinds: SyntaxKind.InvocationExpression
            );
        });
    }

    static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, KnownSymbols knownSymbols)
    {
        if (context.Node is not InvocationExpressionSyntax invocation)
            return;

        var nameSyntax = invocation.Expression switch
        {
            GenericNameSyntax x                                        => x,
            MemberAccessExpressionSyntax { Name: GenericNameSyntax x } => x,
            _                                                          => null,
        };

        if (nameSyntax is null)
            return;

        if (context.SemanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol methodSymbol)
            return;

        var methodName = nameSyntax.Identifier.ValueText;

        if (methodName is "FindObjectOfType" or "FindFirstObjectByType" or "FindAnyObjectByType")
        {
            if (!methodSymbol.ContainingType.Is(knownSymbols.UnityObject))
                return;

            if (methodSymbol.TypeArguments.FirstOrDefault()?.HasAttribute(knownSymbols.SingletonAttribute) == true)
                context.ReportDiagnostic(Diagnostic.Create(MED013, invocation.GetLocation(), invocation.ToString()));

            return;
        }

        if (methodName is "FindObjectsOfType" or "FindObjectsByType" or "ObjectsByType")
            if (IsTarget(methodSymbol, invocation, knownSymbols))
                context.ReportDiagnostic(Diagnostic.Create(MED011, invocation.GetLocation()));
    }

    static bool IsTarget(IMethodSymbol methodSymbol, InvocationExpressionSyntax invocation, KnownSymbols knownSymbols)
    {
        if (!methodSymbol.IsGenericMethod)
            return false;

        bool IsTracked()
            => methodSymbol.TypeArguments.FirstOrDefault()?.HasAttribute(knownSymbols.TrackAttribute) is true;

        if (methodSymbol.ContainingType.Is(knownSymbols.UnityObject))
            if (methodSymbol.Name is "FindObjectsOfType" or "FindObjectsByType")
                return true;

        bool IsIncludeInactive()
            => invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression
                is LiteralExpressionSyntax { Token.ValueText: "true" }
                or MemberAccessExpressionSyntax { Name.Identifier.Text: "Include" };

        if (methodSymbol.ContainingType.Is(knownSymbols.MedicineFind))
            if (methodSymbol.Name is "ObjectsByType")
                if (IsTracked())
                    if (!IsIncludeInactive())
                        return true;

        return false;
    }
}
