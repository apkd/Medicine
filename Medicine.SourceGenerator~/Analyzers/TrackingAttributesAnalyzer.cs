using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using static Microsoft.CodeAnalysis.SymbolDisplayFormat;

#pragma warning disable RS2008

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TrackingAttributesAnalyzer : DiagnosticAnalyzer
{
    static readonly DiagnosticDescriptor MED001 = new(
        id: nameof(MED001),
        title: "Class must use [Singleton] attribute",
        messageFormat: "{1} '{0}' must be marked with [Medicine.Singleton] to be used with Find.Singleton<T>()",
        category: "Medicine",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The class used in T in Find.Singleton<T>() must be marked with [Singleton]."
    );

    static readonly DiagnosticDescriptor MED002 = new(
        id: nameof(MED002),
        title: "Class must use [Track] attribute",
        messageFormat: "{1} '{0}' must be marked with [Medicine.Track] to be used with Find.Instances<T>().",
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

    static readonly DiagnosticDescriptor MED033 = new(
        id: nameof(MED033),
        title: "[Track(transformAccessArray: true)] requires MonoBehaviour",
        messageFormat: "Class '{0}' uses [Track(transformAccessArray: true)] but does not inherit from UnityEngine.MonoBehaviour.",
        category: "Medicine",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "TransformAccessArray tracking is valid only for MonoBehaviour classes."
    );

    static readonly DiagnosticDescriptor MED036 = new(
        id: nameof(MED036),
        title: "Class must be partial",
        messageFormat: "Class '{0}' must be declared partial because it uses [{1}].",
        category: "Medicine",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Classes using [Track], [Singleton], or [Inject] methods must be declared partial for source generation."
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }
        = ImmutableArray.Create(MED001, MED002, MED003, MED004, MED005, MED033, MED036);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        var knownSymbols = new KnownSymbols(context.Compilation);

        context.RegisterSyntaxNodeAction(
            syntaxContext => AnalyzeInvocation(
                syntaxContext,
                knownSymbols,
                diagnosticDescriptor: MED001,
                methodName: "Singleton",
                attributeSymbol: knownSymbols.SingletonAttribute,
                attributeDisplayName: Constants.SingletonAttributeShort
            ),
            SyntaxKind.InvocationExpression
        );

        context.RegisterSyntaxNodeAction(
            syntaxContext => AnalyzeInvocation(
                syntaxContext,
                knownSymbols,
                diagnosticDescriptor: MED002,
                methodName: "Instances",
                attributeSymbol: knownSymbols.TrackAttribute,
                attributeDisplayName: Constants.TrackAttributeName.Replace(nameof(Attribute), "")
            ),
            SyntaxKind.InvocationExpression
        );

        context.RegisterSymbolAction(
            symbolContext => AnalyzeNamedType((INamedTypeSymbol)symbolContext.Symbol, symbolContext, knownSymbols),
            SymbolKind.NamedType
        );

        context.RegisterSymbolAction(
            symbolContext => AnalyzeMethod((IMethodSymbol)symbolContext.Symbol, symbolContext, knownSymbols),
            SymbolKind.Method
        );
    }

    static void AnalyzeNamedType(
        INamedTypeSymbol typeSymbol,
        SymbolAnalysisContext context,
        KnownSymbols knownSymbols
    )
    {
        var hasSingleton = typeSymbol.HasAttribute(knownSymbols.SingletonAttribute);
        var hasTrack = typeSymbol.HasAttribute(knownSymbols.TrackAttribute);

        if (hasSingleton && hasTrack)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    descriptor: MED004,
                    location: typeSymbol.Locations.FirstOrDefault() ?? Location.None,
                    messageArgs: typeSymbol.Name
                )
            );
        }

        if (typeSymbol.TypeKind is not TypeKind.Class)
            return;

        if (hasTrack)
            AnalyzeTrackUsage(typeSymbol, context, knownSymbols);

        if (hasTrack || hasSingleton)
        {
            ReportMissingPartialForClass(
                typeSymbol,
                context,
                attributeShortName: hasTrack ? "Track" : Constants.SingletonAttributeShort
            );
        }

        ReportInterfaceAttributeMismatch(
            typeSymbol,
            context,
            requiredAttribute: knownSymbols.TrackAttribute,
            attributeShortName: "Track",
            allowTrackViaBaseType: true
        );

        ReportInterfaceAttributeMismatch(
            typeSymbol,
            context,
            requiredAttribute: knownSymbols.SingletonAttribute,
            attributeShortName: Constants.SingletonAttributeShort,
            allowTrackViaBaseType: false
        );
    }

    static void AnalyzeMethod(
        IMethodSymbol methodSymbol,
        SymbolAnalysisContext context,
        KnownSymbols knownSymbols
    )
    {
        if (methodSymbol.GetAttributes().Length == 0)
            return;

        if (!methodSymbol.HasAttribute(knownSymbols.InjectAttribute))
            return;

        if (methodSymbol.MethodKind is not (MethodKind.Ordinary or MethodKind.LocalFunction))
            return;

        if (methodSymbol.ContainingType is not { TypeKind: TypeKind.Class } containingType)
            return;

        if (containingType.HasAttribute(knownSymbols.TrackAttribute))
            return;

        if (containingType.HasAttribute(knownSymbols.SingletonAttribute))
            return;

        if (!TryGetFirstNonPartialClassDeclaration(containingType, context.CancellationToken, out _))
            return;

        if (!TryGetInjectDeclarationLocation(methodSymbol, context.CancellationToken, out var location))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(
                descriptor: MED036,
                location: location,
                messageArgs:
                [
                    containingType.ToDisplayString(MinimallyQualifiedFormat),
                    "Inject",
                ]
            )
        );
    }

    static void AnalyzeTrackUsage(
        INamedTypeSymbol typeSymbol,
        SymbolAnalysisContext context,
        KnownSymbols knownSymbols
    )
    {
        var trackAttribute = typeSymbol.GetAttribute(knownSymbols.TrackAttribute);
        if (trackAttribute is null)
            return;

        bool trackTransforms = trackAttribute
            .GetAttributeConstructorArguments(context.CancellationToken)
            .Get("transformAccessArray", false);

        if (!trackTransforms || typeSymbol.InheritsFrom(knownSymbols.UnityMonoBehaviour))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(
                descriptor: MED033,
                location: trackAttribute.ApplicationSyntaxReference?.GetLocation() ?? typeSymbol.Locations.FirstOrDefault() ?? Location.None,
                messageArgs: typeSymbol.ToDisplayString(MinimallyQualifiedFormat)
            )
        );
    }

    static void ReportMissingPartialForClass(
        INamedTypeSymbol typeSymbol,
        SymbolAnalysisContext context,
        string attributeShortName
    )
    {
        if (!TryGetFirstNonPartialClassDeclaration(typeSymbol, context.CancellationToken, out var declaration))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(
                descriptor: MED036,
                location: GetTypeDeclarationHeaderLocation(declaration),
                messageArgs:
                [
                    typeSymbol.ToDisplayString(MinimallyQualifiedFormat),
                    attributeShortName,
                ]
            )
        );
    }

    static void ReportInterfaceAttributeMismatch(
        INamedTypeSymbol typeSymbol,
        SymbolAnalysisContext context,
        INamedTypeSymbol requiredAttribute,
        string attributeShortName,
        bool allowTrackViaBaseType
    )
    {
        if (typeSymbol.HasAttribute(requiredAttribute))
            return;

        if (allowTrackViaBaseType && typeSymbol.GetBaseTypes().Any(x => x.HasAttribute(requiredAttribute)))
            return;

        foreach (var @interface in typeSymbol.Interfaces)
        {
            if (!@interface.HasAttribute(requiredAttribute))
                continue;

            context.ReportDiagnostic(
                Diagnostic.Create(
                    descriptor: MED005,
                    location: typeSymbol.Locations.FirstOrDefault() ?? Location.None,
                    messageArgs:
                    [
                        typeSymbol.ToDisplayString(MinimallyQualifiedFormat),
                        @interface.ToDisplayString(MinimallyQualifiedFormat),
                        attributeShortName,
                    ]
                )
            );
            return;
        }
    }

    static bool TryGetFirstNonPartialClassDeclaration(
        INamedTypeSymbol typeSymbol,
        CancellationToken ct,
        out ClassDeclarationSyntax declaration
    )
    {
        foreach (var syntaxRef in typeSymbol.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax(ct) is not ClassDeclarationSyntax classDeclaration)
                continue;

            if (classDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                continue;

            declaration = classDeclaration;
            return true;
        }

        declaration = null!;
        return false;
    }

    static bool TryGetInjectDeclarationLocation(
        IMethodSymbol methodSymbol,
        CancellationToken ct,
        out Location location
    )
    {
        foreach (var syntaxRef in methodSymbol.DeclaringSyntaxReferences)
        {
            switch (syntaxRef.GetSyntax(ct))
            {
                case MethodDeclarationSyntax methodDeclaration:
                    location = methodDeclaration.GetLocation();
                    return true;
                case LocalFunctionStatementSyntax localFunction:
                    location = localFunction.GetLocation();
                    return true;
            }
        }

        location = methodSymbol.Locations.FirstOrDefault() ?? Location.None;
        return location != Location.None;
    }

    static Location GetTypeDeclarationHeaderLocation(TypeDeclarationSyntax typeDeclaration)
    {
        var start = typeDeclaration.SpanStart;
        var end = typeDeclaration.OpenBraceToken.SpanStart;
        if (end <= start)
            return typeDeclaration.GetLocation();

        return Location.Create(typeDeclaration.SyntaxTree, TextSpan.FromBounds(start, end));
    }

    static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        KnownSymbols knownSymbols,
        DiagnosticDescriptor diagnosticDescriptor,
        string methodName,
        INamedTypeSymbol attributeSymbol,
        string attributeDisplayName
    )
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                Name: GenericNameSyntax
                {
                    Text: { } valueText,
                    TypeArgumentList.Arguments: [{ } typeArgumentSyntax],
                },
            })
            return;

        if (valueText != methodName)
            return;

        if (!TryGetMethodSymbol(context.SemanticModel, invocation, context.CancellationToken, out var methodSymbol))
            return;

        if (methodSymbol.OriginalDefinition is not { TypeParameters.Length: 1, ContainingType: { } containingType })
            return;

        if (!containingType.Is(knownSymbols.MedicineFind))
            return;

        if (context.SemanticModel.GetTypeInfo(typeArgumentSyntax).Type is not INamedTypeSymbol { IsUnboundGenericType: false } typeArgument)
            return;

        if (!typeArgument.HasAttribute(attributeSymbol))
        {
            if (typeArgument.ContainingNamespace?.ToDisplayString().StartsWith("UnityEngine", StringComparison.Ordinal) ?? false)
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        descriptor: MED003,
                        location: typeArgumentSyntax.GetLocation(),
                        messageArgs:
                        [
                            typeArgument.ToDisplayString(MinimallyQualifiedFormat),
                            methodSymbol.ToDisplayString(CSharpShortErrorMessageFormat),
                            attributeDisplayName,
                        ]
                    )
                );
            }
            else
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        descriptor: diagnosticDescriptor,
                        location: typeArgumentSyntax.GetLocation(),
                        messageArgs:
                        [
                            typeArgument.ToDisplayString(MinimallyQualifiedFormat),
                            typeArgument.TypeKind is TypeKind.Interface ? "Interface" : "Class",
                        ]
                    )
                );
            }
        }
    }

    static bool TryGetMethodSymbol(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        CancellationToken ct,
        out IMethodSymbol methodSymbol
    )
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, ct);
        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            methodSymbol = method;
            return true;
        }

        foreach (var candidate in symbolInfo.CandidateSymbols)
        {
            if (candidate is not IMethodSymbol candidateMethod)
                continue;

            methodSymbol = candidateMethod;
            return true;
        }

        methodSymbol = null!;
        return false;
    }
}
