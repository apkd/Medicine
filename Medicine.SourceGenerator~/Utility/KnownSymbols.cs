using Microsoft.CodeAnalysis;

/// <summary>
/// Represents configuration settings extracted from the [MedicineSettings] attribute
/// defined in the given assembly in the current compilation, as well as other compilation parameters.
/// </summary>
public readonly record struct MedicineSettings
{
    public SingletonStrategy SingletonStrategy { get; init; }
    public bool? MakePublic { get; init; }
}

public readonly record struct GeneratorEnvironment(KnownSymbols KnownSymbols, ActivePreprocessorSymbolNames PreprocessorSymbols, MedicineSettings MedicineSettings)
{
    public readonly KnownSymbols KnownSymbols = KnownSymbols;
    public readonly ActivePreprocessorSymbolNames PreprocessorSymbols = PreprocessorSymbols;
    public readonly MedicineSettings MedicineSettings = MedicineSettings;

    public bool ShouldEmitDocs => PreprocessorSymbols.Has(ActivePreprocessorSymbolNames.DEBUG);
    public bool IsEditor => PreprocessorSymbols.Has(ActivePreprocessorSymbolNames.UNITY_EDITOR);

    bool IEquatable<GeneratorEnvironment>.Equals(GeneratorEnvironment other) => false;
}

public readonly record struct KnownSymbols
{
    readonly EquatableIgnore<INamedTypeSymbol> unityObject;
    readonly EquatableIgnore<INamedTypeSymbol> unityComponent;
    readonly EquatableIgnore<INamedTypeSymbol> unityMonoBehaviour;
    readonly EquatableIgnore<INamedTypeSymbol> unityScriptableObject;

    readonly EquatableIgnore<INamedTypeSymbol> medicineFind;
    readonly EquatableIgnore<INamedTypeSymbol> injectAttribute;
    readonly EquatableIgnore<INamedTypeSymbol> singletonAttribute;
    readonly EquatableIgnore<INamedTypeSymbol> trackAttribute;
    readonly EquatableIgnore<INamedTypeSymbol> unmanagedAccessAttribute;
    readonly EquatableIgnore<INamedTypeSymbol> iInstanceIndexInterface;
    readonly EquatableIgnore<INamedTypeSymbol> unmanagedDataInterface;
    readonly EquatableIgnore<INamedTypeSymbol> trackingIdInterface;
    readonly EquatableIgnore<INamedTypeSymbol> trackedInstances;
    readonly EquatableIgnore<INamedTypeSymbol> lazyRef;
    readonly EquatableIgnore<INamedTypeSymbol> lazyVal;

    readonly EquatableIgnore<INamedTypeSymbol> systemIDisposable;
    readonly EquatableIgnore<INamedTypeSymbol> systemFunc1;

    public INamedTypeSymbol UnityObject => unityObject.Value;
    public INamedTypeSymbol UnityComponent => unityComponent.Value;
    public INamedTypeSymbol UnityMonoBehaviour => unityMonoBehaviour.Value;
    public INamedTypeSymbol UnityScriptableObject => unityScriptableObject.Value;

    public INamedTypeSymbol MedicineFind => medicineFind.Value;
    public INamedTypeSymbol InjectAttribute => injectAttribute.Value;
    public INamedTypeSymbol SingletonAttribute => singletonAttribute.Value;
    public INamedTypeSymbol TrackAttribute => trackAttribute.Value;
    public INamedTypeSymbol UnmanagedAccessAttribute => unmanagedAccessAttribute.Value;
    public INamedTypeSymbol IInstanceIndexInterface => iInstanceIndexInterface.Value;
    public INamedTypeSymbol UnmanagedDataInterface => unmanagedDataInterface.Value;
    public INamedTypeSymbol TrackingIdInterface => trackingIdInterface.Value;
    public INamedTypeSymbol TrackedInstances => trackedInstances.Value;
    public INamedTypeSymbol LazyRef => lazyRef.Value;
    public INamedTypeSymbol LazyVal => lazyVal.Value;

    public INamedTypeSymbol SystemIDisposable => systemIDisposable.Value;
    public INamedTypeSymbol SystemFunc1 => systemFunc1.Value;

    public KnownSymbols(Compilation compilation)
    {

        EquatableIgnore<INamedTypeSymbol> Get(string metadataName)
        {
            // missing common symbols should be rare enough; in case we can't resolve these,
            // we fall back to System.Void which won't match any equality/inheritance checks
            var missingSymbolFallback = compilation.GetSpecialType(SpecialType.System_Void);
            return new(compilation.GetTypeByMetadataName(metadataName) ?? missingSymbolFallback);
        }

        unityObject = Get("UnityEngine.Object");
        unityComponent = Get("UnityEngine.Component");
        unityMonoBehaviour = Get("UnityEngine.MonoBehaviour");
        unityScriptableObject = Get("UnityEngine.ScriptableObject");

        medicineFind = Get("Medicine.Find");
        injectAttribute = Get(Constants.InjectAttributeMetadataName);
        singletonAttribute = Get(Constants.SingletonAttributeMetadataName);
        trackAttribute = Get(Constants.TrackAttributeMetadataName);
        unmanagedAccessAttribute = Get(Constants.UnmanagedAccessAttributeMetadataName);
        iInstanceIndexInterface = Get("Medicine.IInstanceIndex");
        unmanagedDataInterface = Get("Medicine.IUnmanagedData`1");
        trackingIdInterface = Get($"{Constants.TrackingIdInterfaceMetadataName}`1");
        trackedInstances = Get("Medicine.TrackedInstances`1");
        lazyRef = Get("Medicine.LazyRef`1");
        lazyVal = Get("Medicine.LazyVal`1");

        systemIDisposable = Get("System.IDisposable");
        systemFunc1 = Get("System.Func`1");
    }

    bool IEquatable<KnownSymbols>.Equals(KnownSymbols other)
        => false; // force no caching
}

static class KnownSymbolsExtensions
{
    public static IncrementalValueProvider<KnownSymbols> GetKnownSymbolsProvider(this IncrementalGeneratorInitializationContext context)
        => context.CompilationProvider.Select((compilation, _) => new KnownSymbols(compilation));
}
