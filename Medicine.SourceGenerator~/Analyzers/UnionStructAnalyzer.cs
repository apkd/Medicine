using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using static System.StringComparison;
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

    static readonly DiagnosticDescriptor MED025 = new(
        id: nameof(MED025),
        title: "Duplicate [Union] ID",
        messageFormat: "Struct '{0}' has a duplicate [Union] ID {1} for the UnionHeader interface '{2}' (already used by '{3}')",
        category: "Medicine",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Multiple structs associated with the same UnionHeader cannot share the same ID."
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }
        = ImmutableArray.Create(MED019, MED020, MED021, MED022, MED023, MED024, MED025);

    readonly record struct UnionEntry(INamedTypeSymbol Symbol, INamedTypeSymbol HeaderInterface, byte Id, Location Location);

    readonly record struct UnionIdKey(INamedTypeSymbol HeaderInterface, byte Id);

    sealed class UnionIdKeyComparer : IEqualityComparer<UnionIdKey>
    {
        public bool Equals(UnionIdKey x, UnionIdKey y)
            => x.Id == y.Id && x.HeaderInterface.Is(y.HeaderInterface);

        public int GetHashCode(UnionIdKey obj)
            => (obj.HeaderInterface.Hash, obj.Id).GetHashCode();
    }

    readonly record struct ReportedKey(INamedTypeSymbol HeaderInterface, byte Id, INamedTypeSymbol UnionSymbol);

    sealed class ReportedKeyComparer : IEqualityComparer<ReportedKey>
    {
        public bool Equals(ReportedKey x, ReportedKey y)
            => x.Id == y.Id && x.HeaderInterface.Is(y.HeaderInterface) && x.UnionSymbol.Is(y.UnionSymbol);

        public int GetHashCode(ReportedKey obj)
            => (obj.HeaderInterface.Hash, obj.Id, obj.UnionSymbol.Hash).GetHashCode();
    }

    sealed class DuplicateUnionIdTracker
    {
        readonly ConcurrentDictionary<UnionIdKey, UnionEntry> earliestByKey = new(new UnionIdKeyComparer());
        readonly ConcurrentDictionary<ReportedKey, byte> reported = new(new ReportedKeyComparer());

        public void Observe(SymbolAnalysisContext context, UnionEntry entry)
        {
            if (entry.Id == 0)
                return;

            if (entry.Location == Location.None)
                return;

            var key = new UnionIdKey(entry.HeaderInterface, entry.Id);

            while (!context.CancellationToken.IsCancellationRequested)
            {
                if (earliestByKey.TryGetValue(key, out var existing))
                {
                    var cmp = CompareEntries(entry, existing);
                    var earlier = cmp < 0 ? entry : existing;
                    var later = cmp < 0 ? existing : entry;

                    if (cmp < 0)
                        if (!earliestByKey.TryUpdate(key, earlier, existing))
                            continue;

                    var rkey = new ReportedKey(key.HeaderInterface, key.Id, later.Symbol);

                    if (!reported.TryAdd(rkey, 0))
                        return;

                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            descriptor: MED025,
                            location: later.Location,
                            messageArgs:
                            [
                                later.Symbol.Name,
                                later.Id,
                                later.HeaderInterface.Name,
                                earlier.Symbol.Name,
                            ]
                        )
                    );

                    return;
                }

                if (earliestByKey.TryAdd(key, entry))
                    return;
            }
        }
    }

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(static startContext =>
        {
            var tracker = new DuplicateUnionIdTracker();
            startContext.RegisterSymbolAction(c => AnalyzeNamedType(c, tracker), SymbolKind.NamedType);
        });
    }

    static void AnalyzeNamedType(SymbolAnalysisContext context, DuplicateUnionIdTracker tracker)
    {
        if (context.Symbol is not INamedTypeSymbol { TypeKind: TypeKind.Struct } unionType)
            return;

        var headerAttr = unionType.GetAttribute(UnionHeaderStructAttributeFQN);
        var unionAttr = unionType.GetAttribute(UnionStructAttributeFQN);

        if (headerAttr is not null)
            if (AnalyzeHeader() is { } diagnostic)
                context.ReportDiagnostic(diagnostic);

        if (unionAttr is not null)
        {
            if (AnalyzeUnion() is { } diagnostic)
                context.ReportDiagnostic(diagnostic);

            RecordUnionForDuplicateChecks();
        }

        if (unionAttr is null)
        {
            if (AnalyzeMissingUnionAttribute() is { } diagnostic)
                context.ReportDiagnostic(diagnostic);
        }

        return;

        void RecordUnionForDuplicateChecks()
        {
            if (!TryGetUnionId(unionAttr, context.CancellationToken, out var id))
                return;

            if (id == 0)
                return;

            var loc = GetBestLocation(unionType, unionAttr, context.CancellationToken);

            foreach (var candidateInterface in unionType.AllInterfaces)
            {
                if (candidateInterface.ContainingType is not { TypeKind: TypeKind.Struct } containingType)
                    continue;

                if (!containingType.HasAttribute(UnionHeaderStructAttributeFQN))
                    continue;

                var isUnionInterface = containingType
                    .GetTypeMembers()
                    .Any(x => x is { TypeKind: TypeKind.Interface, DeclaredAccessibility: Accessibility.Public } && x.Is(candidateInterface));

                if (!isUnionInterface)
                    continue;

                tracker.Observe(context, new(unionType, candidateInterface, id, loc));
            }
        }

        Diagnostic? AnalyzeMissingUnionAttribute()
        {
            foreach (var candidateInterface in unionType.AllInterfaces)
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

                return Diagnostic.Create(MED024, unionType.Locations[0], unionType.Name, candidateInterface.Name);
            }

            return null;
        }

        Diagnostic? AnalyzeHeader()
        {
            var hasPublicInterface = unionType
                .GetTypeMembers()
                .Any(x => x is
                    {
                        TypeKind: TypeKind.Interface,
                        DeclaredAccessibility: Accessibility.Public,
                    }
                );

            if (!hasPublicInterface)
                return Diagnostic.Create(MED019, unionType.Locations[0], unionType.Name);

            var typeIdField = unionType
                .GetMembers()
                .FirstOrDefault(x => x is IFieldSymbol
                    {
                        Name: "TypeID",
                        Type.Name: "TypeIDs",
                        DeclaredAccessibility: Accessibility.Public,
                    }
                );

            if (typeIdField is null)
                return Diagnostic.Create(MED020, unionType.Locations[0], unionType.Name);

            return null;
        }

        Diagnostic? AnalyzeUnion()
        {
            var fields = unionType.GetMembers()
                .OfType<IFieldSymbol>()
                .Where(x => !x.IsStatic)
                .OrderBy(x => x.Locations.FirstOrDefault()?.SourceSpan.Start ?? int.MaxValue)
                .ToArray();

            if (fields.Length == 0)
                return Diagnostic.Create(MED022, unionType.Locations[0], unionType.Name);

            var headerFieldIndex = Array.FindIndex(fields, f => f.Type.HasAttribute(UnionHeaderStructAttributeFQN));

            if (headerFieldIndex is -1)
                return Diagnostic.Create(MED022, unionType.Locations[0], unionType.Name);

            if (headerFieldIndex is not 0)
                return Diagnostic.Create(MED023, fields[headerFieldIndex].Locations[0], unionType.Name);

            var firstFieldType = fields[0].Type;
            var fieldMembers = firstFieldType.GetTypeMembers();

            var targetInterface
                = fieldMembers.FirstOrDefault(x => x.Name is "Interface") ??
                  fieldMembers.FirstOrDefault(x => x.TypeKind is TypeKind.Interface);

            if (targetInterface is not null)
                if (!unionType.AllInterfaces.Any(i => i.Is(targetInterface)))
                    return Diagnostic.Create(MED021, unionType.Locations[0], unionType.Name);

            return null;
        }
    }

    static int CompareEntries(in UnionEntry a, in UnionEntry b)
    {
        var aPath = a.Location.SourceTree?.FilePath ?? "";
        var bPath = b.Location.SourceTree?.FilePath ?? "";
        var c = StringComparer.OrdinalIgnoreCase.Compare(aPath, bPath);
        if (c != 0)
            return c;

        c = a.Location.SourceSpan.Start.CompareTo(b.Location.SourceSpan.Start);
        if (c != 0)
            return c;

        return StringComparer.Ordinal.Compare(
            a.Symbol.FQN,
            b.Symbol.FQN
        );
    }

    static Location GetBestLocation(INamedTypeSymbol unionType, AttributeData unionAttr, CancellationToken ct)
    {
        if (unionAttr.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation() is { } attrLoc)
            return attrLoc;

        foreach (var l in unionType.Locations)
            if (l.IsInSource)
                return l;

        return unionType.Locations.FirstOrDefault() ?? Location.None;
    }

    static bool TryGetUnionId(AttributeData unionAttr, CancellationToken ct, out byte id)
    {
        foreach (var kv in unionAttr.NamedArguments)
            if (kv.Key.Equals("id", OrdinalIgnoreCase))
                if (TryCoerceToByte(kv.Value, out id))
                    return true;

        var ctor = unionAttr.AttributeConstructor;
        var args = unionAttr.ConstructorArguments;

        if (ctor is not null)
        {
            var parameters = ctor.Parameters;
            for (var i = 0; i < parameters.Length && i < args.Length; i++)
                if (parameters[i].Name.Equals("id", OrdinalIgnoreCase))
                    if (TryCoerceToByte(args[i], out id))
                        return true;
        }

        if (args.Length > 0)
            if (TryCoerceToByte(args[0], out id))
                return true;

        id = 0;
        return false;
    }

    static bool TryCoerceToByte(TypedConstant constant, out byte value)
    {
        switch (constant.Value)
        {
            case byte b:
                value = b;
                return true;
            case sbyte sb and >= 0:
                value = (byte)sb;
                return true;
            case short s and >= 0 and <= byte.MaxValue:
                value = (byte)s;
                return true;
            case ushort us and <= byte.MaxValue:
                value = (byte)us;
                return true;
            case int i and >= 0 and <= byte.MaxValue:
                value = (byte)i;
                return true;
            case uint ui and <= byte.MaxValue:
                value = (byte)ui;
                return true;
            case long l and >= 0 and <= byte.MaxValue:
                value = (byte)l;
                return true;
            case ulong ul and <= byte.MaxValue:
                value = (byte)ul;
                return true;
            default:
                value = 0;
                return false;
        }
    }
}