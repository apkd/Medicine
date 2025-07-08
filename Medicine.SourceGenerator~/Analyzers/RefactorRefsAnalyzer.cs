using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

#pragma warning disable RS2008

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RefactorRefsAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor MED007 = new(
        id: nameof(MED007),
        title: "Cache component reference in Awake",
        messageFormat: "Call to '{0}' can be cached with [Inject]",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor MED008 = new(
        id: nameof(MED008),
        title: "Replace with a cached component reference",
        messageFormat: "Use the cached property instead of a '{0}' call",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }
        = ImmutableArray.Create(MED007, MED008);

    static readonly ImmutableHashSet<string> TargetMethodNames
        = ImmutableHashSet.Create(
            "GetComponent",
            "GetComponents",
            "GetComponentInChildren",
            "GetComponentsInChildren",
            "GetComponentInParent",
            "GetComponentsInParent"
        );

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocationExpr = (InvocationExpressionSyntax)context.Node;

        // ignore expressions that refer to local variables or parameters - can't easily refactor those
        foreach (var identifierName in invocationExpr.DescendantNodes().OfType<IdentifierNameSyntax>().Where(x => x.Parent is not NameColonSyntax))
            if (context.SemanticModel.GetSymbolInfo(identifierName, context.CancellationToken).Symbol is ILocalSymbol or IParameterSymbol)
                return;

        if (context.SemanticModel.GetSymbolInfo(invocationExpr, context.CancellationToken).Symbol is not IMethodSymbol methodSymbol)
            return;

        // check if the method is one of the targeted methods
        {
            if (!TargetMethodNames.Contains(methodSymbol.Name))
                return;

            if (!methodSymbol.ContainingType.Is("global::UnityEngine.Component"))
                return;

            if (methodSymbol.IsStatic)
                return;
        }

        // check if the containing method is marked with [Inject]
        {
            if (invocationExpr.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault() is not { } containingMethod)
                return;

            if (containingMethod.Modifiers.Any(SyntaxKind.StaticKeyword))
                return;

            // fast attribute name test
            if (containingMethod.HasAttribute(x => x is "Inject" or "InjectAttribute" or "Medicine.Inject" or "Medicine.InjectAttribute"))
                return;
        }

        if (invocationExpr.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault() is not { } classDecl)
            return; // no parent class decl?

        var injectionMethod = classDecl.Members
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m =>
                (m.Identifier.Text == "Awake" && !m.ParameterList.Parameters.Any()) ||
                m.HasAttribute(x => x is "Inject" or "InjectAttribute" or "Medicine.Inject" or "Medicine.InjectAttribute")
            );

        // can create cache property -- report fixable diagnostic
        if (injectionMethod is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(MED007, invocationExpr.GetLocation(), invocationExpr.ToString()));
            return;
        }

        var assignmentToProperty = injectionMethod.Body?.Statements
            .OfType<ExpressionStatementSyntax>()
            .Select(x => x.Expression as AssignmentExpressionSyntax)
            .FirstOrDefault(x => x is { Left: IdentifierNameSyntax identifier });

        if (assignmentToProperty is null)
            return;

        var initializer = assignmentToProperty.Right switch
        {
            ObjectCreationExpressionSyntax { Initializer: { } x }         => x,
            ImplicitObjectCreationExpressionSyntax { Initializer: { } x } => x,
            _                                                             => null,
        };

        // find existing initialization expression
        if (initializer?.Expressions.OfType<AssignmentExpressionSyntax>().Any(a => SyntaxFactory.AreEquivalent(a.Right, invocationExpr)) is true)
            return;

        // can add to existing cache property -- report fixable diagnostic
        context.ReportDiagnostic(Diagnostic.Create(MED008, invocationExpr.GetLocation(), invocationExpr.ToString()));
    }
}