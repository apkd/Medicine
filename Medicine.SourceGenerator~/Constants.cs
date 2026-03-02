static class Constants
{
    public const string m = "ᵐ";
    public const string Namespace = "Medicine";
    public const string NamespaceWithGlobal = $"global::{Namespace}";
    public const string MedicineExtensionsDefine = "MEDICINE_EXTENSIONS_LIB";

    public const string MedicineSettingsAttributeShort = "MedicineSettings";
    public const string MedicineSettingsAttribute = $"{MedicineSettingsAttributeShort}Attribute";
    public const string MedicineSettingsAttributeFQN = $"{NamespaceWithGlobal}.{MedicineSettingsAttribute}";

    public const string InjectAttributeNameShort = "Inject";
    public const string InjectAttributeName = $"{InjectAttributeNameShort}Attribute";
    public const string InjectAttributeMetadataName = $"{Namespace}.{InjectAttributeName}";
    public const string InjectAttributeFQN = $"{NamespaceWithGlobal}.{InjectAttributeName}";

    public const string SingletonAttributeNameShort = "Singleton";
    public const string SingletonAttributeName = $"{SingletonAttributeNameShort}Attribute";
    public const string SingletonAttributeMetadataName = $"{Namespace}.{SingletonAttributeName}";
    public const string SingletonAttributeFQN = $"{NamespaceWithGlobal}.{SingletonAttributeName}";

    public const string TrackAttributeNameShort = "Track";
    public const string TrackAttributeName = $"{TrackAttributeNameShort}Attribute";
    public const string TrackAttributeMetadataName = $"{Namespace}.{TrackAttributeName}";
    public const string TrackAttributeFQN = $"{NamespaceWithGlobal}.{TrackAttributeName}";

    public const string WrapValueEnumerableAttributeNameShort = "WrapValueEnumerable";
    public const string WrapValueEnumerableAttributeName = $"{WrapValueEnumerableAttributeNameShort}Attribute";
    public const string WrapValueEnumerableAttributeMetadataName = $"{Namespace}.{WrapValueEnumerableAttributeName}";
    public const string WrapValueEnumerableAttributeFQN = $"{NamespaceWithGlobal}.{WrapValueEnumerableAttributeName}";

    public const string UnionHeaderStructAttributeName = "UnionHeaderAttribute";
    public const string UnionHeaderStructAttributeMetadataName = $"{Namespace}.{UnionHeaderStructAttributeName}";
    public const string UnionHeaderStructAttributeFQN = $"{NamespaceWithGlobal}.{UnionHeaderStructAttributeName}";

    public const string UnionStructAttributeName = "UnionAttribute";
    public const string UnionStructAttributeMetadataName = $"{Namespace}.{UnionStructAttributeName}";
    public const string UnionStructAttributeFQN = $"{NamespaceWithGlobal}.{UnionStructAttributeName}";

    public const string UnmanagedAccessAttributeNameShort = "UnmanagedAccess";
    public const string UnmanagedAccessAttributeName = $"{UnmanagedAccessAttributeNameShort}Attribute";
    public const string UnmanagedAccessAttributeMetadataName = $"{Namespace}.{UnmanagedAccessAttributeName}";
    public const string UnmanagedAccessAttributeFQN = $"{NamespaceWithGlobal}.{UnmanagedAccessAttributeName}";

    public const string GenerateConstantsAttributeNameShort = "GenerateUnityConstants";
    public const string GenerateConstantsAttributeName = $"{GenerateConstantsAttributeNameShort}Attribute";
    public const string GenerateConstantsAttributeMetadataName = $"{Namespace}.{GenerateConstantsAttributeName}";
    public const string GenerateConstantsAttributeFQN = $"{NamespaceWithGlobal}.{GenerateConstantsAttributeName}";

    public const string UnmanagedDataInterfaceName = "IUnmanagedData";
    public const string UnmanagedDataInterfaceFQN = $"{NamespaceWithGlobal}.{UnmanagedDataInterfaceName}";

    public const string CustomStorageInterfaceName = "ICustomStorage";
    public const string CustomStorageInterfaceFQN = $"{NamespaceWithGlobal}.{CustomStorageInterfaceName}";

    public const string TrackInstanceIDsInterfaceName = "ITrackInstanceIDs";
    public const string TrackInstanceIDsInterfaceMetadataName = $"{Namespace}.{TrackInstanceIDsInterfaceName}";
    public const string TrackInstanceIDsInterfaceFQN = $"{NamespaceWithGlobal}.{TrackInstanceIDsInterfaceName}";

    public const string TrackingIdInterfaceName = "IFindByID";
    public const string TrackingIdInterfaceMetadataName = $"{Namespace}.{TrackingIdInterfaceName}";
    public const string TrackingIdInterfaceFQN = $"{NamespaceWithGlobal}.{TrackingIdInterfaceName}";

    public const string FindByAssetIdInterfaceName = "IFindByAssetID";
    public const string FindByAssetIdInterfaceFQN = $"{NamespaceWithGlobal}.{FindByAssetIdInterfaceName}";
    public const string AssetIdTypeFQN = "global::Unity.Mathematics.uint4";

    public const string IInstanceIndexInterfaceName = "IInstanceIndex";
    public const string IInstanceIndexInterfaceMetadataName = $"{Namespace}.{IInstanceIndexInterfaceName}";
    public const string IInstanceIndexInterfaceFQN = $"{NamespaceWithGlobal}.{IInstanceIndexInterfaceName}";

    public const string IInstanceIndexInternalInterfaceName = "IInstanceIndex";
    public const string IInstanceIndexInternalInterfaceMetadataName = $"{Namespace}.Internal.{IInstanceIndexInternalInterfaceName}";
    public const string IInstanceIndexInternalInterfaceFQN = $"{NamespaceWithGlobal}.Internal.{IInstanceIndexInternalInterfaceName}";

    public const string ITrackedInternalInterfaceName = "ITracked";
    public const string ITrackedInternalInterfaceMetadataName = $"{Namespace}.Internal.{ITrackedInternalInterfaceName}";
    public const string ITrackedInternalInterfaceFQN = $"{NamespaceWithGlobal}.Internal.{ITrackedInternalInterfaceName}";

    public const string ITrackedTransformAccessArrayInternalInterfaceName = "ITrackedTransformAccessArray";
    public const string ITrackedTransformAccessArrayInternalInterfaceMetadataName = $"{Namespace}.Internal.{ITrackedTransformAccessArrayInternalInterfaceName}";
    public const string ITrackedTransformAccessArrayInternalInterfaceFQN = $"{NamespaceWithGlobal}.Internal.{ITrackedTransformAccessArrayInternalInterfaceName}";

    public const string ITrackedUnmanagedDataInternalInterfaceName = "ITrackedUnmanagedData";
    public const string ITrackedUnmanagedDataInternalInterfaceMetadataName = $"{Namespace}.Internal.{ITrackedUnmanagedDataInternalInterfaceName}";
    public const string ITrackedUnmanagedDataInternalInterfaceFQN = $"{NamespaceWithGlobal}.Internal.{ITrackedUnmanagedDataInternalInterfaceName}";

    public const string MedicineFindClassFQN = "global::Medicine.Find";

    public static class Alias
    {
        // cannot alias to any shortened name because Rider fails to recognize the attribute
        public const string Hidden = "[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]";
        public const string ObsoleteInternal = "[System.Obsolete(\"This is an internal generated Medicine API and should not be used directly.\")]";
        public const string Inline = $"[{m}Inline(256)]";
        public const string NoInline = $"[{m}Inline(8)]";
        public const string EditorInit = "[global::UnityEditor.InitializeOnLoadMethodAttribute]";
        public const string RuntimeInit = "[global::UnityEngine.RuntimeInitializeOnLoadMethod(global::UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]";

        public const string UsingInline = $"using {m}Inline = global::System.Runtime.CompilerServices.MethodImplAttribute;";
        public const string UsingUtility = $"using {m}Utility = global::Medicine.Internal.Utility;";
        public const string UsingStorage = $"using {m}Storage = global::Medicine.Internal.Storage;";
        public const string UsingFind = $"using {m}Find = {MedicineFindClassFQN};";
        public const string UsingDebug = $"using {m}Debug = global::UnityEngine.Debug;";
        public const string UsingDeclaredAt = $"using {m}DeclaredAt = global::Medicine.Internal.DeclaredAtAttribute;";
        public const string UsingNonSerialized = $"using {m}NS = global::System.NonSerializedAttribute;";
        public const string UsingBindingFlags = $"using {m}BF = global::System.Reflection.BindingFlags;";
        public const string UsingUnsafeUtility = $"using {m}UU = global::Unity.Collections.LowLevel.Unsafe.UnsafeUtility;";
    }
}
