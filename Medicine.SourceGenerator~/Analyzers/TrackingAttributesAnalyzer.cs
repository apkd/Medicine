using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using static Microsoft.CodeAnalysis.SymbolDisplayFormat;

#pragma warning disable RS2008

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TrackingAttributesAnalyzer : DiagnosticAnalyzer
{
    static readonly DiagnosticDescriptor MED001 = new(
        id: nameof(MED001),
        title: "Class must use [Singleton] attribute",
        messageFormat:
        "{1} '{0}' must be marked with [Medicine.Singleton] to be used with Find.Singleton<T>()",
        category: "Medicine",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The class used in T in Find.Singleton<T>() must be marked with [Singleton]."
    );

    static readonly DiagnosticDescriptor MED002 = new(
        id: nameof(MED002),
        title: "Class must use [Track] attribute",
        messageFormat:
        "{1} '{0}' must be marked with [Medicine.Track] to be used with Find.Instances<T>().",
        category: "Medicine",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The class used in Find.Instances<T>() must be marked with [Track]."
    );

    static readonly DiagnosticDescriptor MED003 = new(
        id: nameof(MED003),
        title: "UnityEngine classes are not compatible with {1}",
        messageFormat:
        """
        Class '{0}' is a built-in Unity class, and it can't be used with {1}.
        You can only use it with custom classes marked with the [Medicine.{2}] attribute.
        """,
        category: "Medicine",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The class used Find.Instances<T> or Find.Singleton<T> must not be a UnityEngine class."
    );

    static readonly DiagnosticDescriptor MED004 = new(
        id: nameof(MED004),
        title: "Class cannot use both [Singleton] and [Track]",
        messageFormat:
        """
        Class '{0}' cannot have both [Medicine.Singleton] and [Medicine.Track] attributes.
        The [Singleton] attribute is used to register the class as a singleton, and the [Track] attribute is used to track a collection of multiple instances.
        """,
        category: "Medicine",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The class cannot have both of the [Singleton] and [Track] attributes."
    );

    static readonly DiagnosticDescriptor MED005 = new(
        id: nameof(MED005),
        title: "Class implementing a tracked interface must also be marked as tracked",
        messageFormat: "Class '{0}' implements tracked interface '{1}', but it won't be tracked unless it is also marked with [{2}].",
        category: "Medicine",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A class that implements an interface with a [Track] or [Singleton] attribute is expected to be tracked itself."
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }
        = ImmutableArray.Create(MED001, MED002, MED003, MED004, MED005);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(
            syntaxContext => AnalyzeInvocation(syntaxContext, MED001, "Singleton", Constants.SingletonAttributeFQN, "global::Medicine.Find"),
            SyntaxKind.InvocationExpression
        );

        context.RegisterSyntaxNodeAction(
            syntaxContext => AnalyzeInvocation(syntaxContext, MED002, "Instances", Constants.TrackAttributeFQN, "global::Medicine.Find"),
            SyntaxKind.InvocationExpression
        );

        context.RegisterSymbolAction(
            symbolContext =>
            {
                var namedTypeSymbol = (INamedTypeSymbol)symbolContext.Symbol;

                // Incompatible attributes check
                var hasSingleton = namedTypeSymbol.HasAttribute(Constants.SingletonAttributeFQN);
                var hasTrack = namedTypeSymbol.HasAttribute(Constants.TrackAttributeFQN);

                if (hasSingleton && hasTrack)
                {
                    symbolContext.ReportDiagnostic(
                        Diagnostic.Create(
                            MED004,
                            namedTypeSymbol.Locations.First(),
                            namedTypeSymbol.Name
                        )
                    );
                }

                if (namedTypeSymbol.TypeKind != TypeKind.Class)
                    return;

                if (!hasTrack && !namedTypeSymbol.GetBaseTypes().Any(x => x.HasAttribute(Constants.TrackAttributeFQN)))
                {
                    foreach (var @interface in namedTypeSymbol.Interfaces)
                    {
                        if (!@interface.HasAttribute(Constants.TrackAttributeFQN))
                            continue;

                        symbolContext.ReportDiagnostic(
                            Diagnostic.Create(
                                descriptor: MED005,
                                location: namedTypeSymbol.Locations.First(),
                                namedTypeSymbol.ToDisplayString(MinimallyQualifiedFormat),
                                @interface.ToDisplayString(MinimallyQualifiedFormat),
                                "Track"
                            )
                        );

                        return;
                    }
                }

                if (!hasSingleton)
                {
                    foreach (var @interface in namedTypeSymbol.Interfaces)
                    {
                        if (!@interface.HasAttribute(Constants.SingletonAttributeFQN))
                            continue;

                        symbolContext.ReportDiagnostic(
                            Diagnostic.Create(
                                descriptor: MED005,
                                location: namedTypeSymbol.Locations.First(),
                                namedTypeSymbol.ToDisplayString(MinimallyQualifiedFormat),
                                @interface.ToDisplayString(MinimallyQualifiedFormat),
                                "Singleton"
                            )
                        );

                        return;
                    }
                }
            },
            SymbolKind.NamedType
        );
    }

    static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        DiagnosticDescriptor diagnosticDescriptor,
        string methodName,
        string attributeFQN,
        string medicineFindFQN
    )
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                Name: GenericNameSyntax
                {
                    Identifier.ValueText : { } valueText,
                    TypeArgumentList.Arguments: [{ } typeArgumentSyntax],
                },
            })
            return;

        if (valueText != methodName)
            return;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);

        if ((symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault()) is not IMethodSymbol
            {
                OriginalDefinition:
                {
                    TypeParameters.Length: 1,
                    ContainingType : { } containingType,
                } methodSymbol,
            })
            return;

        if (!containingType.Is(medicineFindFQN))
            return;

        if (context.SemanticModel.GetTypeInfo(typeArgumentSyntax).Type is not INamedTypeSymbol typeArgument)
            return;

        if (typeArgument.IsUnboundGenericType)
            return;

        if (!typeArgument.HasAttribute(attributeFQN))
        {
            if (typeArgument.ContainingNamespace?.ToDisplayString().StartsWith("UnityEngine", StringComparison.Ordinal) ?? false)
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        MED003,
                        typeArgumentSyntax.GetLocation(),
                        typeArgument.ToDisplayString(MinimallyQualifiedFormat),
                        methodSymbol.ToDisplayString(CSharpShortErrorMessageFormat),
                        attributeFQN.Replace(nameof(Attribute), "")
                    )
                );
            }
            else
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        diagnosticDescriptor,
                        typeArgumentSyntax.GetLocation(),
                        typeArgument.ToDisplayString(MinimallyQualifiedFormat),
                        typeArgument.TypeKind is TypeKind.Interface ? "Interface" : "Class"
                    )
                );
            }
        }
    }
}