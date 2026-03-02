using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using static Constants;
using static TrackSourceGenerator.TypeFlags;

[Flags]
public enum SingletonStrategy : uint
{
    // ReSharper disable UnusedMember.Global
    Replace = 0,
    KeepExisting = 1 << 0,
    ThrowException = 1 << 1,
    LogWarning = 1 << 2,
    LogError = 1 << 3,
    Destroy = 1 << 4,
    AutoInstantiate = 1 << 5,
    // ReSharper restore UnusedMember.Global
}

[Generator]
public sealed class TrackSourceGenerator : IIncrementalGenerator
{
    static readonly SourceText staticInitClass
        = SourceText.From(
            $$"""
              namespace Medicine.Internal
              {
                  {{Alias.Hidden}}
                  static partial class {{m}}StaticInit            
                  {
              #if UNITY_EDITOR
                      {{Alias.EditorInit}}
              #endif
                      {{Alias.RuntimeInit}}
                      static void {{m}}Init() { }
                  }
              }
              """,
            Encoding.UTF8
        );

    static readonly DiagnosticDescriptor MED029 = new(
        id: nameof(MED029),
        title: "Mixed manual/automatic registration in inheritance",
        messageFormat:
        "Type '{0}' mixes manual and automatic [{1}] registration in its inheritance chain. Use the same 'manual' setting on all [{1}] types.",
        category: "Medicine",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    static readonly DiagnosticDescriptor MED037 = new(
        id: nameof(MED037),
        title: "Conflicting custom storage property names",
        messageFormat:
        "Type '{0}' generates duplicate custom storage property name '{1}' from storage types '{2}' and '{3}'. Rename one of the storage types.",
        category: "Medicine",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    record struct TrackAttributeSettings(
        SingletonStrategy? SingletonStrategy,
        bool TrackTransforms,
        int InitialCapacity,
        int DesiredJobCount,
        bool CacheEnabledState,
        bool Manual
    );

    readonly record struct TrackingIdRegistration(
        string LookupTypeFQN,
        string IDTypeFQN
    );

    readonly record struct TrackInterfaceRegistration(
        string TypeFQN,
        bool UseTransformFallback,
        EquatableArray<string> UnmanagedDataFQNs,
        EquatableArray<string> CustomStorageTypeFQNs
    );

    readonly record struct CustomStoragePropertyInfo(
        string StorageTypeFQN,
        string StorageTypeShort,
        string PropertyName
    );

    record struct PrecomputedAttributeSettingsData : ISourceGeneratorPassData
    {
        public string? SourceGeneratorOutputFilename { get; init; }
        public string? SourceGeneratorError { get; init; }
        public LocationInfo? SourceGeneratorLocation { get; set; }

        public GeneratorEnvironment Environment;
        public EquatableIgnore<Dictionary<INamedTypeSymbol, TrackAttributeSettings>> AttributeSettingsByType;

        bool IEquatable<PrecomputedAttributeSettingsData>.Equals(PrecomputedAttributeSettingsData other) => false;
        public readonly override int GetHashCode() => 0;
    }

    record struct SingletonCacheInput : ISourceGeneratorPassData
    {
        public string? SourceGeneratorOutputFilename { get; init; }
        public string? SourceGeneratorError { get; init; }
        public LocationInfo? SourceGeneratorLocation { get; set; }

        public GeneratorEnvironment Environment;
        public EquatableIgnore<GeneratorAttributeSyntaxContext> Context;
        public EquatableIgnore<Dictionary<INamedTypeSymbol, TrackAttributeSettings>> AttributeSettingsByType;
        public ulong Checksum64ForCache;
    }

    record struct GeneratorInput : ISourceGeneratorPassData
    {
        public string? SourceGeneratorOutputFilename { get; init; }
        public string? SourceGeneratorError { get; init; }
        public LocationInfo? SourceGeneratorLocation { get; set; }

        public GeneratorEnvironment Environment;
        public bool HasIInstanceIndex;
        public bool EmitIInstanceIndex;
        public bool HasBaseDeclarationsWithAttribute;
        public bool ManualInheritanceMismatch;
        public string? Attribute;
        public TrackAttributeSettings AttributeSettings;
        public EquatableArray<string> ContainingTypeDeclaration;
        public string? ContainingTypeFQN;
        public EquatableArray<string> InterfacesWithAttribute;
        public Defer<TrackInterfaceRegistration[]>? TrackedInterfacesDeferred;
        public EquatableArray<string> UnmanagedDataFQNs;
        public EquatableArray<string> CustomStorageTypeFQNs;
        public EquatableArray<TrackingIdRegistration> TrackingIdRegistrations;
        public string? TypeFQN;
        public string? TypeDisplayName;
        public TypeFlags Flags;
    }

    record struct InterfaceGeneratorInput : ISourceGeneratorPassData
    {
        public string? SourceGeneratorOutputFilename { get; init; }
        public string? SourceGeneratorError { get; init; }
        public LocationInfo? SourceGeneratorLocation { get; set; }

        public GeneratorEnvironment Environment;
        public TrackAttributeSettings AttributeSettings;
        public EquatableArray<string> ContainingTypeDeclaration;
        public EquatableArray<string> UnmanagedDataFQNs;
        public EquatableArray<string> CustomStorageTypeFQNs;
        public EquatableArray<string> TrackingIdTypeFQNs;
        public string? TypeFQN;
        public string? ContainingTypeFQN;
        public Defer<string>? TypeDisplayNameBuilderDeferred;
        public bool IsGenericType;
        public TypeFlags Flags;
    }

    readonly record struct TrackApiSurfaceInput(
        string TypeFQN,
        string TypeDisplayName,
        TrackAttributeSettings AttributeSettings,
        string NewModifier,
        EquatableArray<string> UnmanagedDataFQNs,
        EquatableArray<CustomStoragePropertyInfo> CustomStorageProperties,
        EquatableArray<string> FindByIdTypeFQNs,
        bool EmitDocs
    );

    readonly record struct StaticInitInput(
        string TypeFQN,
        string? ContainingTypeFQN,
        TypeFlags Flags,
        bool IsAutoInstantiate,
        bool EmitTransformAccessInit,
        int TransformInitialCapacity,
        int TransformDesiredJobCount
    );

    internal enum TypeFlags : uint
    {
        IsAccessible = 1 << 0,
        ContainingTypeIsAccessible = 1 << 1,
        IsSealed = 1 << 2,
        IsGenericType = 1 << 4,
        IsUnityEngineObject = 1 << 5,
        IsComponent = 1 << 6,
        IsMonoBehaviour = 1 << 7,
        IsScriptableObject = 1 << 8,
        IsValueType = 1 << 9,
        IsInterface = 1 << 10,
        IsAbstract = 1 << 11,
    }

    void IIncrementalGenerator.Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(x => x.AddSource("Medicine.Internal.StaticInit.g.cs", staticInitClass));

        var generatorEnvironment = context.GetGeneratorEnvironment();

        var singletonAttributeCache = context.SyntaxProvider
            .ForAttributeWithMetadataNameEx(
                fullyQualifiedMetadataName: SingletonAttributeMetadataName,
                predicate: static (node, _) => true,
                transform: static (context, _) => new GeneratorAttributeContextInput(context, x => GetOutputFilename(x, $"[{SingletonAttributeNameShort}]"))
            )
            .Collect()
            .Combine(generatorEnvironment)
            .SelectEx(TransformSyntaxContextPrecompute);

        var trackAttributeCache = context.SyntaxProvider
            .ForAttributeWithMetadataNameEx(
                fullyQualifiedMetadataName: TrackAttributeMetadataName,
                predicate: static (node, _) => true,
                transform: static (context, _) => new GeneratorAttributeContextInput(context, x => GetOutputFilename(x, $"[{TrackAttributeNameShort}]"))
            )
            .Collect()
            .Combine(generatorEnvironment)
            .SelectEx(TransformSyntaxContextPrecompute);

        context.RegisterSourceOutputEx(
            context.SyntaxProvider
                .ForAttributeWithMetadataNameEx(
                    fullyQualifiedMetadataName: SingletonAttributeMetadataName,
                    predicate: static (node, _) => node is ClassDeclarationSyntax,
                    transform: static (context, _) => new GeneratorAttributeContextInput(context, x => GetOutputFilename(x, $"[{SingletonAttributeNameShort}]"))
                )
                .Combine(singletonAttributeCache)
                .SelectEx(TransformSingletonForCache)
                .SelectEx((x, ct) => TransformSyntaxContext(
                        context: x.Context.Value,
                        attributeSettingsByType: x.AttributeSettingsByType.Value,
                        environment: x.Environment,
                        ct: ct,
                        attributeName: SingletonAttributeMetadataName
                    )
                ),
            GenerateSource
        );

        context.RegisterSourceOutputEx(
            context.SyntaxProvider
                .ForAttributeWithMetadataNameEx(
                    fullyQualifiedMetadataName: TrackAttributeMetadataName,
                    predicate: static (node, _) => node is ClassDeclarationSyntax,
                    transform: static (context, _) => new GeneratorAttributeContextInput(context, x => GetOutputFilename(x, $"[{TrackAttributeNameShort}]"))
                )
                .Combine(trackAttributeCache)
                .SelectEx(TransformTrackForCache)
                .SelectEx((x, ct) => TransformSyntaxContext(
                        context: x.Context.Value,
                        attributeSettingsByType: x.AttributeSettingsByType.Value,
                        environment: x.Environment,
                        ct: ct,
                        attributeName: TrackAttributeMetadataName
                    )
                ),
            GenerateSource
        );

        context.RegisterSourceOutputEx(
            context.SyntaxProvider
                .ForAttributeWithMetadataNameEx(
                    fullyQualifiedMetadataName: TrackAttributeMetadataName,
                    predicate: static (node, _) => node is InterfaceDeclarationSyntax,
                    transform: static (context, _) => new GeneratorAttributeContextInput(context, x => GetOutputFilename(x, $"[{TrackAttributeNameShort}]"))
                )
                .Combine(trackAttributeCache)
                .SelectEx(TransformTrackForCache)
                .SelectEx((x, ct) =>
                    {
                        var input = TransformInterfaceSyntaxContext(
                            context: x.Context.Value,
                            attributeSettingsByType: x.AttributeSettingsByType.Value,
                            ct: ct
                        );

                        if (input.TypeFQN is not { Length: > 0 })
                            return input;

                        return input with
                        {
                            Environment = x.Environment,
                        };
                    }
                )
                .Where(x => x is { TypeFQN.Length: > 0, IsGenericType: false }),
            GenerateTrackedInterfaceHelper
        );
    }

    static string? GetOutputFilename(GeneratorAttributeSyntaxContext context, string label)
    {
        if (context.TargetNode is not TypeDeclarationSyntax
            {
                ShortName: { Length: > 0 } name,
                FullName: { Length: > 0 } fullName,
                SyntaxTree.FilePath: { Length: > 0 } filePath,
            })
            return null;

        return Utility.GetOutputFilename(
            filePath: filePath,
            targetNodeName: name,
            additionalNameForHash: fullName,
            label: label,
            includeFilename: false
        );
    }

    static SingletonCacheInput TransformSingletonForCache(
        (GeneratorAttributeContextInput Left, PrecomputedAttributeSettingsData Right) input,
        CancellationToken ct
    ) => new()
    {
        SourceGeneratorOutputFilename = input.Left.SourceGeneratorOutputFilename,
        SourceGeneratorLocation = input.Left.SourceGeneratorLocation,
        Environment = input.Right.Environment,
        Context = input.Left.Context,
        AttributeSettingsByType = input.Right.AttributeSettingsByType.Value,
        Checksum64ForCache = input.Left.Context.TargetNode switch
        {
            TypeDeclarationSyntax { AttributeLists.Count: > 0 } typeDeclaration
                => typeDeclaration.GetAttributeListChecksum(ct),
            _ => input.Left.Context.TargetNode.GetNodeChecksum(ct),
        },
    };

    static SingletonCacheInput TransformTrackForCache(
        (GeneratorAttributeContextInput Left, PrecomputedAttributeSettingsData Right) input,
        CancellationToken ct
    ) => new()
    {
        SourceGeneratorOutputFilename = input.Left.SourceGeneratorOutputFilename,
        SourceGeneratorLocation = input.Left.SourceGeneratorLocation,
        Environment = input.Right.Environment,
        Context = input.Left.Context,
        AttributeSettingsByType = input.Right.AttributeSettingsByType.Value,
        Checksum64ForCache = input.Left.Context.TargetSymbol.GetDeclarationHierarchyChecksum(ct),
    };

    PrecomputedAttributeSettingsData TransformSyntaxContextPrecompute(
        (ImmutableArray<GeneratorAttributeContextInput>, GeneratorEnvironment) input,
        CancellationToken ct
    )
    {
        var (passDataArray, environment) = input;
        var attributeSettingsByType = new Dictionary<INamedTypeSymbol, TrackAttributeSettings>(passDataArray.Length, SymbolEqualityComparer.Default);

        foreach (var passData in passDataArray)
        {
            var context = passData.Context;

            if (context.TargetSymbol is not INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Interface } symbol)
                continue;

            if (context.Attributes.FirstOrDefault() is not { AttributeConstructor: not null } attributeData)
                continue;

            attributeSettingsByType[symbol] = GetTrackAttributeSettings(attributeData, ct);
        }

        return new()
        {
            SourceGeneratorOutputFilename = $"{nameof(TransformSyntaxContextPrecompute)}.cs",
            Environment = environment,
            AttributeSettingsByType = attributeSettingsByType,
        };
    }

    static GeneratorInput TransformSyntaxContext(
        GeneratorAttributeSyntaxContext context,
        Dictionary<INamedTypeSymbol, TrackAttributeSettings>? attributeSettingsByType,
        GeneratorEnvironment environment,
        CancellationToken ct,
        string attributeName
    )
    {
        if (context.TargetNode is not TypeDeclarationSyntax typeDeclaration)
            return default;

        if (context.TargetSymbol is not INamedTypeSymbol { TypeKind: TypeKind.Class } classSymbol)
            return default;

        if (context.Attributes.FirstOrDefault() is not { AttributeConstructor: not null } attributeData)
            return default;

        var attributeSettingsLookup = attributeSettingsByType;
        if (attributeSettingsLookup is null)
            return default;

        if (!attributeSettingsLookup.TryGetValue(classSymbol, out var attributeArguments))
            return default;

        var knownSymbols = context.SemanticModel.Compilation.GetKnownSymbols();

        bool hasBaseDeclarationsWithAttribute = false;
        bool hasBaseCachedEnabledState = false;
        bool manualInheritanceMismatch = false;
        for (var baseType = classSymbol.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            var symbolForLookup = baseType is { IsGenericType: true, IsUnboundGenericType: false }
                ? baseType.OriginalDefinition
                : baseType;

            if (!attributeSettingsLookup.TryGetValue(symbolForLookup, out var baseAttributeSettings))
                continue;

            hasBaseDeclarationsWithAttribute = true;

            if (attributeName is TrackAttributeMetadataName)
                hasBaseCachedEnabledState |= baseAttributeSettings.CacheEnabledState;

            manualInheritanceMismatch = baseAttributeSettings.Manual != attributeArguments.Manual;

            if (manualInheritanceMismatch)
                break;
        }

        if (attributeName is TrackAttributeMetadataName)
            if (hasBaseCachedEnabledState && attributeArguments.CacheEnabledState)
                attributeArguments = attributeArguments with { CacheEnabledState = false };

        var sourceLocation = attributeData.ApplicationSyntaxReference.GetLocation()
                             ?? typeDeclaration.Identifier.GetLocation();

        bool hasIIndexInstance = classSymbol.HasInterface(knownSymbols.IInstanceIndexInterface);

        bool emitIIndexInstance = hasIIndexInstance &&
                                  !classSymbol.HasInterface(knownSymbols.IInstanceIndexInterface, checkAllInterfaces: false);

        string classTypeFQN = classSymbol.FQN;
        bool isMonoBehaviour = classSymbol.InheritsFrom(knownSymbols.UnityMonoBehaviour);

        using var r1 = Scratch.RentA<List<(INamedTypeSymbol Interface, TrackAttributeSettings AttributeSettings)>>(out var interfacesWithAttributeData);
        using var r2 = Scratch.RentB<List<string>>(out var interfacesWithAttribute);

        using (Scratch.RentA<HashSet<INamedTypeSymbol>>(out var inheritedInterfaces))
        {
            interfacesWithAttribute.Capacity = interfacesWithAttributeData.Count;
            interfacesWithAttributeData.EnsureCapacity(classSymbol.Interfaces.Length);

            for (var baseType = classSymbol.BaseType; baseType is not null; baseType = baseType.BaseType)
                foreach (var inheritedInterface in baseType.Interfaces)
                    inheritedInterfaces.Add(inheritedInterface);

            foreach (var interfaceSymbol in classSymbol.Interfaces)
            {
                if (inheritedInterfaces.Contains(interfaceSymbol))
                    continue;

                var symbolForLookup = interfaceSymbol is { IsGenericType: true, IsUnboundGenericType: false }
                    ? interfaceSymbol.OriginalDefinition
                    : interfaceSymbol;

                if (!attributeSettingsLookup.TryGetValue(symbolForLookup, out var interfaceAttributeSettings))
                    continue;

                interfacesWithAttributeData.Add((interfaceSymbol, interfaceAttributeSettings));
            }

            foreach (var (interfaceSymbol, _) in interfacesWithAttributeData)
                if (interfaceSymbol.FQN is { } interfaceFqn)
                    interfacesWithAttribute.Add(interfaceFqn);
        }

        TrackInterfaceRegistration[] trackedInterfaces = [];
        if (attributeName is TrackAttributeMetadataName)
        {
            using var r3 = Scratch.RentA<List<TrackInterfaceRegistration>>(out var trackedInterfaceList);
            trackedInterfaceList.EnsureCapacity(interfacesWithAttributeData.Count);

            foreach (var (interfaceSymbol, interfaceAttributeSettings) in interfacesWithAttributeData)
            {
                bool useTransformFallback = isMonoBehaviour && interfaceSymbol.IsGenericType && interfaceAttributeSettings.TrackTransforms;

                string[] unmanagedDataFQNs;
                string[] customStorageTypeFQNs;
                {
                    using (Scratch.RentA<List<string>>(out var values))
                    using (Scratch.RentC<List<string>>(out var customStorageValues))
                    using (Scratch.RentA<HashSet<string>>(out var seen))
                    using (Scratch.RentC<HashSet<string>>(out var seenCustomStorage))
                    {
                        TryAdd(interfaceSymbol);
                        foreach (var inheritedInterface in interfaceSymbol.AllInterfaces)
                            TryAdd(inheritedInterface);

                        unmanagedDataFQNs = values.ToArray();
                        customStorageTypeFQNs = customStorageValues.ToArray();

                        void TryAdd(INamedTypeSymbol symbol)
                        {
                            if (symbol.OriginalDefinition.Is(knownSymbols.UnmanagedDataInterface))
                                if (symbol.TypeArguments is [{ FQN: { Length: > 0 } fqn }])
                                    if (seen.Add(fqn))
                                        values.Add(fqn);

                            if (symbol.OriginalDefinition.Is(knownSymbols.CustomStorageInterface))
                                if (symbol.TypeArguments is [{ FQN: { Length: > 0 } customStorageFqn }])
                                    if (seenCustomStorage.Add(customStorageFqn))
                                        customStorageValues.Add(customStorageFqn);
                        }
                    }
                }

                trackedInterfaceList.Add(
                    new(
                        TypeFQN: interfaceSymbol.FQN,
                        UseTransformFallback: useTransformFallback,
                        UnmanagedDataFQNs: unmanagedDataFQNs,
                        CustomStorageTypeFQNs: customStorageTypeFQNs
                    )
                );
            }

            trackedInterfaces = trackedInterfaceList.ToArray();
        }

        TrackingIdRegistration[] trackingIdRegistrations;
        using (Scratch.RentA<List<TrackingIdRegistration>>(out var trackingIdList))
        {
            foreach (var interfaceType in classSymbol.AllInterfaces)
            {
                AddTrackingIdFromSymbol(interfaceType, interfaceType, interfaceType.FQN);

                foreach (var inheritedInterface in interfaceType.AllInterfaces)
                    AddTrackingIdFromSymbol(inheritedInterface, interfaceType, interfaceType.FQN);
            }

            trackingIdRegistrations = trackingIdList.ToArray();

            void AddTrackingIdFromSymbol(INamedTypeSymbol type, INamedTypeSymbol lookupType, string lookupTypeFQN)
            {
                if (!TryGetTrackingIdType(type, lookupType, out var idTypeFQN, out bool registerLookupType))
                    return;

                if (!trackingIdList.Contains(new(classTypeFQN, idTypeFQN)))
                    trackingIdList.Add(new(classTypeFQN, idTypeFQN));

                // omit direct IFindByID<T> implementations
                if (!registerLookupType)
                    return;

                if (!trackingIdList.Contains(new(lookupTypeFQN, idTypeFQN)))
                    trackingIdList.Add(new(lookupTypeFQN, idTypeFQN));
            }

            bool TryGetTrackingIdType(INamedTypeSymbol type, INamedTypeSymbol lookupType, out string idTypeFQN, out bool registerLookupType)
            {
                if (!type.OriginalDefinition.Is(knownSymbols.TrackingIdInterface))
                {
                    idTypeFQN = "";
                    registerLookupType = false;
                    return false;
                }

                idTypeFQN = type.TypeArguments.FirstOrDefault()?.FQN ?? "";
                if (idTypeFQN.Length is 0)
                {
                    registerLookupType = false;
                    return false;
                }

                registerLookupType = !type.Is(lookupType);
                return true;
            }
        }

        string[] classUnmanagedDataFQNs;
        string[] classCustomStorageTypeFQNs;
        using (Scratch.RentA<List<string>>(out var unmanagedDataFQNsList))
        using (Scratch.RentC<List<string>>(out var customStorageTypeFQNsList))
        using (Scratch.RentA<HashSet<string>>(out var seenUnmanagedData))
        using (Scratch.RentC<HashSet<string>>(out var seenCustomStorage))
        {
            foreach (var interfaceType in classSymbol.Interfaces)
            {
                TryAddUnmanagedData(interfaceType);
                TryAddInheritedUnmanagedData(interfaceType);

                TryAddCustomStorage(interfaceType);

                if (interfaceType.OriginalDefinition.Is(knownSymbols.TrackInstanceIDsInterface))
                    foreach (var inheritedInterface in interfaceType.AllInterfaces)
                        TryAddCustomStorage(inheritedInterface);
            }

            classUnmanagedDataFQNs = unmanagedDataFQNsList.ToArray();
            classCustomStorageTypeFQNs = customStorageTypeFQNsList.ToArray();

            void TryAddUnmanagedData(INamedTypeSymbol symbol)
            {
                if (symbol.OriginalDefinition.Is(knownSymbols.UnmanagedDataInterface))
                    if (symbol.TypeArguments is [{ FQN: { Length: > 0 } unmanagedDataFqn }])
                        if (seenUnmanagedData.Add(unmanagedDataFqn))
                            unmanagedDataFQNsList.Add(unmanagedDataFqn);
            }

            void TryAddInheritedUnmanagedData(INamedTypeSymbol symbol)
            {
                if (IsTrackedInterface(symbol))
                    return;

                foreach (var inheritedInterface in symbol.Interfaces)
                {
                    if (IsTrackedInterface(inheritedInterface))
                        continue;

                    TryAddUnmanagedData(inheritedInterface);
                    TryAddInheritedUnmanagedData(inheritedInterface);
                }
            }

            bool IsTrackedInterface(INamedTypeSymbol symbol)
            {
                var symbolForLookup = symbol is { IsGenericType: true, IsUnboundGenericType: false }
                    ? symbol.OriginalDefinition
                    : symbol;

                return symbolForLookup.HasAttribute(knownSymbols.TrackAttribute);
            }

            void TryAddCustomStorage(INamedTypeSymbol symbol)
            {
                if (symbol.OriginalDefinition.Is(knownSymbols.CustomStorageInterface))
                    if (symbol.TypeArguments is [{ FQN: { Length: > 0 } customStorageFqn }])
                        if (seenCustomStorage.Add(customStorageFqn))
                            customStorageTypeFQNsList.Add(customStorageFqn);
            }
        }

        return new()
        {
            SourceGeneratorOutputFilename = Utility.GetOutputFilename(
                filePath: typeDeclaration.SyntaxTree.FilePath,
                targetNodeName: classSymbol.Name,
                additionalNameForHash: classSymbol.FQN,
                label: attributeName switch
                {
                    TrackAttributeMetadataName     => $"[{TrackAttributeNameShort}]",
                    SingletonAttributeMetadataName => $"[{SingletonAttributeNameShort}]",
                    _                              => attributeName,
                },
                includeFilename: false
            ),
            Environment = environment,
            HasIInstanceIndex = hasIIndexInstance,
            EmitIInstanceIndex = emitIIndexInstance,
            SourceGeneratorLocation = sourceLocation,
            ContainingTypeDeclaration = Utility.DeconstructTypeDeclaration(typeDeclaration),
            ContainingTypeFQN = classSymbol.ContainingType?.FQN,
            Attribute = attributeName,
            AttributeSettings = attributeArguments,
            UnmanagedDataFQNs = classUnmanagedDataFQNs,
            CustomStorageTypeFQNs = classCustomStorageTypeFQNs,
            TrackingIdRegistrations = trackingIdRegistrations,
            TypeFQN = classSymbol.FQN,
            TypeDisplayName = classSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat).HtmlEncode(),
            HasBaseDeclarationsWithAttribute = hasBaseDeclarationsWithAttribute,
            ManualInheritanceMismatch = manualInheritanceMismatch,
            InterfacesWithAttribute = interfacesWithAttribute.ToArray(),
            TrackedInterfacesDeferred = new(() => trackedInterfaces),
            Flags = 0
                    | (context.SemanticModel.IsAccessible(position: 0, classSymbol) ? IsAccessible : 0)
                    | (classSymbol.ContainingSymbol is { } containing && context.SemanticModel.IsAccessible(position: 0, containing) ? ContainingTypeIsAccessible : 0)
                    | (classSymbol.IsGenericType ? IsGenericType : 0)
                    | (classSymbol.IsSealed ? IsSealed : 0)
                    | (classSymbol.IsAbstract ? IsAbstract : 0)
                    | (classSymbol.IsValueType ? IsValueType : 0)
                    | (classSymbol.InheritsFrom(knownSymbols.UnityObject) ? IsUnityEngineObject : 0)
                    | (classSymbol.InheritsFrom(knownSymbols.UnityComponent) ? IsComponent : 0)
                    | (classSymbol.InheritsFrom(knownSymbols.UnityMonoBehaviour) ? IsMonoBehaviour : 0)
                    | (classSymbol.InheritsFrom(knownSymbols.UnityScriptableObject) ? IsScriptableObject : 0),
        };
    }

    static InterfaceGeneratorInput TransformInterfaceSyntaxContext(
        GeneratorAttributeSyntaxContext context,
        Dictionary<INamedTypeSymbol, TrackAttributeSettings>? attributeSettingsByType,
        CancellationToken ct
    )
    {
        if (context.TargetNode is not TypeDeclarationSyntax typeDeclaration)
            return default;

        if (context.TargetSymbol is not INamedTypeSymbol { TypeKind: TypeKind.Interface } interfaceSymbol)
            return default;

        if (context.Attributes.FirstOrDefault() is not { AttributeConstructor: not null } attributeData)
            return default;

        var knownSymbols = context.SemanticModel.Compilation.GetKnownSymbols();
        var interfaceSymbolForLookup = interfaceSymbol is { IsGenericType: true, IsUnboundGenericType: false }
            ? interfaceSymbol.OriginalDefinition
            : interfaceSymbol;

        var interfaceAttributeSettings = attributeSettingsByType?.TryGetValue(interfaceSymbolForLookup, out var value) is true
            ? value
            : GetTrackAttributeSettings(attributeData, ct);

        var allDeclarations = Utility
            .DeconstructTypeDeclaration(typeDeclaration)
            .AsArray();

        string[] unmanagedDataFQNs;
        string[] customStorageTypeFQNs;
        string[] trackingIdTypeFQNs;
        using (Scratch.RentA<List<string>>(out var unmanagedDataFQNsList))
        using (Scratch.RentB<List<string>>(out var customStorageTypeFQNsList))
        using (Scratch.RentC<List<string>>(out var trackingIdTypeFQNsList))
        using (Scratch.RentA<HashSet<string>>(out var seenUnmanagedDataFQNs))
        using (Scratch.RentB<HashSet<string>>(out var seenCustomStorageTypeFQNs))
        using (Scratch.RentC<HashSet<string>>(out var seenTrackingIdTypeFQNs))
        {
            Add(interfaceSymbol);
            foreach (var inheritedInterface in interfaceSymbol.AllInterfaces)
                Add(inheritedInterface);

            unmanagedDataFQNs = unmanagedDataFQNsList.ToArray();
            customStorageTypeFQNs = customStorageTypeFQNsList.ToArray();
            trackingIdTypeFQNs = trackingIdTypeFQNsList.ToArray();

            void Add(INamedTypeSymbol symbol)
            {
                bool hasUnmanagedData = symbol.OriginalDefinition.Is(knownSymbols.UnmanagedDataInterface);
                bool hasCustomStorage = symbol.OriginalDefinition.Is(knownSymbols.CustomStorageInterface);
                bool hasTrackingId = symbol.OriginalDefinition.Is(knownSymbols.TrackingIdInterface);

                if (!hasUnmanagedData)
                    if (!hasTrackingId)
                        if (!hasCustomStorage)
                            return;

                if (symbol.TypeArguments is not [{ FQN: { Length: > 0 } fqn }])
                    return;

                if (hasUnmanagedData)
                    if (seenUnmanagedDataFQNs.Add(fqn))
                        unmanagedDataFQNsList.Add(fqn);

                if (hasTrackingId)
                    if (seenTrackingIdTypeFQNs.Add(fqn))
                        trackingIdTypeFQNsList.Add(fqn);

                if (hasCustomStorage)
                    if (seenCustomStorageTypeFQNs.Add(fqn))
                        customStorageTypeFQNsList.Add(fqn);
            }
        }

        return new()
        {
            SourceGeneratorOutputFilename = Utility.GetOutputFilename(
                filePath: typeDeclaration.SyntaxTree.FilePath,
                targetNodeName: interfaceSymbol.Name,
                additionalNameForHash: interfaceSymbol.FQN,
                label: $"[{TrackAttributeNameShort}]",
                includeFilename: false
            ),
            SourceGeneratorLocation = attributeData.ApplicationSyntaxReference.GetLocation() ?? typeDeclaration.Identifier.GetLocation(),
            AttributeSettings = interfaceAttributeSettings,
            ContainingTypeDeclaration = allDeclarations,
            ContainingTypeFQN = interfaceSymbol.ContainingType?.FQN,
            UnmanagedDataFQNs = unmanagedDataFQNs,
            CustomStorageTypeFQNs = customStorageTypeFQNs,
            TrackingIdTypeFQNs = trackingIdTypeFQNs,
            TypeFQN = interfaceSymbol.FQN,
            TypeDisplayNameBuilderDeferred = new(() => interfaceSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat).HtmlEncode()),
            IsGenericType = interfaceSymbol.IsGenericType,
            Flags = 0
                    | (context.SemanticModel.IsAccessible(position: 0, interfaceSymbol) ? IsAccessible : 0)
                    | (interfaceSymbol.ContainingSymbol is { } containing && context.SemanticModel.IsAccessible(position: 0, containing) ? ContainingTypeIsAccessible : 0)
                    | (interfaceSymbol.IsGenericType ? IsGenericType : 0)
                    | IsInterface,
        };
    }

    static void AppendInterface(SourceWriter src, ref bool hasInterfaces, string interfaceFqn)
    {
        src.Write(hasInterfaces ? ", " : " : ");
        src.Write(interfaceFqn);
        hasInterfaces = true;
    }

    static void AppendTrackedMarkerInterfaces(
        SourceWriter src,
        string typeFqn,
        TrackAttributeSettings attributeSettings,
        string[] unmanagedDataFQNs,
        ref bool hasInterfaces
    )
    {
        bool hasDerivedTrackedMarker = false;

        if (attributeSettings.TrackTransforms)
        {
            AppendInterface(src, ref hasInterfaces, $"{ITrackedTransformAccessArrayInternalInterfaceFQN}<{typeFqn}>");
            hasDerivedTrackedMarker = true;
        }

        foreach (var unmanagedDataTypeFqn in unmanagedDataFQNs)
        {
            AppendInterface(src, ref hasInterfaces, $"{ITrackedUnmanagedDataInternalInterfaceFQN}<{typeFqn}, {unmanagedDataTypeFqn}>");
            hasDerivedTrackedMarker = true;
        }

        if (!hasDerivedTrackedMarker)
            AppendInterface(src, ref hasInterfaces, $"{ITrackedInternalInterfaceFQN}<{typeFqn}>");
    }

    static TrackAttributeSettings GetTrackAttributeSettings(AttributeData attributeData, CancellationToken ct)
        => attributeData
            .GetAttributeConstructorArguments(ct)
            .Select(x => new TrackAttributeSettings(
                    SingletonStrategy: x.Get<SingletonStrategy>("strategy", null),
                    TrackTransforms: x.Get("transformAccessArray", false),
                    InitialCapacity: x.Get("transformInitialCapacity", 64),
                    DesiredJobCount: x.Get("transformDesiredJobCount", -1),
                    CacheEnabledState: x.Get("cacheEnabledState", false),
                    Manual: x.Get("manual", false)
                )
            );

    static void GenerateTrackedInterfaceHelper(SourceProductionContext context, SourceWriter src, InterfaceGeneratorInput input)
    {
        if (input is not { TypeFQN.Length: > 0, IsGenericType: false })
            return;

        var customStorageProperties = GetCustomStorageProperties(
            context: context,
            storageTypeFQNs: input.CustomStorageTypeFQNs,
            trackedTypeFQN: input.TypeFQN,
            sourceGeneratorErrorLocation: input.SourceGeneratorLocation
        );

        string typeDisplayName
            = input.TypeDisplayNameBuilderDeferred?.Value
              ?? input.TypeFQN;

        src.Line.Write(Alias.UsingStorage);
        src.Line.Write(Alias.UsingInline);
        src.Line.Write(Alias.UsingUtility);
        src.Linebreak();

        var declarations = input.ContainingTypeDeclaration.AsArray();
        int lastDeclaration = declarations.Length - 1;

        for (int i = 0; i < declarations.Length; i++)
        {
            src.Line.Write(declarations[i]);

            if (i == lastDeclaration)
            {
                bool hasInterfaces = false;
                AppendTrackedMarkerInterfaces(
                    src: src,
                    typeFqn: input.TypeFQN ?? "",
                    attributeSettings: input.AttributeSettings,
                    unmanagedDataFQNs: input.UnmanagedDataFQNs.AsArray(),
                    hasInterfaces: ref hasInterfaces
                );
            }

            src.OpenBrace();
        }

        src.Line.Write("public static class Track");

        using (src.Braces)
        {
            EmitTrackApiSurface(
                src: src,
                input: new(
                    TypeFQN: input.TypeFQN ?? "",
                    TypeDisplayName: typeDisplayName,
                    AttributeSettings: input.AttributeSettings,
                    NewModifier: "",
                    UnmanagedDataFQNs: input.UnmanagedDataFQNs,
                    CustomStorageProperties: customStorageProperties,
                    FindByIdTypeFQNs: input.TrackingIdTypeFQNs,
                    EmitDocs: true
                )
            );
        }

        foreach (var _ in declarations)
            src.CloseBrace();

        src.Linebreak();

        if (input.TypeFQN is { Length: > 0 })
        {
            EmitStaticInit(
                src: src,
                input: new(
                    TypeFQN: input.TypeFQN,
                    ContainingTypeFQN: input.ContainingTypeFQN,
                    Flags: input.Flags,
                    IsAutoInstantiate: false,
                    EmitTransformAccessInit: input.AttributeSettings.TrackTransforms,
                    TransformInitialCapacity: input.AttributeSettings.InitialCapacity,
                    TransformDesiredJobCount: input.AttributeSettings.DesiredJobCount
                ),
                containingDeclarations: declarations.AsSpan()[..^1]
            );
        }

        src.TrimEndWhitespace();
    }

    static void GenerateSource(SourceProductionContext context, SourceWriter src, GeneratorInput input)
    {
        if (input.Attribute is null)
            return;

        src.ShouldEmitDocs = input.Environment.ShouldEmitDocs;

        src.Line.Write("#pragma warning disable CS8321 // Local function is declared but never used");
        src.Line.Write("#pragma warning disable CS0618 // Type or member is obsolete");
        src.Line.Write(Alias.UsingStorage);
        src.Line.Write(Alias.UsingInline);
        src.Line.Write(Alias.UsingUtility);
        src.Line.Write(Alias.UsingNonSerialized);
        src.Linebreak();

        if (input.Attribute is TrackAttributeMetadataName)
        {
            input.EmitIInstanceIndex |= !input.HasIInstanceIndex;
            input.HasIInstanceIndex = true;
        }
        else
        {
            input.EmitIInstanceIndex = false;
            input.HasIInstanceIndex = false;
        }

        if (input.ManualInheritanceMismatch)
        {
            string attributeShortName = input.Attribute is SingletonAttributeMetadataName ? SingletonAttributeNameShort : "Track";
            string typeName = input.TypeFQN ?? "type";
            var location = input.SourceGeneratorLocation?.ToLocation() ?? Location.None;
            context.ReportDiagnostic(Diagnostic.Create(MED029, location, typeName, attributeShortName));
        }

        string @protected = input.Flags.Has(IsSealed) ? "" : "protected ";
        string @new = input.HasBaseDeclarationsWithAttribute ? "new " : "";
        bool hasBaseRegistrationMethod = input is { HasBaseDeclarationsWithAttribute: true, ManualInheritanceMismatch: false };
        string registrationNew = hasBaseRegistrationMethod ? "new " : "";
        var unmanagedDataInfo = input.UnmanagedDataFQNs.AsArray()
            .Select(x => (dataType: x, dataTypeShort: x.Split('.', ':').Last().HtmlEncode()))
            .ToArray();

        var customStorageProperties = GetCustomStorageProperties(
            context: context,
            storageTypeFQNs: input.CustomStorageTypeFQNs,
            trackedTypeFQN: input.TypeFQN,
            sourceGeneratorErrorLocation: input.SourceGeneratorLocation
        );

        var findByIdTypeFQNs = input.TrackingIdRegistrations.AsArray()
            .Select(x => x.IDTypeFQN)
            .Distinct()
            .ToArray();

        bool hasFindByAssetId = input.TrackingIdRegistrations.AsArray()
            .Any(x => x is { LookupTypeFQN: FindByAssetIdInterfaceFQN, IDTypeFQN: AssetIdTypeFQN });

        string[] trackedStorageTypes = input.Attribute is TrackAttributeMetadataName
            ? input.InterfacesWithAttribute.AsArray()
                .Prepend(input.TypeFQN!)
                .Distinct()
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray()
            : [];

        var declarations = input.ContainingTypeDeclaration.AsSpan();
        int lastDeclaration = declarations.Length - 1;
        for (int i = 0; i < declarations.Length; i++)
        {
            if (i == lastDeclaration && src.ShouldEmitDocs)
            {
                if (input.Attribute is TrackAttributeMetadataName)
                {
                    string active = input.Flags.Has(IsComponent) ? "(enabled) instances of this component" : "instances of this class";
                    src.Line.Write($"/// <remarks>");
                    src.Line.Write($"/// Active {active} are tracked, and contain additional generated static properties:");
                    src.Line.Write($"/// <list type=\"bullet\">");
                    WriteGeneratedArraysComment();
                    src.Line.Write($"/// </list>");
                    src.Line.Write($"/// And the following additional instance properties:");
                    src.Line.Write($"/// <list type=\"bullet\">");
                    src.Line.Write($"/// <item> The <see cref=\"{input.TypeDisplayName}.InstanceIndex\"/> property, which is the instance's index into the above arrays </item>");
                    foreach (var (_, dataTypeShort) in unmanagedDataInfo)
                        src.Line.Write($"/// <item> The <see cref=\"{input.TypeDisplayName}.Local{dataTypeShort}\"/> per-instance data accessor </item>");

                    src.Line.Write($"/// </list>");

                    src.Line.Write("/// </remarks>");
                }
                else if (input.Attribute is SingletonAttributeMetadataName)
                {
                    src.Line.Write($"/// <remarks>");
                    src.Line.Write($"/// This is a singleton class. The current instance of the singleton can be accessed via the");
                    src.Line.Write($"/// generated <see cref=\"{input.TypeDisplayName}.Instance\"/> static property.");
                    src.Line.Write($"/// </remarks>");
                }
            }

            src.Line.Write(declarations[i]);

            if (i == lastDeclaration)
            {
                bool hasInterfaces = false;

                if (input.Attribute is TrackAttributeMetadataName)
                {
                    AppendTrackedMarkerInterfaces(
                        src: src,
                        typeFqn: input.TypeFQN ?? "",
                        attributeSettings: input.AttributeSettings,
                        unmanagedDataFQNs: input.UnmanagedDataFQNs.AsArray(),
                        hasInterfaces: ref hasInterfaces
                    );

                    if (trackedStorageTypes.Length > 0)
                    {
                        AppendInterface(src, ref hasInterfaces, $"{IInstanceIndexInternalInterfaceFQN}<{trackedStorageTypes[0]}>");
                        for (int j = 1; j < trackedStorageTypes.Length; j++)
                            AppendInterface(src, ref hasInterfaces, $"{IInstanceIndexInternalInterfaceFQN}<{trackedStorageTypes[j]}>");
                    }
                }

                if (input.EmitIInstanceIndex)
                    AppendInterface(src, ref hasInterfaces, IInstanceIndexInterfaceFQN);
            }

            src.OpenBrace();
        }

        void EmitRegistrationMethod(string methodName, string? prependCall = null, Action? emitPreBody = null, params string?[] methodCalls)
        {
            bool register = methodName is "OnEnableINTERNAL";

            if (!input.AttributeSettings.Manual)
            {
                src.Line.Write(Alias.Hidden);

                if (!input.Flags.Has(IsSealed))
                    src.Line.Write(Alias.ObsoleteInternal);
            }
            else
            {
                methodName = register ? "RegisterInstance" : "UnregisterInstance";
                src.Doc?.Write($"/// <summary>");
                src.Doc?.Write($"/// Manually {(register ? "registers" : "unregisters")} this instance {(register ? "in" : "from")} the <c>{input.Attribute}</c> storage.");
                src.Doc?.Write($"/// </summary>");
                src.Doc?.Write($"/// <remarks>");
                src.Doc?.Write($"/// You <b>must ensure</b> that the instance always registers and unregisters itself symmetrically.");
                src.Doc?.Write($"/// This is usually achieved by hooking into reliable object lifecycle methods, such as <c>OnEnable</c>+<c>OnDisable</c>,");
                src.Doc?.Write($"/// or <c>Awake</c>+<c>OnDestroy</c>. Make sure the registration methods are never stopped by an earlier exception.");
                src.Doc?.Write($"/// </remarks>");
            }

            src.Line.Write($"{@protected}{registrationNew}void {methodName}()");

            using (src.Braces)
            {
                if (input is { Attribute: TrackAttributeMetadataName })
                {
                    string check = register
                        ? $"{m}MedicineInternalInstanceIndex >= 0"
                        : $"{m}MedicineInternalInstanceIndex is -1";

                    src.Line.Write($"if ({check})");
                    using (src.Indent)
                        src.Line.Write("return;");
                }

                if (prependCall is { Length: > 0 })
                    src.Line.Write(prependCall);

                if (hasBaseRegistrationMethod)
                    src.Line.Write($"base.{methodName}();");

                emitPreBody?.Invoke();

                foreach (var methodCall in methodCalls)
                    if (methodCall is not null)
                        src.Line.Write(methodCall).Write(';');
            }

            src.Linebreak();
        }

        void EmitAssetIdRefresh()
        {
            if (!hasFindByAssetId)
                return;

            if (!input.Environment.IsEditor)
                return;

            src.Line.Write($"if ({m}Utility.TryGetAssetID(this, out var {m}AssetId))");
            using (src.Indent)
                src.Line.Write($"{m}MedicineAssetId = {m}AssetId;");
        }

        void WriteGeneratedArraysComment()
        {
            src.Doc?.Write($"/// <item> The <see cref=\"{input.TypeDisplayName}.Instances\"/> tracked instance list </item>");

            if (input.AttributeSettings.TrackTransforms)
                src.Doc?.Write($"/// <item> The <see cref=\"{input.TypeDisplayName}.TransformAccessArray\"/> transform array </item>");

            foreach (var (_, dataTypeShort) in unmanagedDataInfo)
                src.Doc?.Write($"/// <item> The <see cref=\"{input.TypeDisplayName}.Unmanaged.{dataTypeShort}Array\"/> data array </item>");
        }

        void EmitTrackLocalUnmanagedAccessors()
        {
            if (input.Attribute is not TrackAttributeMetadataName)
                return;

            foreach (var (dataType, dataTypeShort) in unmanagedDataInfo)
            {
                src.Linebreak();
                src.Doc?.Write($"/// <summary>");
                src.Doc?.Write($"/// Gets a reference to the <see cref=\"{dataTypeShort}\"/> data for the tracked type's currently active instance.");
                src.Doc?.Write($"/// You can use this reference in jobs or Burst-compiled functions.");
                src.Doc?.Write($"/// </summary>");
                src.Doc?.Write($"/// <remarks>");
                src.Doc?.Write($"/// The reference corresponds to the tracked instance with the appropriate instance index.");
                src.Doc?.Write($"/// You can also access data statically via <see cref=\"{input.TypeDisplayName}.Unmanaged.{dataTypeShort}Array\"/>");
                src.Doc?.Write($"/// and initialize/dispose it by overriding methods in <see cref=\"Medicine.IUnmanagedData{{T}}\"/>.");

                src.Line.Write($"public ref {dataType} Local{dataTypeShort}");
                using (src.Braces)
                {
                    src.Line.Write($"{Alias.Inline} get");
                    using (src.Indent)
                        src.Line.Write($"=> ref {m}Storage.UnmanagedData<{input.TypeFQN}, {dataType}>.ElementAtRefRW({m}MedicineInternalInstanceIndex);");
                }
            }

            if (unmanagedDataInfo.Length > 0)
                src.Linebreak();
        }

        if (input.Attribute is TrackAttributeMetadataName)
        {
            src.Doc?.Write($"/// <summary>");
            src.Doc?.Write($"/// Represents the index of this instance in the following static storage arrays:");
            src.Doc?.Write($"/// <list type=\"bullet\">");
            WriteGeneratedArraysComment();
            src.Doc?.Write($"/// </list>");
            src.Doc?.Write($"/// </summary>");
            src.Doc?.Write($"/// <remarks>");
            src.Doc?.Write($"/// This property is automatically updated.");
            src.Doc?.Write($"/// Note that the instance index will change during the lifetime of the instance - never store it.");
            src.Doc?.Write($"/// <br/><br/>");
            src.Doc?.Write($"/// A value of -1 indicates that the instance is not currently active/registered.");
            src.Doc?.Write($"/// </remarks>");

            src.Line.Write($"public int InstanceIndex => {m}MedicineInternalInstanceIndex;");
            src.Linebreak();

            src.Line.Write($"int {IInstanceIndexInterfaceFQN}.InstanceIndex");
            using (src.Braces)
            {
                src.Line.Write($"{Alias.Inline} get => {m}MedicineInternalInstanceIndex;");
                src.Line.Write($"{Alias.Inline} set => {m}MedicineInternalInstanceIndex = value;");
            }

            src.Line.Write($"int {IInstanceIndexInternalInterfaceFQN}<{input.TypeFQN}>.InstanceIndex");
            using (src.Braces)
            {
                src.Line.Write($"{Alias.Inline} get => {m}MedicineInternalInstanceIndex;");
                src.Line.Write($"{Alias.Inline} set => {m}MedicineInternalInstanceIndex = value;");
            }

            src.Linebreak();

            src.Line.Write(Alias.Hidden);
            src.Line.Write($"[{m}NS] int {m}MedicineInternalInstanceIndex = -1;");
            src.Linebreak();

            var trackedInterfaceStorageTypes = trackedStorageTypes
                .Where(x => x != input.TypeFQN)
                .ToArray();

            for (int i = 0; i < trackedInterfaceStorageTypes.Length; i++)
            {
                string interfaceStorageType = trackedInterfaceStorageTypes[i];
                string indexFieldName = $"{m}MedicineInternalInstanceIndexFor{i}_{interfaceStorageType.Sanitize()}";

                src.Line.Write($"int {IInstanceIndexInternalInterfaceFQN}<{interfaceStorageType}>.InstanceIndex");
                using (src.Braces)
                {
                    src.Line.Write($"{Alias.Inline} get => {indexFieldName};");
                    src.Line.Write($"{Alias.Inline} set => {indexFieldName} = value;");
                }

                src.Linebreak();

                src.Line.Write(Alias.Hidden);
                src.Line.Write($"[{m}NS] int {indexFieldName} = -1;");
                src.Linebreak();
            }
        }

        if (hasFindByAssetId)
        {
            src.Line.Write(Alias.Hidden);
            src.Line.Write("[global::UnityEngine.SerializeField]");
            src.Line.Write("[global::UnityEngine.HideInInspector]");
            src.Line.Write($"{AssetIdTypeFQN} {m}MedicineAssetId;");
            src.Linebreak();

            src.Line.Write($"{AssetIdTypeFQN} {TrackingIdInterfaceFQN}<{AssetIdTypeFQN}>.ID");
            using (src.Braces)
                src.Line.Write($"{Alias.Inline} get => {m}MedicineAssetId;");

            src.Linebreak();

            src.Line.Write($"public {AssetIdTypeFQN} AssetID");
            using (src.Braces)
                src.Line.Write($"{Alias.Inline} get => {m}MedicineAssetId;");

            src.Linebreak();
        }

        var effectiveSingletonStrategy = input.AttributeSettings.SingletonStrategy ?? input.Environment.MedicineSettings.SingletonStrategy;

        // todo: extract singleton emit pipeline to a separate source generator?
        // the [Singleton] path is increasingly distinct from the [Track] path; how much code would we need to duplicate?
        if (input.Attribute is SingletonAttributeMetadataName)
        {
            src.Doc?.Write($"/// <summary>");
            src.Doc?.Write($"/// Retrieves the active <see cref=\"{input.TypeDisplayName}\"/> singleton instance.");
            src.Doc?.Write($"/// </summary>");
            src.Doc?.Write($"/// <remarks>");
            src.Doc?.Write($"/// <list type=\"bullet\">");
            src.Doc?.Write($"/// <item> This property <b>might return null</b> if the singleton instance has not been registered yet");
            src.Doc?.Write($"/// (or has been disabled/destroyed). </item>");
            src.Doc?.Write($"/// <item> MonoBehaviours and ScriptableObjects marked with the <see cref=\"SingletonAttribute\"/> will");
            src.Doc?.Write($"/// automatically register/unregister themselves as the active singleton instance in OnEnable/OnDisable. </item>");
            src.Doc?.Write($"/// <item> In edit mode, to provide better compatibility with editor tooling, <c>FindObjectsByType</c>");
            src.Doc?.Write($"/// is used internally to attempt to locate the object (cached for one editor update). </item>");
            src.Doc?.Write($"/// </list>");
            src.Doc?.Write($"/// </remarks>");

            src.Line.Write($"public static {@new}{input.TypeFQN}? Instance");

            using (src.Braces)
            {
                src.Line.Write(Alias.Inline);
                src.Line.Write($"get => {m}Storage.Singleton<{input.TypeFQN}>.Instance;");
            }

            const string stratFQN = $"{SingletonAttributeFQN}.Strategy";

            src.Linebreak();
            EmitRegistrationMethod(
                methodName: "OnEnableINTERNAL",
                prependCall: $"const {stratFQN} {m}singletonStrategy = ({stratFQN}){(ulong)effectiveSingletonStrategy};",
                methodCalls:
                [
                    $"{m}Storage.Singleton<{input.TypeFQN}>.Register(this, {m}singletonStrategy)",
                    ..input.InterfacesWithAttribute.AsArray().Select(x => $"{m}Storage.Singleton<{x}>.Register(this, {m}singletonStrategy)"),
                ]
            );

            EmitRegistrationMethod(
                methodName: "OnDisableINTERNAL",
                prependCall: $"const {stratFQN} {m}singletonStrategy = ({stratFQN}){(ulong)effectiveSingletonStrategy};",
                methodCalls:
                [
                    $"{m}Storage.Singleton<{input.TypeFQN}>.Unregister(this, {m}singletonStrategy)",
                    ..input.InterfacesWithAttribute.AsArray().Select(x => $"{m}Storage.Singleton<{x}>.Unregister(this, {m}singletonStrategy)"),
                ]
            );

            if (input.Environment.IsEditor)
            {
                src.Linebreak();
                src.Line.Write(Alias.Hidden);
                src.Line.Write(Alias.ObsoleteInternal);
                src.Line.Write($"[{m}NS] int {m}MedicineInvalidateInstanceToken = {m}Storage.Singleton<{input.TypeFQN}>.EditMode.Invalidate();");
            }
        }
        else if (input.Attribute is TrackAttributeMetadataName)
        {
            EmitTrackApiSurface(
                src, new(
                    TypeFQN: input.TypeFQN ?? "",
                    TypeDisplayName: input.TypeDisplayName ?? input.TypeFQN ?? "",
                    AttributeSettings: input.AttributeSettings,
                    NewModifier: @new,
                    UnmanagedDataFQNs: input.UnmanagedDataFQNs,
                    CustomStorageProperties: customStorageProperties,
                    FindByIdTypeFQNs: findByIdTypeFQNs,
                    EmitDocs: input.Environment.IsEditor
                )
            );

            EmitTrackLocalUnmanagedAccessors();

            var trackedInterfaces = input.TrackedInterfacesDeferred?.Value ?? [];

            IEnumerable<string> EnumerateTrackedInterfaceEnableCalls()
            {
                foreach (var trackedInterface in trackedInterfaces)
                {
                    yield return $"{m}Storage.Instances<{trackedInterface.TypeFQN}>.Register(this)";

                    if (input.Flags.Has(IsMonoBehaviour))
                    {
                        if (trackedInterface.UseTransformFallback)
                            yield return $"{m}Storage.TransformAccess<{trackedInterface.TypeFQN}>.Register(transform)";
                        else
                            yield return $"if ({m}Storage.TransformAccess<{trackedInterface.TypeFQN}>.Transforms.isCreated) {m}Storage.TransformAccess<{trackedInterface.TypeFQN}>.Register(transform)";
                    }

                    foreach (var unmanagedDataType in trackedInterface.UnmanagedDataFQNs.AsArray())
                        yield return $"{m}Storage.UnmanagedData<{trackedInterface.TypeFQN}, {unmanagedDataType}>.Register(this)";

                    foreach (var customStorageType in trackedInterface.CustomStorageTypeFQNs.AsArray())
                        yield return $"{m}Storage.Custom<{trackedInterface.TypeFQN}, {customStorageType}>.Register(this)";
                }
            }

            IEnumerable<string> EnumerateTrackedInterfaceDisableCalls()
            {
                int i = 0;
                foreach (var trackedInterface in trackedInterfaces.AsEnumerable().Reverse())
                {
                    string indexName = $"{m}MedicineInternalInterfaceIndex{i++}";
                    yield return $"int {indexName} = {m}Storage.Instances<{trackedInterface.TypeFQN}>.Unregister(this)";

                    foreach (var customStorageType in trackedInterface.CustomStorageTypeFQNs.AsArray())
                        yield return $"{m}Storage.Custom<{trackedInterface.TypeFQN}, {customStorageType}>.Unregister(this, {indexName})";

                    foreach (var unmanagedDataType in trackedInterface.UnmanagedDataFQNs.AsArray())
                        yield return $"{m}Storage.UnmanagedData<{trackedInterface.TypeFQN}, {unmanagedDataType}>.Unregister(this, {indexName})";

                    if (input.Flags.Has(IsMonoBehaviour))
                    {
                        if (trackedInterface.UseTransformFallback)
                            yield return $"{m}Storage.TransformAccess<{trackedInterface.TypeFQN}>.Unregister({indexName})";
                        else
                            yield return $"if ({m}Storage.TransformAccess<{trackedInterface.TypeFQN}>.Transforms.isCreated) {m}Storage.TransformAccess<{trackedInterface.TypeFQN}>.Unregister({indexName})";
                    }
                }
            }

            EmitRegistrationMethod(
                methodName: "OnEnableINTERNAL",
                emitPreBody: EmitAssetIdRefresh,
                methodCalls:
                [
                    // keep first
                    input.AttributeSettings.CacheEnabledState ? $"{m}MedicineInternalCachedEnabledState = true" : null,
                    $"{m}Storage.Instances<{input.TypeFQN}>.Register(this)",

                    input.AttributeSettings.TrackTransforms ? $"{m}Storage.TransformAccess<{input.TypeFQN}>.Register(transform)" : null,
                    ..EnumerateTrackedInterfaceEnableCalls(),
                    ..input.UnmanagedDataFQNs.AsArray().Select(x => $"{m}Storage.UnmanagedData<{input.TypeFQN}, {x}>.Register(this)"),
                    ..input.CustomStorageTypeFQNs.AsArray().Select(x => $"{m}Storage.Custom<{input.TypeFQN}, {x}>.Register(this)"),
                    ..input.TrackingIdRegistrations.AsArray().Select(x => $"{m}Storage.LookupByID<{x.LookupTypeFQN}, {x.IDTypeFQN}>.Register(this)"),
                ]
            );

            EmitRegistrationMethod(
                methodName: "OnDisableINTERNAL",
                methodCalls:
                [
                    // keep first
                    input.AttributeSettings.CacheEnabledState ? $"{m}MedicineInternalCachedEnabledState = false" : null,
                    $"int index = {m}Storage.Instances<{input.TypeFQN}>.Unregister(this)",
                    // we try to execute the unregistration calls in reverse order;
                    // shouldn't really make a difference, but it seems like a good practice
                    ..input.TrackingIdRegistrations.AsArray().Select(x => $"{m}Storage.LookupByID<{x.LookupTypeFQN}, {x.IDTypeFQN}>.Unregister(this)").Reverse(),
                    ..input.CustomStorageTypeFQNs.AsArray().Select(x => $"{m}Storage.Custom<{input.TypeFQN}, {x}>.Unregister(this, index)").Reverse(),
                    ..input.UnmanagedDataFQNs.AsArray().Select(x => $"{m}Storage.UnmanagedData<{input.TypeFQN}, {x}>.Unregister(this, index)").Reverse(),
                    ..EnumerateTrackedInterfaceDisableCalls(),
                    input.AttributeSettings.TrackTransforms ? $"{m}Storage.TransformAccess<{input.TypeFQN}>.Unregister(index)" : null,
                ]
            );

            if (input.Environment.IsEditor)
            {
                src.Linebreak();
                src.Line.Write(Alias.Hidden);
                src.Line.Write(Alias.ObsoleteInternal);
                src.Line.Write($"[{m}NS] int {m}MedicineInvalidateInstanceToken = {m}Storage.Instances<{input.TypeFQN}>.EditMode.Invalidate();");
            }

            if (input.AttributeSettings.CacheEnabledState)
            {
                src.Linebreak();
                src.Line.Write(Alias.Hidden);
                src.Line.Write($"[{m}NS] bool {m}MedicineInternalCachedEnabledState;");

                src.Linebreak();
                src.Doc?.Write($"/// <summary>");
                src.Doc?.Write($"/// Enabled components are updated, disabled components are not.");
                src.Doc?.Write($"/// </summary>");
                src.Doc?.Write($"/// <remarks>");
                src.Doc?.Write($"/// This property returns the cached value of <see cref=\"UnityEngine.Behaviour.enabled\"/>.");
                src.Doc?.Write($"/// Setting this property normally updates the component's enabled state.");
                src.Doc?.Write($"/// The cached value is automatically updated when the component is enabled/disabled.<br/><br/>");
                src.Doc?.Write($"/// This generated property effectively hides the built-in <c>enabled</c>");
                src.Doc?.Write($"/// property, and tries to exactly replicate its behaviour.");
                src.Doc?.Write($"/// </remarks>");
                src.Doc?.Write($"/// <codegen>Generated because of the <c>TrackAttribute</c> parameter: <c>cacheEnabledState</c></codegen>");
                src.Line.Write("public new bool enabled");
                using (src.Braces)
                {
                    src.Line.Write(Alias.Inline);
                    src.Line.Write("get");
                    using (src.Braces)
                    {
                        src.Line.Write($"if ({m}Utility.EditMode)");
                        using (src.Indent)
                            src.Line.Write($"return base.enabled;");

                        src.Line.Write($"return {m}MedicineInternalCachedEnabledState;");
                    }

                    src.Line.Write($"{Alias.Inline} set => base.enabled = value;");
                }
            }
        }

        src.TrimEndWhitespace();
        foreach (var _ in declarations)
            src.CloseBrace();

        src.Linebreak();

        EmitStaticInit(
            src: src,
            input: new(
                TypeFQN: input.TypeFQN ?? "",
                ContainingTypeFQN: input.ContainingTypeFQN,
                Flags: input.Flags,
                IsAutoInstantiate: input is { Attribute: SingletonAttributeMetadataName } && effectiveSingletonStrategy.Has(SingletonStrategy.AutoInstantiate),
                EmitTransformAccessInit: input is { Attribute: TrackAttributeMetadataName, AttributeSettings.TrackTransforms: true },
                TransformInitialCapacity: input.AttributeSettings.InitialCapacity,
                TransformDesiredJobCount: input.AttributeSettings.DesiredJobCount
            ),
            containingDeclarations: declarations[..^1]
        );
    }

    static void EmitStaticInit(SourceWriter src, StaticInitInput input, Span<string> containingDeclarations)
    {
        if (input.Flags.Has(IsGenericType))
            return;

        string typeNameSanitized = input.TypeFQN.Sanitize();
        string typeFlagsInitName = $"InitBakedTypeInfo_{typeNameSanitized}";
        string transformInitName = $"InitTransformAccess_{typeNameSanitized}";

        void AssignTypeFlags()
        {
            using (src.Indent)
            {
                src.Line.Write($" = 0");

                if (input.Flags.Has(IsValueType))
                    src.Line.Write($" | {m}Utility.TypeFlags.IsValueType");
                else
                    src.Line.Write($" | {m}Utility.TypeFlags.IsReferenceType");

                if (input.Flags.Has(IsInterface))
                    src.Line.Write($" | {m}Utility.TypeFlags.IsInterface");

                if (input.Flags.Has(IsAbstract))
                    src.Line.Write($" | {m}Utility.TypeFlags.IsAbstract");

                if (input.Flags.Has(IsUnityEngineObject))
                    src.Line.Write($" | {m}Utility.TypeFlags.IsUnityEngineObject");

                if (input.Flags.Has(IsComponent))
                    src.Line.Write($" | {m}Utility.TypeFlags.IsComponent");

                if (input.Flags.Has(IsMonoBehaviour))
                    src.Line.Write($" | {m}Utility.TypeFlags.IsMonoBehaviour");

                if (input.Flags.Has(IsScriptableObject))
                    src.Line.Write($" | {m}Utility.TypeFlags.IsScriptableObject");

                if (input.IsAutoInstantiate)
                    src.Line.Write($" | {m}Utility.TypeFlags.IsAutoInstantiate");

                src.Write(";");
            }
        }

        if (input.Flags.Has(IsAccessible))
        {
            // this is the straightforward static init path
            src.Line.Write($"namespace Medicine.Internal");
            using (src.Braces)
            {
                src.Line.Write($"static partial class {m}StaticInit");
                using (src.Braces)
                {
                    src.Line.Write($"static {m}Utility.TypeFlags {typeFlagsInitName}");
                    using (src.Indent)
                    {
                        src.Line.Write($"= Medicine.Internal.Utility.BakedTypeInfo<{input.TypeFQN}>.Flags");
                        AssignTypeFlags();
                    }

                    if (input.EmitTransformAccessInit)
                    {
                        src.Linebreak();
                        src.Line.Write($"static int {transformInitName}");
                        using (src.Indent)
                            src.Line.Write($"= {m}Storage.TransformAccess<{input.TypeFQN}>.Initialize({input.TransformInitialCapacity}, {input.TransformDesiredJobCount});");
                    }
                }
            }

            src.Linebreak();
        }
        else if (input.Flags.Has(ContainingTypeIsAccessible) && containingDeclarations.Length is not 0)
        {
            // fallback for private classes that are nested:
            // we can't reference the tracked type's name from the assembly scope, so we need to emit this
            // intermediate method in the containing type where the tracked type is accessible

            foreach (var declaration in containingDeclarations)
                src.Line.Write(declaration).OpenBrace();

            src.Line.Write(Alias.Hidden);
            src.Line.Write($"internal static {m}Utility.TypeFlags {typeFlagsInitName}()");
            using (src.Indent)
            {
                src.Line.Write($"=> Medicine.Internal.Utility.BakedTypeInfo<{input.TypeFQN}>.Flags");
                AssignTypeFlags();
            }

            if (input.EmitTransformAccessInit)
            {
                src.Line.Write(Alias.Hidden);
                src.Line.Write($"internal static int {transformInitName}()");
                using (src.Indent)
                    src.Line.Write($"=> {m}Storage.TransformAccess<{input.TypeFQN}>.Initialize({input.TransformInitialCapacity}, {input.TransformDesiredJobCount});");
            }

            foreach (var _ in containingDeclarations)
                src.CloseBrace();

            src.Linebreak();

            string containingTypeFqn = input.ContainingTypeFQN ?? input.TypeFQN;
            src.Line.Write($"namespace Medicine.Internal");
            using (src.Braces)
            {
                src.Line.Write($"static partial class {m}StaticInit");
                using (src.Braces)
                {
                    src.Line.Write($"static {m}Utility.TypeFlags {typeFlagsInitName}");
                    using (src.Indent)
                        src.Line.Write($"= {containingTypeFqn}.{typeFlagsInitName}();");

                    if (input.EmitTransformAccessInit)
                    {
                        src.Linebreak();
                        src.Line.Write($"static int {transformInitName}");
                        using (src.Indent)
                            src.Line.Write($"= {containingTypeFqn}.{transformInitName}();");
                    }
                }
            }

            src.Linebreak();
        }
    }

    static void EmitTrackApiSurface(SourceWriter src, TrackApiSurfaceInput input)
    {
        EmitTrackInstancesProperty(src, input);
        EmitTrackTransformAccessArray(src, input);
        EmitTrackUnmanagedStaticArrays(src, input);
        EmitTrackCustomStorageProperties(src, input);
        EmitTrackFindByIDMethods(src, input);
    }

    static void EmitTrackInstancesProperty(SourceWriter src, in TrackApiSurfaceInput input)
    {
        src.Doc?.Write($"/// <summary>");
        src.Doc?.Write($"/// Allows enumeration of all enabled instances of <see cref=\"{input.TypeDisplayName}\"/>.");
        src.Doc?.Write($"/// </summary>");
        src.Doc?.Write($"/// <remarks>");
        src.Doc?.Write($"/// <list type=\"bullet\">");
        src.Doc?.Write($"/// <item> MonoBehaviours and ScriptableObjects marked with the <see cref=\"TrackAttribute\"/> will automatically register/unregister themselves");
        src.Doc?.Write($"/// in the active instance list in OnEnable/OnDisable. </item>");
        src.Doc?.Write($"/// <item> When there are no active instances, the returned enumerable is empty. </item>");
        src.Doc?.Write($"/// <item> In edit mode, to provide better compatibility with editor tooling, <see cref=\"Object.FindObjectsByType(System.Type,UnityEngine.FindObjectsSortMode)\"/>");
        src.Doc?.Write($"/// is used internally to find object instances (cached for one editor update). </item>");
        src.Doc?.Write($"/// <item> You can use <c>foreach</c> to iterate over the instances. </item>");
        src.Doc?.Write($"/// <item> If you're enabling/disabling instances while enumerating, you need to use <c>{input.TypeDisplayName}.Instances.WithCopy</c>. </item>");
        src.Doc?.Write($"/// <item> The returned struct is compatible with <a href=\"https://github.com/Cysharp/ZLinq\">ZLINQ</a>. </item>");
        src.Doc?.Write($"/// </list>");
        src.Doc?.Write($"/// </remarks>");

        src.Line.Write($"public {input.NewModifier}static global::Medicine.TrackedInstances<{input.TypeFQN}> Instances");
        using (src.Braces)
            src.Line.Write($"{Alias.Inline} get => default;");

        src.Linebreak();
    }

    static void EmitTrackTransformAccessArray(SourceWriter src, in TrackApiSurfaceInput input)
    {
        if (!input.AttributeSettings.TrackTransforms)
            return;

        if (input.EmitDocs)
        {
            src.Doc?.Write($"/// <summary>");
            src.Doc?.Write($"/// Allows job access to the transforms of the tracked {input.TypeDisplayName} instances.");
            src.Doc?.Write($"/// </summary>");
        }

        src.Line.Write($"{input.NewModifier}public static global::UnityEngine.Jobs.TransformAccessArray TransformAccessArray");
        using (src.Braces)
        {
            src.Line.Write(Alias.Inline).Write(" get");
            using (src.Braces)
                src.Line.Write($"return {m}Storage.TransformAccess<{input.TypeFQN}>.Transforms;");
        }

        src.Linebreak();
    }

    static void EmitTrackUnmanagedStaticArrays(SourceWriter src, in TrackApiSurfaceInput input)
    {
        if (input.UnmanagedDataFQNs.Length is 0)
            return;

        var unmanagedDataInfo = input.UnmanagedDataFQNs.AsArray()
            .Select(x => (dataType: x, dataTypeShort: x.Split('.', ':').Last().HtmlEncode()))
            .ToArray();

        src.Doc?.Write($"/// <summary>");
        src.Doc?.Write($"/// Allows job access to the unmanaged data arrays of the tracked {input.TypeDisplayName} instances");
        src.Doc?.Write($"/// of this component type.");
        src.Doc?.Write($"/// </summary>");
        src.Doc?.Write($"/// <remarks>");
        src.Doc?.Write($"/// The unmanaged data is stored in a <see cref=\"global::Unity.Collections.NativeArray{{T}}\"/>, where each element");
        src.Doc?.Write($"/// corresponds to the tracked instance with the appropriate <see cref=\"Medicine.IInstanceIndex\">instance index</see>.");
        src.Doc?.Write($"/// </remarks>");
        string s = input.UnmanagedDataFQNs.Length > 1 ? "s" : "";
        string origin = unmanagedDataInfo.Select(x => $"<c>IUnmanagedData&lt;{x.dataTypeShort}&gt;</c>").Join(", ");
        src.Doc?.Write($"/// <codegen>Generated because of the implemented interface{s}: {origin}</codegen>");

        src.Line.Write($"public static {input.NewModifier}partial class Unmanaged");
        using (src.Braces)
        {
            foreach (var (dataType, dataTypeShort) in unmanagedDataInfo)
            {
                src.Linebreak();
                src.Doc?.Write($"/// <summary>");
                src.Doc?.Write($"/// Gets an array of <see cref=\"{dataTypeShort}\"/> data for the tracked type's currently active instances.");
                src.Doc?.Write($"/// You can use this array in jobs or Burst-compiled functions.");
                src.Doc?.Write($"/// </summary>");
                src.Doc?.Write($"/// <remarks>");
                src.Doc?.Write($"/// Each element in the native array corresponds to the tracked instance with the appropriate instance index.");
                src.Doc?.Write($"/// You can access per-instance data via the generated <see cref=\"{input.TypeDisplayName}.Local{dataTypeShort}\"/> property,");
                src.Doc?.Write($"/// or you can access data statically via this array and");
                src.Doc?.Write($"/// initialize/dispose it by overriding the methods in the <see cref=\"Medicine.IUnmanagedData{{T}}\"/> interface.");
                src.Doc?.Write($"/// </remarks>");
                src.Doc?.Write($"/// <codegen>Generated because of the following implemented interface: <c>IUnmanagedData&lt;{dataTypeShort}&gt;</c></codegen>");

                src.Line.Write($"public static ref global::Unity.Collections.NativeArray<{dataType}> {dataTypeShort}Array");
                using (src.Braces)
                    src.Line.Write($"{Alias.Inline} get => ref {m}Storage.UnmanagedData<{input.TypeFQN}, {dataType}>.Array;");
            }
        }

        src.Linebreak();
    }

    static void EmitTrackCustomStorageProperties(SourceWriter src, in TrackApiSurfaceInput input)
    {
        if (input.CustomStorageProperties.Length is 0)
            return;

        string s = input.CustomStorageProperties.Length > 1 ? "s" : "";
        string origin = input.CustomStorageProperties.AsArray()
            .Select(x => $"<c>ICustomStorage&lt;{x.StorageTypeShort}&gt;</c>")
            .Join(", ");

        src.Doc?.Write("/// <summary>");
        src.Doc?.Write($"/// Provides access to custom tracking storage for <see cref=\"{input.TypeDisplayName}\"/>.");
        src.Doc?.Write("/// </summary>");
        src.Doc?.Write("/// <remarks>");
        src.Doc?.Write("/// Custom storage instances are created when the first tracked instance is registered and disposed");
        src.Doc?.Write("/// when the last tracked instance is unregistered.");
        src.Doc?.Write("/// </remarks>");
        src.Doc?.Write($"/// <codegen>Generated because of the implemented interface{s}: {origin}</codegen>");

        foreach (var customStorage in input.CustomStorageProperties.AsArray())
        {
            src.Linebreak();
            src.Doc?.Write("/// <summary>");
            src.Doc?.Write($"/// Gets the shared <see cref=\"{customStorage.StorageTypeShort}\"/> custom storage instance.");
            src.Doc?.Write("/// </summary>");
            src.Doc?.Write($"/// <codegen>Generated because of the implemented interface: <c>ICustomStorage&lt;{customStorage.StorageTypeShort}&gt;</c></codegen>");

            src.Line.Write($"public {input.NewModifier}static ref {customStorage.StorageTypeFQN} {customStorage.PropertyName}");
            using (src.Braces)
                src.Line.Write($"{Alias.Inline} get => ref {m}Storage.Custom<{input.TypeFQN}, {customStorage.StorageTypeFQN}>.Storage;");
        }

        src.Linebreak();
    }

    static void EmitTrackFindByIDMethods(SourceWriter src, in TrackApiSurfaceInput input)
    {
        foreach (var idType in input.FindByIdTypeFQNs.AsArray().Distinct())
        {
            src.Doc?.Write($"/// <summary>");
            src.Doc?.Write($"/// Returns the tracked instance associated with the provided ID.");
            src.Doc?.Write($"/// </summary>");
            src.Doc?.Write($"/// <param name=\"id\">The ID value to resolve.</param>");
            src.Doc?.Write($"/// <returns>The matching <see cref=\"{input.TypeDisplayName}\"/> instance, or <c>null</c> when no matching instance exists.</returns>");

            src.Line.Write(Alias.Inline);
            src.Line.Write($"public static {input.TypeFQN}? FindByID({idType} id)");
            using (src.Indent)
                src.Line.Write($"=> {m}Storage.LookupByID<{input.TypeFQN}, {idType}>.Find(id);");

            src.Linebreak();

            src.Doc?.Write($"/// <summary>");
            src.Doc?.Write($"/// Attempts to resolve a tracked instance by ID.");
            src.Doc?.Write($"/// </summary>");
            src.Doc?.Write($"/// <param name=\"id\">The ID value to resolve.</param>");
            src.Doc?.Write($"/// <param name=\"result\">When this method returns <c>true</c>, contains the resolved <see cref=\"{input.TypeDisplayName}\"/> instance; otherwise <c>null</c>.</param>");
            src.Doc?.Write($"/// <returns><c>true</c> when a matching instance was found; otherwise <c>false</c>.</returns>");

            src.Line.Write(Alias.Inline);
            src.Line.Write($"public static bool TryFindByID({idType} id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out {input.TypeFQN}? result)");
            using (src.Indent)
                src.Line.Write($"=> {m}Storage.LookupByID<{input.TypeFQN}, {idType}>.TryFind(id, out result);");

            src.Linebreak();
        }
    }

    static CustomStoragePropertyInfo[] GetCustomStorageProperties(
        SourceProductionContext context,
        EquatableArray<string> storageTypeFQNs,
        string? trackedTypeFQN,
        LocationInfo? sourceGeneratorErrorLocation
    )
    {
        using var r1 = Scratch.RentA<List<CustomStoragePropertyInfo>>(out var result);
        using var r2 = Scratch.RentA<HashSet<string>>(out var seenStorageTypes);
        using var r3 = Scratch.RentA<List<string>>(out var sortedStorageTypes);

        foreach (var storageTypeFQN in storageTypeFQNs.AsArray())
            if (seenStorageTypes.Add(storageTypeFQN))
                sortedStorageTypes.Add(storageTypeFQN);

        sortedStorageTypes.Sort(StringComparer.Ordinal);

        foreach (var storageTypeFQN in sortedStorageTypes)
        {
            string storageTypeShortRaw = GetTypeNameTail(storageTypeFQN);
            string propertyName = GetCustomStoragePropertyName(storageTypeShortRaw);

            foreach (var existing in result)
            {
                if (!existing.PropertyName.Equals(propertyName, StringComparison.Ordinal))
                    continue;

                context.ReportDiagnostic(
                    Diagnostic.Create(
                        descriptor: MED037,
                        location: sourceGeneratorErrorLocation?.ToLocation() ?? Location.None,
                        messageArgs:
                        [
                            trackedTypeFQN ?? "type",
                            propertyName,
                            existing.StorageTypeFQN,
                            storageTypeFQN
                        ]
                    )
                );

                return [];
            }

            result.Add(
                new(
                    StorageTypeFQN: storageTypeFQN,
                    StorageTypeShort: storageTypeShortRaw.HtmlEncode(),
                    PropertyName: propertyName
                )
            );
        }

        return result.ToArray();

        static string GetTypeNameTail(string typeFQN)
        {
            for (int i = typeFQN.Length - 1; i >= 0; i--)
                if (typeFQN[i] is '.' or ':')
                    return typeFQN[(i + 1)..];

            return typeFQN;
        }

        static string GetCustomStoragePropertyName(string storageTypeShort)
        {
            string propertyName = storageTypeShort.EndsWith("Storage", StringComparison.Ordinal)
                ? storageTypeShort
                : $"{storageTypeShort}Storage";

            return propertyName.Sanitize();
        }
    }
}
