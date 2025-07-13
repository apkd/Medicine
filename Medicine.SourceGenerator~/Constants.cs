using System.Runtime.CompilerServices;

static class Constants
{
    public const string m = "ᵐ";
    public const string Namespace = "Medicine";
    public const string NamespaceWithGlobal = $"global::{Namespace}";

    public const string InjectAttributeNameShort = "Inject";
    public const string InjectAttributeName = $"{InjectAttributeNameShort}Attribute";
    public const string InjectAttributeMetadataName = $"{Namespace}.{InjectAttributeName}";
    public const string InjectAttributeFQN = $"{NamespaceWithGlobal}.{InjectAttributeName}";

    public const string SingletonAttributeName = "SingletonAttribute";
    public const string SingletonAttributeFQN = $"{NamespaceWithGlobal}.{SingletonAttributeName}";

    public const string TrackAttributeName = "TrackAttribute";
    public const string TrackAttributeFQN = $"{NamespaceWithGlobal}.{TrackAttributeName}";

    public const string UnmanagedDataInterfaceName = "IUnmanagedData";
    public const string UnmanagedDataInterfaceFQN = $"{NamespaceWithGlobal}.{UnmanagedDataInterfaceName}";

    public const string IInstanceIndexInterfaceName = "IInstanceIndex";
    public const string IInstanceIndexInterfaceFQN = $"{NamespaceWithGlobal}.{IInstanceIndexInterfaceName}";

    public const string WrapValueEnumerableAttributeName = "WrapValueEnumerableAttribute";
    public const string WrapValueEnumerableAttributeMetadataName = $"{Namespace}.{WrapValueEnumerableAttributeName}";
    public const string WrapValueEnumerableAttributeFQN = $"{NamespaceWithGlobal}.{WrapValueEnumerableAttributeName}";

    public static class Alias
    {
        // cannot alias to any shortened name because Rider fails to recognize the attribute
        public const string Hidden = "[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]";
        public const string ObsoleteInternal = "[System.Obsolete(\"This is an internal generated Medicine API and should not be used directly.\")]";
        public const string Inline = $"[{m}Inline(256)]";
        public const string NoInline = $"[{m}Inline(8)]";

        public const string UsingInline = $"using {m}Inline = global::System.Runtime.CompilerServices.MethodImplAttribute;";
        public const string UsingUtility = $"using {m}Utility = global::Medicine.Internal.Utility;";
        public const string UsingStorage = $"using {m}Storage = global::Medicine.Internal.Storage;";
        public const string UsingFind = $"using {m}Find = global::Medicine.Find;";
        public const string UsingDebug = $"using {m}Debug = global::UnityEngine.Debug;";
    }
}