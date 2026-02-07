using Microsoft.CodeAnalysis;

readonly record struct KnownSymbols
{
    readonly EquatableIgnore<INamedTypeSymbol> unityObject;
    readonly EquatableIgnore<INamedTypeSymbol> unityComponent;
    readonly EquatableIgnore<INamedTypeSymbol> unityMonoBehaviour;
    readonly EquatableIgnore<INamedTypeSymbol> unityScriptableObject;

    readonly EquatableIgnore<INamedTypeSymbol> medicineFind;
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
            => new(compilation.GetTypeByMetadataName(metadataName)
                   ?? throw new($"Common type not found: '{metadataName}'"));

        unityObject = Get("UnityEngine.Object");
        unityComponent = Get("UnityEngine.Component");
        unityMonoBehaviour = Get("UnityEngine.MonoBehaviour");
        unityScriptableObject = Get("UnityEngine.ScriptableObject");

        medicineFind = Get("Medicine.Find");
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
}

static class KnownSymbolsExtensions
{
    public static IncrementalValueProvider<KnownSymbols> GetKnownSymbolsProvider(this IncrementalGeneratorInitializationContext context)
        => context.CompilationProvider.Select((compilation, _) => new KnownSymbols(compilation));
}
