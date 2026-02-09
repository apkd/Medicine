using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

#pragma warning disable RS2008

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NullComparisonAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor MED026 = new(
        id: nameof(MED026),
        title: "Use faster IsNull extension method",
        messageFormat: "Use faster IsNull extension method",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor MED027 = new(
        id: nameof(MED027),
        title: "Use faster IsNotNull extension method",
        messageFormat: "Use faster IsNotNull extension method",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(MED026, MED027);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(startContext =>
            {
                if (!HasExtensionsSymbol(startContext.Compilation))
                    return;

                var knownSymbols = new KnownSymbols(startContext.Compilation);
                startContext.RegisterSyntaxNodeAction(
                    syntaxContext => AnalyzeBinary(syntaxContext, knownSymbols.UnityObject),
                    SyntaxKind.EqualsExpression,
                    SyntaxKind.NotEqualsExpression
                );

                startContext.RegisterSyntaxNodeAction(
                    syntaxContext => AnalyzeIsPattern(syntaxContext, knownSymbols.UnityObject),
                    SyntaxKind.IsPatternExpression
                );
            }
        );
    }

    static bool HasExtensionsSymbol(Compilation compilation)
    {
        foreach (var tree in compilation.SyntaxTrees)
            if (tree.Options is CSharpParseOptions options)
                foreach (var name in options.PreprocessorSymbolNames)
                    if (name is Constants.MedicineExtensionsDefine)
                        return true;

        return false;
    }

    static void AnalyzeBinary(SyntaxNodeAnalysisContext context, INamedTypeSymbol unityObjectSymbol)
    {
        if (context.Node is not BinaryExpressionSyntax binary)
            return;

        if (!TryGetUnityOperand(binary.Left, binary.Right, context.SemanticModel, context.CancellationToken, unityObjectSymbol, out _))
            return;

        var descriptor = binary.IsKind(SyntaxKind.EqualsExpression) ? MED026 : MED027;
        context.ReportDiagnostic(Diagnostic.Create(descriptor, binary.GetLocation()));
    }

    static void AnalyzeIsPattern(SyntaxNodeAnalysisContext context, INamedTypeSymbol unityObjectSymbol)
    {
        if (context.Node is not IsPatternExpressionSyntax isPattern)
            return;

        var pattern = UnwrapParenthesizedPattern(isPattern.Pattern);

        if (IsNullPattern(pattern))
        {
            if (IsUnityObjectExpression(isPattern.Expression, context.SemanticModel, context.CancellationToken, unityObjectSymbol))
                context.ReportDiagnostic(Diagnostic.Create(MED026, isPattern.GetLocation()));
        }
        else if (IsNotNullPattern(pattern))
        {
            if (IsUnityObjectExpression(isPattern.Expression, context.SemanticModel, context.CancellationToken, unityObjectSymbol))
                context.ReportDiagnostic(Diagnostic.Create(MED027, isPattern.GetLocation()));
        }
    }

    static bool TryGetUnityOperand(
        ExpressionSyntax left,
        ExpressionSyntax right,
        SemanticModel model,
        CancellationToken ct,
        INamedTypeSymbol unityObjectSymbol,
        out ExpressionSyntax unityOperand
    )
    {
        unityOperand = null!;

        if (IsNullLiteral(left))
        {
            if (IsUnityObjectExpression(right, model, ct, unityObjectSymbol))
            {
                unityOperand = right;
                return true;
            }

            return false;
        }

        if (IsNullLiteral(right))
        {
            if (!IsUnityObjectExpression(left, model, ct, unityObjectSymbol))
                return false;

            unityOperand = left;
            return true;
        }

        return false;
    }

    static bool IsNullLiteral(ExpressionSyntax expression)
        => (UnwrapParenthesizedExpression(expression)).IsKind(SyntaxKind.NullLiteralExpression);

    static bool IsNullPattern(PatternSyntax pattern)
        => pattern is ConstantPatternSyntax
        {
            Expression: LiteralExpressionSyntax { RawKind: (int)SyntaxKind.NullLiteralExpression },
        };

    static bool IsNotNullPattern(PatternSyntax pattern)
        => pattern is UnaryPatternSyntax { Pattern: var inner }
           && IsNullPattern(UnwrapParenthesizedPattern(inner));

    static ExpressionSyntax UnwrapParenthesizedExpression(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax { Expression: var inner })
            expression = inner;

        return expression;
    }

    static PatternSyntax UnwrapParenthesizedPattern(PatternSyntax pattern)
    {
        while (pattern is ParenthesizedPatternSyntax { Pattern: var inner })
            pattern = inner;

        return pattern;
    }

    static bool IsUnityObjectExpression(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken ct,
        INamedTypeSymbol unityObjectSymbol
    )
    {
        var typeInfo = model.GetTypeInfo(expression, ct);
        var type = typeInfo.Type ?? typeInfo.ConvertedType;
        return IsUnityObjectType(type, unityObjectSymbol);
    }

    static bool IsUnityObjectType(ITypeSymbol? type, INamedTypeSymbol unityObjectSymbol)
    {
        switch (type)
        {
            case null or IErrorTypeSymbol:
            {
                return false;
            }
            case ITypeParameterSymbol typeParameter:
            {
                foreach (var constraint in typeParameter.ConstraintTypes)
                    if (IsUnityObjectType(constraint, unityObjectSymbol))
                        return true;

                return false;
            }
            default:
            {
                return type.Is(unityObjectSymbol) || type.InheritsFrom(unityObjectSymbol);
            }
        }
    }
}
