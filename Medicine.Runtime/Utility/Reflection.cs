#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using UnityEngine;
using static System.Runtime.CompilerServices.MethodImplOptions;
using Object = UnityEngine.Object;

namespace Medicine.Internal
{
    public static partial class Utility
    {
        [MethodImpl(AggressiveInlining)]
        static void Set(this ref TypeFlags flags, TypeFlags flag, bool value)
            => flags = value ? flags | flag : flags & ~flag;

        [MethodImpl(AggressiveInlining)]
        static bool Has(this TypeFlags flags, TypeFlags flag)
            => (flags & flag) != 0;

        [SuppressMessage("ReSharper", "UnusedTypeParameter")]
        public static class BakedTypeInfo<T>
        {
            public static TypeFlags Flags;
        }

        public static class TypeInfo<T>
        {
            public static readonly TypeFlags Flags = InitializeTypeFlags();

            static TypeFlags InitializeTypeFlags()
            {
                if (BakedTypeInfo<T>.Flags is not 0)
                {
#if MEDICINE_DEBUG
                    var a = BakedTypeInfo<T>.Flags;
                    var b = GetFromReflection();
                    if (a != b)
                        Debug.LogError($"Cached type info mismatch: {typeof(T).Name}\nCached: {a}\nReflection: {b}");
#endif
                    return BakedTypeInfo<T>.Flags;
                }

                static TypeFlags GetFromReflection()
                {
                    var type = typeof(T);

                    if (type.IsValueType)
                        return TypeFlags.IsValueType;

                    var flags = TypeFlags.IsReferenceType;

                    if (type.IsInterface)
                        return flags | TypeFlags.IsInterface;

                    if (type.IsAbstract)
                        flags |= TypeFlags.IsAbstract;

                    if (!typeof(Object).IsAssignableFrom(type))
                        return flags;

                    flags |= TypeFlags.IsUnityEngineObject;

                    if (typeof(MonoBehaviour).IsAssignableFrom(type))
                        return flags | TypeFlags.IsComponent | TypeFlags.IsMonoBehaviour;

                    if (typeof(Component).IsAssignableFrom(type))
                        return flags | TypeFlags.IsComponent;

                    if (typeof(ScriptableObject).IsAssignableFrom(type))
                        flags |= TypeFlags.IsScriptableObject;

                    return flags;
                }


#if MEDICINE_DEBUG
                Debug.Log($"Initializing from reflection: {typeof(T).Name}");
#endif

                return GetFromReflection();
            }

            public static bool IsValueType => (Flags & TypeFlags.IsValueType) != 0;
            public static bool IsReferenceType => (Flags & TypeFlags.IsReferenceType) != 0;
            public static bool IsComponent => (Flags & TypeFlags.IsComponent) != 0;
            public static bool IsUnityEngineObject => (Flags & TypeFlags.IsUnityEngineObject) != 0;
            public static bool IsScriptableObject => (Flags & TypeFlags.IsScriptableObject) != 0;
            public static bool IsMonoBehaviour => (Flags & TypeFlags.IsMonoBehaviour) != 0;
            public static bool IsInterface => (Flags & TypeFlags.IsInterface) != 0;
            public static bool IsAbstract => (Flags & TypeFlags.IsAbstract) != 0;
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void InitCommonUnityTypes()
        {
            [MethodImpl(AggressiveInlining)]
            static void InitVT<T>() where T : struct
                => BakedTypeInfo<T>.Flags = TypeFlags.IsValueType;

            [MethodImpl(AggressiveInlining)]
            static void InitRT<T>(bool isAbstract = false) where T : class
                => BakedTypeInfo<T>.Flags = TypeFlags.IsReferenceType | (isAbstract ? TypeFlags.IsAbstract : 0);

            [MethodImpl(AggressiveInlining)]
            static void InitUnityObject<T>(bool isAbstract = false) where T : Object
                => BakedTypeInfo<T>.Flags = TypeFlags.IsReferenceType | TypeFlags.IsUnityEngineObject | (isAbstract ? TypeFlags.IsAbstract : 0);

            [MethodImpl(AggressiveInlining)]
            static void InitComponent<T>(bool isAbstract = false) where T : Component
                => BakedTypeInfo<T>.Flags = TypeFlags.IsReferenceType | TypeFlags.IsUnityEngineObject | TypeFlags.IsComponent | (isAbstract ? TypeFlags.IsAbstract : 0);

            [MethodImpl(AggressiveInlining)]
            static void InitMonoBehaviour<T>(bool isAbstract = false) where T : Component
                => BakedTypeInfo<T>.Flags = TypeFlags.IsReferenceType | TypeFlags.IsUnityEngineObject | TypeFlags.IsComponent | TypeFlags.IsMonoBehaviour | (isAbstract ? TypeFlags.IsAbstract : 0);

            [MethodImpl(AggressiveInlining)]
            static void InitScriptableObject<T>(bool isAbstract = false) where T : ScriptableObject
                => BakedTypeInfo<T>.Flags = TypeFlags.IsReferenceType | TypeFlags.IsUnityEngineObject | TypeFlags.IsScriptableObject | (isAbstract ? TypeFlags.IsAbstract : 0);

            InitRT<object>();
            InitRT<string>();
            InitRT<Array>(isAbstract: true);
            InitRT<Enum>(isAbstract: true);
            InitRT<Type>(isAbstract: true);
            InitVT<byte>();
            InitVT<sbyte>();
            InitVT<short>();
            InitVT<ushort>();
            InitVT<int>();
            InitVT<uint>();
            InitVT<long>();
            InitVT<ulong>();
            InitVT<float>();
            InitVT<double>();
            InitVT<decimal>();
            InitVT<char>();
            InitVT<bool>();

            InitVT<DateTime>();
            InitVT<TimeSpan>();
            InitVT<Vector2>();
            InitVT<Vector3>();
            InitVT<Vector4>();
            InitVT<Vector2Int>();
            InitVT<Vector3Int>();
            InitVT<Quaternion>();
            InitVT<Matrix4x4>();
            InitVT<Color>();
            InitVT<Color32>();
            InitVT<Rect>();
            InitVT<RectInt>();
            InitVT<Bounds>();
            InitVT<BoundsInt>();
            InitVT<LayerMask>();
            InitVT<Ray>();
            InitVT<Plane>();
            InitVT<Pose>();
            InitVT<Hash128>();
            InitRT<AnimationCurve>();
            InitRT<Gradient>();

            InitUnityObject<GameObject>();
            InitComponent<Transform>();
            InitComponent<RectTransform>();
            InitComponent<Camera>();
            InitComponent<Light>();
            InitComponent<Skybox>();
            InitComponent<FlareLayer>();
            InitComponent<LensFlare>();
            InitComponent<Projector>();
            InitComponent<Renderer>();
            InitComponent<MeshRenderer>();
            InitComponent<SkinnedMeshRenderer>();
            InitComponent<SpriteRenderer>();
            InitComponent<LineRenderer>();
            InitComponent<TrailRenderer>();
            InitComponent<BillboardRenderer>();
            InitComponent<MeshFilter>();
            InitComponent<TextMesh>();
            InitComponent<LODGroup>();
            InitComponent<ReflectionProbe>();
            InitComponent<LightProbeGroup>();
            InitComponent<LightProbeProxyVolume>();

            InitUnityObject<Texture>();
            InitUnityObject<Texture2D>();
            InitUnityObject<Texture2DArray>();
            InitUnityObject<Texture3D>();
            InitUnityObject<Cubemap>();
            InitUnityObject<CubemapArray>();
            InitUnityObject<RenderTexture>();
            InitUnityObject<CustomRenderTexture>();
            InitUnityObject<SparseTexture>();

            InitUnityObject<Sprite>();
            InitUnityObject<UnityEngine.U2D.SpriteAtlas>();

            InitUnityObject<Mesh>();
            InitUnityObject<Material>();

            InitUnityObject<Shader>();
            InitUnityObject<ComputeShader>();

            InitUnityObject<Font>();
            InitUnityObject<TextAsset>();
            InitUnityObject<Flare>();
            InitUnityObject<BillboardAsset>();
            InitComponent<GridLayout>();
            InitComponent<Grid>();

#if HAS_UNITY_ANIMATIONMODULE
            InitComponent<Animator>();
            InitComponent<Animation>();
            InitUnityObject<Motion>();
            InitUnityObject<AnimationClip>();
            InitUnityObject<Avatar>();
            InitUnityObject<AvatarMask>();
            InitUnityObject<RuntimeAnimatorController>();
            InitUnityObject<AnimatorOverrideController>();
            InitScriptableObject<StateMachineBehaviour>(isAbstract: true);
#endif

#if HAS_UNITY_DIRECTORMODULE
            InitComponent<UnityEngine.Playables.PlayableDirector>();
#endif

#if HAS_UNITY_AUDIOMODULE
            InitComponent<AudioListener>();
            InitComponent<AudioSource>();
            InitComponent<AudioReverbZone>();
            InitComponent<AudioChorusFilter>();
            InitComponent<AudioDistortionFilter>();
            InitComponent<AudioEchoFilter>();
            InitComponent<AudioHighPassFilter>();
            InitComponent<AudioLowPassFilter>();
            InitComponent<AudioReverbFilter>();
            InitUnityObject<AudioClip>();
            InitUnityObject<UnityEngine.Audio.AudioMixer>();
            InitUnityObject<UnityEngine.Audio.AudioMixerGroup>();
            InitUnityObject<UnityEngine.Audio.AudioMixerSnapshot>();
#endif

#if HAS_UNITY_PARTICLESYSTEMMODULE
            InitComponent<ParticleSystem>();
            InitComponent<ParticleSystemRenderer>();
#endif

#if HAS_UNITY_WINDMODULE
            InitComponent<WindZone>();
#endif

#if HAS_UNITY_CLOTHMODULE
            InitComponent<Cloth>();
#endif

#if HAS_UNITY_PHYSICSMODULE
            InitComponent<Rigidbody>();
            InitComponent<Collider>();
            InitComponent<BoxCollider>();
            InitComponent<SphereCollider>();
            InitComponent<CapsuleCollider>();
            InitComponent<MeshCollider>();
            InitComponent<CharacterController>();
            InitComponent<Joint>();
            InitComponent<FixedJoint>();
            InitComponent<HingeJoint>();
            InitComponent<SpringJoint>();
            InitComponent<CharacterJoint>();
            InitComponent<ConfigurableJoint>();
            InitComponent<ConstantForce>();
#if UNITY_6000_0_OR_NEWER || UNITY_2023_3_OR_NEWER
            InitUnityObject<PhysicsMaterial>();
#else
            InitUnityObject<PhysicMaterial>();
#endif

            InitVT<RaycastHit>();
            InitVT<ContactPoint>();
#endif

#if HAS_UNITY_VEHICLESMODULE
            InitComponent<WheelCollider>();
#endif

#if HAS_UNITY_TERRAINPHYSICSMODULE
            InitComponent<TerrainCollider>();
#endif

#if HAS_UNITY_PHYSICS2DMODULE
            InitComponent<Rigidbody2D>();
            InitComponent<Collider2D>();
            InitComponent<BoxCollider2D>();
            InitComponent<CircleCollider2D>();
            InitComponent<CapsuleCollider2D>();
            InitComponent<PolygonCollider2D>();
            InitComponent<EdgeCollider2D>();
            InitComponent<CompositeCollider2D>();
            InitComponent<Joint2D>();
            InitComponent<FixedJoint2D>();
            InitComponent<HingeJoint2D>();
            InitComponent<SpringJoint2D>();
            InitComponent<DistanceJoint2D>();
            InitComponent<SliderJoint2D>();
            InitComponent<RelativeJoint2D>();
            InitComponent<WheelJoint2D>();
            InitComponent<TargetJoint2D>();
            InitComponent<Effector2D>();
            InitComponent<AreaEffector2D>();
            InitComponent<BuoyancyEffector2D>();
            InitComponent<PlatformEffector2D>();
            InitComponent<PointEffector2D>();
            InitComponent<SurfaceEffector2D>();
            InitComponent<ConstantForce2D>();
            InitUnityObject<PhysicsMaterial2D>();
            InitVT<RaycastHit2D>();
            InitVT<ContactPoint2D>();
#endif

#if HAS_UNITY_TERRAINMODULE
            InitComponent<Terrain>();
            InitUnityObject<TerrainData>();
            InitUnityObject<TerrainLayer>();
            InitComponent<Tree>();
#endif

#if HAS_UNITY_TILEMAPMODULE
            InitScriptableObject<UnityEngine.Tilemaps.TileBase>(isAbstract: true);
            InitComponent<UnityEngine.Tilemaps.Tilemap>();
            InitComponent<UnityEngine.Tilemaps.TilemapRenderer>();
#if HAS_UNITY_PHYSICS2DMODULE
            InitComponent<UnityEngine.Tilemaps.TilemapCollider2D>();
#endif
#endif

#if HAS_UNITY_AIMODULE
            InitComponent<UnityEngine.AI.NavMeshAgent>();
            InitComponent<UnityEngine.AI.NavMeshObstacle>();
            InitUnityObject<UnityEngine.AI.NavMeshData>();
            InitRT<UnityEngine.AI.NavMeshPath>();
            InitVT<UnityEngine.AI.NavMeshHit>();
#endif

#if HAS_UNITY_VIDEOMODULE
            InitComponent<UnityEngine.Video.VideoPlayer>();
#endif

#if HAS_UNITY_UIMODULE
            InitComponent<Canvas>();
            InitComponent<CanvasRenderer>();
            InitComponent<CanvasGroup>();
            InitMonoBehaviour<UnityEngine.EventSystems.UIBehaviour>(isAbstract: true);
            InitMonoBehaviour<UnityEngine.UI.GraphicRaycaster>();
            InitMonoBehaviour<UnityEngine.UI.Graphic>(isAbstract: true);
            InitMonoBehaviour<UnityEngine.UI.MaskableGraphic>(isAbstract: true);
            InitMonoBehaviour<UnityEngine.UI.Image>();
            InitMonoBehaviour<UnityEngine.UI.RawImage>();
            InitMonoBehaviour<UnityEngine.UI.Text>();
            InitMonoBehaviour<UnityEngine.UI.Selectable>();
            InitMonoBehaviour<UnityEngine.UI.Button>();
            InitMonoBehaviour<UnityEngine.UI.Toggle>();
            InitMonoBehaviour<UnityEngine.UI.Slider>();
            InitMonoBehaviour<UnityEngine.UI.Scrollbar>();
            InitMonoBehaviour<UnityEngine.UI.Dropdown>();
            InitMonoBehaviour<UnityEngine.UI.InputField>();
            InitMonoBehaviour<UnityEngine.UI.ScrollRect>();
            InitMonoBehaviour<UnityEngine.UI.Mask>();
            InitMonoBehaviour<UnityEngine.UI.RectMask2D>();
            InitMonoBehaviour<UnityEngine.UI.CanvasScaler>();
            InitMonoBehaviour<UnityEngine.UI.ContentSizeFitter>();
            InitMonoBehaviour<UnityEngine.UI.AspectRatioFitter>();
            InitMonoBehaviour<UnityEngine.UI.LayoutElement>();
            InitMonoBehaviour<UnityEngine.UI.LayoutGroup>(isAbstract: true);
            InitMonoBehaviour<UnityEngine.UI.HorizontalLayoutGroup>();
            InitMonoBehaviour<UnityEngine.UI.VerticalLayoutGroup>();
            InitMonoBehaviour<UnityEngine.UI.GridLayoutGroup>();
            InitMonoBehaviour<UnityEngine.UI.Outline>();
            InitMonoBehaviour<UnityEngine.UI.Shadow>();
            InitMonoBehaviour<UnityEngine.UI.PositionAsUV1>();
#if HAS_UNITY_PHYSICSMODULE
            InitMonoBehaviour<UnityEngine.EventSystems.PhysicsRaycaster>();
#endif
#if HAS_UNITY_PHYSICS2DMODULE
            InitMonoBehaviour<UnityEngine.EventSystems.Physics2DRaycaster>();
#endif
#endif

#if HAS_UNITY_VISUALEFFECTGRAPH
            InitComponent<UnityEngine.VFX.VisualEffect>();
            InitUnityObject<UnityEngine.VFX.VisualEffectObject>(isAbstract: true);
            InitUnityObject<UnityEngine.VFX.VisualEffectAsset>();
#endif

            InitScriptableObject<UnityEngine.Rendering.RenderPipelineAsset>(isAbstract: true);
#if UNITY_2021_3_OR_NEWER
            InitScriptableObject<UnityEngine.Rendering.RenderPipelineGlobalSettings>(isAbstract: true);
#endif
        }

        [Flags]
        public enum TypeFlags : ushort
        {
            IsValueType = 1 << 0,
            IsReferenceType = 1 << 1,
            IsInterface = 1 << 2,
            IsAbstract = 1 << 3,
            IsUnityEngineObject = 1 << 4,
            IsComponent = 1 << 5,
            IsMonoBehaviour = 1 << 6,
            IsScriptableObject = 1 << 7,
        }
    }
}