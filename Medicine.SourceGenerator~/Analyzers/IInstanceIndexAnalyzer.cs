using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using static Constants;
using static Microsoft.CodeAnalysis.SymbolDisplayFormat;

#pragma warning disable RS2008

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InstanceIndexAnalyzer : DiagnosticAnalyzer
{
    static readonly DiagnosticDescriptor MED017 = new(
        id: nameof(MED017),
        title: "IInstanceIndex requires [Track]",
        messageFormat: "Class '{0}' implements the IInstanceIndex interface but is missing [Track].",
        category: "Medicine",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The IInstanceIndex interface does not do anything unless the class is also decorated with [Track]."
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }
        = ImmutableArray.Create(MED017);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        if (context.Symbol is not INamedTypeSymbol { TypeKind: TypeKind.Class } type)
            return;

        if (!type.HasInterface(
                x => x is { Name: IInstanceIndexInterfaceName, IsInMedicineNamespace: true },
                checkAllInterfaces: false
            )
        )
            return;

        if (type.HasAttribute(x => x is { Name: TrackAttributeName, IsInMedicineNamespace: true }))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(
                descriptor: MED017,
                location: type.Locations.First(),
                messageArgs: type.ToDisplayString(MinimallyQualifiedFormat)
            )
        );
    }
}
