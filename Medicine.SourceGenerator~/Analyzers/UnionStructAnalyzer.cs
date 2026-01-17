using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using static Constants;

#pragma warning disable RS2008

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnionStructAnalyzer : DiagnosticAnalyzer
{
    static readonly DiagnosticDescriptor MED019 = new(
        id: nameof(MED019),
        title: "UnionHeader struct must have a nested public interface",
        messageFormat: "Struct '{0}' tagged with [UnionHeader] must have a nested public interface",
        category: "Medicine",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "UnionHeader struct must have a nested public interface to define polymorphic members."
    );

    static readonly DiagnosticDescriptor MED020 = new(
        id: nameof(MED020),
        title: "UnionHeader struct must contain a TypeID field",
        messageFormat: "Struct '{0}' tagged with [UnionHeader] must contain a field: 'public TypeIDs TypeID;'",
        category: "Medicine",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "UnionHeader struct must contain a TypeID field of type TypeIDs to support polymorphism."
    );

    static readonly DiagnosticDescriptor MED021 = new(
        id: nameof(MED021),
        title: "Union struct must implement the [UnionHeader] interface",
        messageFormat: "Struct '{0}' tagged with [Union] must implement the interface nested in a [UnionHeader] struct",
        category: "Medicine",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Union struct must implement the interface defined in the UnionHeader struct."
    );

    static readonly DiagnosticDescriptor MED022 = new(
        id: nameof(MED022),
        title: "Union struct must have [UnionHeader] as the first field",
        messageFormat: "Struct '{0}' tagged with [Union] must have the related [UnionHeader] struct as its first field",
        category: "Medicine",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Union struct must have the [UnionHeader] struct as its first field to ensure proper memory layout."
    );

    static readonly DiagnosticDescriptor MED023 = new(
        id: nameof(MED023),
        title: "[UnionHeader] struct must be the first field",
        messageFormat: "Struct '{0}' tagged with [Union] contains a [UnionHeader] struct, but it's not the first field",
        category: "Medicine",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Union struct must have the [UnionHeader] struct as its first field to ensure proper memory layout."
    );

    static readonly DiagnosticDescriptor MED024 = new(
        id: nameof(MED024),
        title: "Struct implementing UnionHeader interface must be tagged with [Union]",
        messageFormat: "Struct '{0}' implementing the interface '{1}' needs to be tagged with [Union]",
        category: "Medicine",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Structs implementing a UnionHeader's nested interface must be tagged with [Union] to be recognized by the generator."
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }
        = ImmutableArray.Create(MED019, MED020, MED021, MED022, MED023, MED024);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        if (context.Symbol is not INamedTypeSymbol { TypeKind: TypeKind.Struct } type)
            return;

        var headerAttr = type.GetAttribute(UnionHeaderStructAttributeFQN);
        var unionAttr = type.GetAttribute(UnionStructAttributeFQN);

        if (headerAttr is not null)
            if (AnalyzeHeader() is { } diagnostic)
                context.ReportDiagnostic(diagnostic);

        if (unionAttr is not null)
            if (AnalyzeUnion() is { } diagnostic)
                context.ReportDiagnostic(diagnostic);

        if (unionAttr is null)
            if (AnalyzeMissingUnionAttribute() is { } diagnostic)
                context.ReportDiagnostic(diagnostic);

        return;

        Diagnostic? AnalyzeMissingUnionAttribute()
        {
            foreach (var candidateInterface in type.AllInterfaces)
            {
                var containingType = candidateInterface.ContainingType;

                if (containingType is not { TypeKind: TypeKind.Struct })
                    continue;

                if (!containingType.HasAttribute(UnionHeaderStructAttributeFQN))
                    continue;

                var isUnionInterface = containingType
                    .GetTypeMembers()
                    .Any(x => x is { TypeKind: TypeKind.Interface, DeclaredAccessibility: Accessibility.Public } && x.Is(candidateInterface));

                if (!isUnionInterface)
                    continue;

                return Diagnostic.Create(MED024, type.Locations[0], type.Name, candidateInterface.Name);
            }

            return null;
        }

        Diagnostic? AnalyzeHeader()
        {
            var hasPublicInterface = type
                .GetTypeMembers()
                .Any(x => x is
                    {
                        TypeKind: TypeKind.Interface,
                        DeclaredAccessibility: Accessibility.Public,
                    }
                );

            if (!hasPublicInterface)
                return Diagnostic.Create(MED019, type.Locations[0], type.Name);

            var typeIdField = type
                .GetMembers()
                .FirstOrDefault(x => x is IFieldSymbol
                    {
                        Name: "TypeID",
                        Type.Name: "TypeIDs",
                        DeclaredAccessibility: Accessibility.Public,
                    }
                );

            if (typeIdField is null)
                return Diagnostic.Create(MED020, type.Locations[0], type.Name);

            return null;
        }

        Diagnostic? AnalyzeUnion()
        {
            var fields = type.GetMembers()
                .OfType<IFieldSymbol>()
                .Where(x => !x.IsStatic)
                .OrderBy(x => x.Locations.FirstOrDefault()?.SourceSpan.Start ?? int.MaxValue)
                .ToArray();

            if (fields.Length == 0)
                return Diagnostic.Create(MED022, type.Locations[0], type.Name);

            var headerFieldIndex = Array.FindIndex(fields, f => f.Type.HasAttribute(UnionHeaderStructAttributeFQN));

            if (headerFieldIndex is -1)
                return Diagnostic.Create(MED022, type.Locations[0], type.Name);

            if (headerFieldIndex is not 0)
                return Diagnostic.Create(MED023, fields[headerFieldIndex].Locations[0], type.Name);

            var firstFieldType = fields[0].Type;

            var fieldMembers = firstFieldType.GetTypeMembers();

            var targetInterface
                = fieldMembers.FirstOrDefault(x => x.Name is "Interface") ??
                  fieldMembers.FirstOrDefault(x => x.TypeKind is TypeKind.Interface);

            if (targetInterface is not null)
                if (!type.AllInterfaces.Any(i => i.Is(targetInterface)))
                    return Diagnostic.Create(MED021, type.Locations[0], type.Name);

            return null;
        }
    }
}