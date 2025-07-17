using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

#pragma warning disable RS2008

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LazyAnalyzer : DiagnosticAnalyzer
{
    static readonly DiagnosticDescriptor Rule = new(
        id: "MED016",
        title: "Invalid argument for Lazy.From",
        messageFormat: "The argument to Lazy.From needs to be a lambda expression or a method group (matching Func<T>).",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Ensures that only lambda expressions or method groups are passed to Lazy.From methods."
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }
        = ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        bool matchesShape = invocation is
        {
            ArgumentList.Arguments: [not null],
            Expression: MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "From",
                Expression: SimpleNameSyntax { Identifier.ValueText: "Lazy" },
            },
        };

        if (!matchesShape)
            return;

        var argExpr = invocation.ArgumentList.Arguments[0].Expression;

        if (argExpr is ParenthesizedLambdaExpressionSyntax or SimpleLambdaExpressionSyntax)
            return; // allow lambda expressions

        bool matchesSymbol = context
            .SemanticModel
            .GetSymbolInfo(invocation, context.CancellationToken)
            .CandidateSymbols
            .FirstOrDefault() is IMethodSymbol
        {
            Name: "From",
            ContainingType:
            {
                Name: "Lazy",
                ContainingNamespace.Name: "Medicine",
            },
        };

        if (!matchesSymbol)
            return;

        if (context.SemanticModel.GetConversion(argExpr, context.CancellationToken).IsMethodGroup)
            return; // allow method groups

        var diagnostic = Diagnostic.Create(Rule, invocation.GetLocation());
        context.ReportDiagnostic(diagnostic);
    }
}