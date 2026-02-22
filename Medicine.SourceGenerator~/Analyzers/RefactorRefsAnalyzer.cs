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
        title: "Cache component reference",
        messageFormat: "Call to '{0}' can be cached",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor MED008 = new(
        id: nameof(MED008),
        title: "Replace with an existing cached component reference",
        messageFormat: "Use the existing cached property instead of a '{0}' call",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor MED009 = new(
        id: nameof(MED009),
        title: "Add an Awake() method and cache component reference",
        messageFormat: "Call to '{0}' can be cached",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }
        = ImmutableArray.Create(MED007, MED008, MED009);

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
        var containingMethod = invocationExpr.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        var containingLocalFunction = invocationExpr.Ancestors().OfType<LocalFunctionStatementSyntax>().FirstOrDefault();

        if (containingMethod is null && containingLocalFunction is null)
            return;

        // ignore static methods
        if (containingMethod?.Modifiers.IsStatic is true)
            return;

        // syntax-only guard to avoid expensive symbol resolution in [Inject] scopes
        if (containingMethod.HasAttribute(IsInjectAttributeName))
            return;

        if (containingLocalFunction.HasAttribute(IsInjectAttributeName))
            return;

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

        // ignore situations when the call already assigns to a field/property
        if (invocationExpr.Parent is AssignmentExpressionSyntax aes && aes.Right == invocationExpr)
            if (context.SemanticModel.GetSymbolInfo(aes.Left, context.CancellationToken).Symbol is IFieldSymbol or IPropertySymbol)
                return;

        if (invocationExpr.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault() is not { } classDecl)
            return; // no parent class decl?

        var injectionMethod
            = classDecl.Members
                  .OfType<MethodDeclarationSyntax>()
                  .FirstOrDefault(m => m.HasAttribute(IsInjectAttributeName)) ??
              classDecl.Members
                  .OfType<MethodDeclarationSyntax>()
                  .FirstOrDefault(m => m.Identifier.Text == "Awake" && !m.ParameterList.Parameters.Any());

        var injectionLocalFunction = classDecl
            .DescendantNodes()
            .OfType<LocalFunctionStatementSyntax>()
            .FirstOrDefault(m => m.HasAttribute(IsInjectAttributeName));

        if (injectionMethod is null && injectionLocalFunction is null)
        {
            // needs a new [Inject] method -- report fixable diagnostic
            context.ReportDiagnostic(Diagnostic.Create(MED009, invocationExpr.GetLocation(), invocationExpr.ToString()));
            return;
        }

        var assignmentsToProperty
            = (injectionMethod?.Body?.Statements ?? [])
            .Concat(injectionLocalFunction?.Body?.Statements ?? [])
            .OfType<ExpressionStatementSyntax>()
            .Select(x => x.Expression as AssignmentExpressionSyntax)
            .Where(x => x is { Left: IdentifierNameSyntax })
            .Select(x => x!.Right)
            .ToArray();

        if (assignmentsToProperty.Any(x => SyntaxFactory.AreEquivalent(x, invocationExpr)))
        {
            // has existing initialization expression - already cached, can merge
            context.ReportDiagnostic(Diagnostic.Create(MED008, invocationExpr.GetLocation(), invocationExpr.ToString()));
        }
        else
        {
            // can create cache property - report fixable diagnostic
            context.ReportDiagnostic(Diagnostic.Create(MED007, invocationExpr.GetLocation(), invocationExpr.ToString()));
        }
    }

    static bool IsInjectAttributeName(NameSyntax name)
        => name.MatchesQualifiedNamePattern("Medicine.InjectAttribute", namespaceSegments: 1, skipEnd: "Attribute");
}
