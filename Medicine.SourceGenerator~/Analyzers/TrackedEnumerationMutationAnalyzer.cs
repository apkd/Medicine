using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

#pragma warning disable RS2008

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TrackedEnumerationMutationAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor MED035 = new(
        id: nameof(MED035),
        title: "Tracked enumeration is mutated during iteration",
        messageFormat: "'{0}' may invalidate tracked enumeration. Use '.WithCopy' before mutating tracked instances.",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Mutating tracked registration/enabled state during enumeration can invalidate iteration."
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(MED035);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        var knownSymbols = new KnownSymbols(context.Compilation);

        context.RegisterSyntaxNodeAction(
            x => AnalyzeForEach(x, knownSymbols),
            SyntaxKind.ForEachStatement
        );
    }

    static void AnalyzeForEach(SyntaxNodeAnalysisContext context, KnownSymbols knownSymbols)
    {
        if (context.Node is not ForEachStatementSyntax forEach)
            return;

        var semanticModel = context.SemanticModel;
        var ct = context.CancellationToken;
        bool hasWithCopy = false;

        if (!TryGetTrackedEnumeration(forEach.Expression))
            return;

        if (hasWithCopy)
            return;

        if (!TryGetMutationLabel(forEach.Statement, out var mutationLabel))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(
                descriptor: MED035,
                location: forEach.Expression.GetLocation(),
                messageArgs: mutationLabel
            )
        );

        return;

        bool TryGetTrackedEnumeration(ExpressionSyntax expression)
        {
            var root = UnwrapTrackedChain(expression);
            if (!hasWithCopy && ContainsWithCopy(expression))
                hasWithCopy = true;

            if (IsTrackedType(root))
                return true;

            if (root is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Text: "Instances", Expression: { } receiver } })
                return semanticModel.GetSymbolInfo(receiver, ct).Symbol is ITypeSymbol findType && findType.Is(knownSymbols.MedicineFind);

            if (root is MemberAccessExpressionSyntax { Name.Text: "Instances", Expression: { } receiverExpression })
                if (semanticModel.GetTypeInfo(receiverExpression, ct).Type is INamedTypeSymbol receiverType)
                    return receiverType.HasAttribute(knownSymbols.TrackAttribute);

            return false;
        }

        ExpressionSyntax UnwrapTrackedChain(ExpressionSyntax expression)
        {
            while (true)
            {
                if (expression is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax receiver })
                {
                    if (receiver is { Name.Text: "WithStride", Expression: { } strideReceiver })
                    {
                        expression = strideReceiver;
                        continue;
                    }

                    if (receiver is { Name.Text: "WithCopy", Expression: { } withCopyMethodReceiver })
                    {
                        hasWithCopy = true;
                        expression = withCopyMethodReceiver;
                        continue;
                    }
                }

                if (expression is MemberAccessExpressionSyntax { Name.Text: "WithCopy", Expression: { } withCopyReceiver })
                {
                    hasWithCopy = true;
                    expression = withCopyReceiver;
                    continue;
                }

                return expression;
            }
        }

        bool IsTrackedType(ExpressionSyntax expression)
        {
            var typeInfo = semanticModel.GetTypeInfo(expression, ct);
            var type = typeInfo.Type ?? typeInfo.ConvertedType;
            if (type is not INamedTypeSymbol namedType)
                return false;

            if (namedType.OriginalDefinition.Is(knownSymbols.TrackedInstances))
                return true;

            return namedType.ContainingType is { } containingType
                   && containingType.OriginalDefinition.Is(knownSymbols.TrackedInstances);
        }

        static bool ContainsWithCopy(ExpressionSyntax expression)
        {
            foreach (var node in expression.DescendantNodesAndSelf())
                if (node is MemberAccessExpressionSyntax { Name.Text: "WithCopy" })
                    return true;

            return false;
        }

        bool TryGetMutationLabel(StatementSyntax statement, out string label)
        {
            if (TryGetNodeMutationLabel(statement, out label))
                return true;

            foreach (var node in statement.DescendantNodes(ShouldDescend))
                if (TryGetNodeMutationLabel(node, out label))
                    return true;

            static bool ShouldDescend(SyntaxNode node)
                => node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or TypeDeclarationSyntax);

            label = "";
            return false;
        }

        bool TryGetNodeMutationLabel(SyntaxNode node, out string label)
        {
            if (node is AssignmentExpressionSyntax { Left: MemberAccessExpressionSyntax { Name.Text: "enabled" } })
            {
                label = "enabled assignment";
                return true;
            }

            if (node is not InvocationExpressionSyntax invocation)
            {
                label = "";
                return false;
            }

            string? methodName = invocation.Expression switch
            {
                MemberAccessExpressionSyntax x => x.Name.Text,
                IdentifierNameSyntax x         => x.Text,
                _                              => null,
            };

            if (methodName is null)
            {
                label = "";
                return false;
            }

            switch (methodName)
            {
                case "SetActive":
                case "RegisterInstance":
                case "UnregisterInstance":
                    label = methodName;
                    return true;
                case "Destroy":
                case "DestroyImmediate":
                    if (!IsUnityObjectDestroyInvocation(invocation))
                        break;

                    label = methodName;
                    return true;
            }

            label = "";
            return false;
        }

        bool IsUnityObjectDestroyInvocation(InvocationExpressionSyntax invocation)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(invocation, ct);
            var method = symbolInfo.Symbol as IMethodSymbol;

            if (method is null)
            {
                foreach (var candidate in symbolInfo.CandidateSymbols)
                {
                    if (candidate is IMethodSymbol candidateMethod)
                    {
                        method = candidateMethod;
                        break;
                    }
                }
            }

            return method?.Name is "Destroy" or "DestroyImmediate" && method.ContainingType.Is(knownSymbols.UnityObject);
        }
    }
}
