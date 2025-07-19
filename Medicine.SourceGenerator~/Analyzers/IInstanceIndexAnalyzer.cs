using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using static Constants;
using static Microsoft.CodeAnalysis.SymbolDisplayFormat;

#pragma warning disable RS2008

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InstanceIndexAnalyzer : DiagnosticAnalyzer
{
    static readonly DiagnosticDescriptor MED006 = new(
        id: "MED017",
        title: "IInstanceIndex requires [Track]",
        messageFormat: "Class '{0}' implements the IInstanceIndex interface but is missing [Track].",
        category: "Medicine",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The IInstanceIndex interface does not do anything unless the class is also decorated with [Track]."
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }
        = ImmutableArray.Create(MED006);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (type.TypeKind != TypeKind.Class)
            return;

        if (type.HasInterface(IInstanceIndexInterfaceFQN) && !type.HasAttribute(TrackAttributeFQN))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    MED006,
                    type.Locations.First(),
                    type.ToDisplayString(MinimallyQualifiedFormat)
                )
            );
        }
    }
}