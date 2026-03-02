using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

public readonly record struct KnownSymbols
{
    public INamedTypeSymbol UnityObject { get; }
    public INamedTypeSymbol UnityComponent { get; }
    public INamedTypeSymbol UnityMonoBehaviour { get; }
    public INamedTypeSymbol UnityScriptableObject { get; }
    public INamedTypeSymbol MedicineFind { get; }
    public INamedTypeSymbol InjectAttribute { get; }
    public INamedTypeSymbol SingletonAttribute { get; }
    public INamedTypeSymbol TrackAttribute { get; }
    public INamedTypeSymbol UnmanagedAccessAttribute { get; }
    public INamedTypeSymbol IInstanceIndexInterface { get; }
    public INamedTypeSymbol UnmanagedDataInterface { get; }
    public INamedTypeSymbol CustomStorageInterface { get; }
    public INamedTypeSymbol TrackInstanceIDsInterface { get; }
    public INamedTypeSymbol TrackingIdInterface { get; }
    public INamedTypeSymbol TrackedInstances { get; }
    public INamedTypeSymbol LazyRef { get; }
    public INamedTypeSymbol LazyVal { get; }
    public INamedTypeSymbol SystemIDisposable { get; }
    public INamedTypeSymbol SystemFunc1 { get; }

    public KnownSymbols(Compilation compilation)
    {
        INamedTypeSymbol Get(string metadataName)
        {
            // missing common symbols should be rare enough; in case we can't resolve these,
            // we fall back to System.Void which won't match any equality/inheritance checks
            var missingSymbolFallback = compilation.GetSpecialType(SpecialType.System_Void);

            return compilation.GetTypeByMetadataName(metadataName) ?? missingSymbolFallback;
        }

        UnityObject
            = Get("UnityEngine.Object");

        UnityComponent
            = Get("UnityEngine.Component");

        UnityMonoBehaviour
            = Get("UnityEngine.MonoBehaviour");

        UnityScriptableObject
            = Get("UnityEngine.ScriptableObject");

        MedicineFind
            = Get("Medicine.Find");

        InjectAttribute
            = Get(Constants.InjectAttributeMetadataName);

        SingletonAttribute
            = Get(Constants.SingletonAttributeMetadataName);

        TrackAttribute
            = Get(Constants.TrackAttributeMetadataName);

        UnmanagedAccessAttribute
            = Get(Constants.UnmanagedAccessAttributeMetadataName);

        IInstanceIndexInterface
            = Get(Constants.IInstanceIndexInterfaceMetadataName);

        UnmanagedDataInterface
            = Get("Medicine.IUnmanagedData`1");

        CustomStorageInterface
            = Get("Medicine.ICustomStorage`1");

        TrackInstanceIDsInterface
            = Get(Constants.TrackInstanceIDsInterfaceMetadataName);

        TrackingIdInterface
            = Get($"{Constants.TrackingIdInterfaceMetadataName}`1");

        TrackedInstances
            = Get("Medicine.TrackedInstances`1");

        LazyRef
            = Get("Medicine.LazyRef`1");

        LazyVal
            = Get("Medicine.LazyVal`1");

        SystemIDisposable
            = Get("System.IDisposable");

        SystemFunc1
            = Get("System.Func`1");
    }
}

static class KnownSymbolsCache
{
    sealed class Holder(KnownSymbols value)
    {
        public readonly KnownSymbols Value = value;
    }

    static readonly ConditionalWeakTable<Compilation, Holder> knownSymbolsByCompilation = new();

    public static KnownSymbols GetKnownSymbols(this Compilation compilation)
        => knownSymbolsByCompilation.GetValue(compilation, static x => new(new(x))).Value;
}
