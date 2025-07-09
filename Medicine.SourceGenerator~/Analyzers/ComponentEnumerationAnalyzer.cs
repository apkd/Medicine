using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

#pragma warning disable RS2008

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ComponentEnumerationAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor MED010
        = new(
            id: nameof(MED010),
            title: "Use EnumerateComponents instead of GetComponents when enumerating",
            messageFormat: "Replace '{0}' with '{1}' to avoid array allocation",
            category: "Performance",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true
        );

    static readonly ImmutableHashSet<string> LinqMethodNames
        = ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "Aggregate", "All", "Any", "AsEnumerable", "AsValueEnumerable",
            "Average", "Cast", "Count", "ElementAt", "ElementAtOrDefault",
            "First", "FirstOrDefault", "GroupBy", "GroupJoin", "Join",
            "Last", "LastOrDefault", "Max", "Min", "OfType", "OrderBy",
            "OrderByDescending", "Reverse", "Select", "SelectMany",
            "SequenceEqual", "Single", "SingleOrDefault", "Skip", "Sum",
            "Take", "ThenBy", "ThenByDescending", "ToArray", "ToDictionary",
            "ToHashSet", "ToList", "ToLookup", "Where", "Zip"
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

        if (!IsUnityGetComponentsCallEarlyOut(invocation))
            return;

        if (!IsUnityGetComponentsCall(model, invocation, context.CancellationToken))
            return;

        if (!IsDirectlyEnumerated(invocation, model, context.CancellationToken))
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

        static string GetInvokedName(InvocationExpressionSyntax inv) =>
            inv.Expression switch
            {
                MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
                GenericNameSyntax gn            => gn.Identifier.ValueText,
                IdentifierNameSyntax idn        => idn.Identifier.ValueText,

                _ => string.Empty,
            };

        static bool IsUnityGetComponentsCallEarlyOut(InvocationExpressionSyntax inv)
            => GetInvokedName(inv).Contains("GetComponents");

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

        static bool IsDirectlyEnumerated(InvocationExpressionSyntax inv, SemanticModel model, CancellationToken ct)
        {
            switch (inv.Parent)
            {
                // foreach (var x in GetComponents<T>())
                case ForEachStatementSyntax foreachStmt when foreachStmt.Expression == inv:

                // GetComponents<T>().Linq...()
                case MemberAccessExpressionSyntax
                {
                    Parent: InvocationExpressionSyntax,
                    Name.Identifier.ValueText: var methodName
                } when LinqMethodNames.Contains(methodName):
                    return true;

                // var myVariable = GetComponents<T>();
                // foreach (var x in myVariable)
                case EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax variableDeclarator }:
                {
                    if (variableDeclarator.Parent?.Parent is not LocalDeclarationStatementSyntax { Parent: BlockSyntax block })
                        return false;
        
                    if (model.GetDeclaredSymbol(variableDeclarator, ct) is not ILocalSymbol localSymbol)
                        return false;
        
                    var usages = block.DescendantNodes()
                        .OfType<IdentifierNameSyntax>()
                        .Where(x => x.Identifier.ValueText == localSymbol.Name && model.GetSymbolInfo(x, ct).Symbol.Is(localSymbol))
                        .ToArray();

                    if (usages.Length != 1)
                        return false;
        
                    var usage = usages[0];
                    return usage.Parent is ForEachStatementSyntax foreachStatement && foreachStatement.Expression == usage;
                }
        
                default:
                    return false;
            }
        }
    }
}