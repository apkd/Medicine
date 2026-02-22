using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using static Microsoft.CodeAnalysis.SymbolDisplayFormat;

#pragma warning disable RS2008

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InjectCleanupAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor MED034 = new(
        id: nameof(MED034),
        title: "Generated Cleanup() is not called",
        messageFormat: "Type '{0}' uses [Inject] cleanup expressions but does not call generated Cleanup().",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "When [Inject] uses .Cleanup(...), call generated Cleanup() from teardown (for example OnDestroy or Dispose)."
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(MED034);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        context.RegisterSymbolAction(
            AnalyzeNamedType,
            SymbolKind.NamedType
        );
    }

    static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        if (context.Symbol is not INamedTypeSymbol { TypeKind: TypeKind.Class } typeSymbol)
            return;

        if (!UsesInjectCleanup(typeSymbol, context.CancellationToken))
            return;

        if (ContainsGeneratedCleanupCall(typeSymbol, context.CancellationToken))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(
                descriptor: MED034,
                location: typeSymbol.Locations.FirstOrDefault() ?? Location.None,
                messageArgs: typeSymbol.ToDisplayString(MinimallyQualifiedFormat)
            )
        );
    }

    static bool UsesInjectCleanup(
        INamedTypeSymbol typeSymbol,
        CancellationToken ct
    )
    {
        foreach (var syntaxRef in typeSymbol.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax(ct) is not TypeDeclarationSyntax typeDeclaration)
                continue;

            foreach (var member in EnumerateExecutableMembers(typeDeclaration))
            {
                if (member is MethodDeclarationSyntax method
                    && method.HasAttribute(MatchInjectAttribute)
                    && ContainsCleanupExtensionInvocation(method))
                    return true;

                foreach (var localFunction in member.DescendantNodes().OfType<LocalFunctionStatementSyntax>())
                    if (localFunction.HasAttribute(MatchInjectAttribute))
                        if (ContainsCleanupExtensionInvocation(localFunction))
                            return true;
            }
        }

        return false;
    }

    static bool ContainsGeneratedCleanupCall(INamedTypeSymbol typeSymbol, CancellationToken ct)
    {
        foreach (var syntaxRef in typeSymbol.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax(ct) is not TypeDeclarationSyntax typeDeclaration)
                continue;

            foreach (var member in EnumerateExecutableMembers(typeDeclaration))
                if (ContainsCleanupCall(member))
                    return true;
        }

        return false;
    }

    static bool ContainsCleanupExtensionInvocation(SyntaxNode node)
    {
        foreach (var invocation in node.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.ArgumentList.Arguments.Count != 1)
                continue;

            if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Text: "Cleanup" })
                continue;

            return true;
        }

        return false;
    }

    static bool ContainsCleanupCall(SyntaxNode member)
    {
        foreach (var invocation in member.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.ArgumentList.Arguments.Count != 0)
                continue;

            bool matchCleanup = invocation.Expression
                is IdentifierNameSyntax { Identifier.ValueText: "Cleanup" }
                or MemberAccessExpressionSyntax
                {
                    Name.Text: "Cleanup",
                    Expression: ThisExpressionSyntax or BaseExpressionSyntax
                };

            if (matchCleanup)
                return true;
        }

        return false;
    }

    static IEnumerable<SyntaxNode> EnumerateExecutableMembers(TypeDeclarationSyntax typeDeclaration)
    {
        foreach (var member in typeDeclaration.Members)
        {
            switch (member)
            {
                case MethodDeclarationSyntax:
                case ConstructorDeclarationSyntax:
                case DestructorDeclarationSyntax:
                case OperatorDeclarationSyntax:
                case ConversionOperatorDeclarationSyntax:
                    yield return member;
                    break;
            }
        }
    }

    static bool MatchInjectAttribute(NameSyntax name)
        => name.MatchesQualifiedNamePattern("Medicine.InjectAttribute", namespaceSegments: 1, skipEnd: "Attribute");
}
