using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using UnityEngine;
using static System.Runtime.CompilerServices.MethodImplOptions;
using Obj = UnityEngine.Object;
#pragma warning disable 162

// ReSharper disable UnusedParameter.Global
// ReSharper disable MemberHidesStaticFromOuterClass
namespace Medicine
{
    /// <summary>
    /// Runtime helpers that are internally used to implement the [Inject] attribute functionality.
    /// You probably shouldn't need to access these methods directly.
    /// </summary>
    public static partial class RuntimeHelpers
    {
        static Camera currentMainCamera;

        [MethodImpl(AggressiveInlining)]
        public static bool ValidateArray(Array array)
            => array.Length > 0;

        [MethodImpl(AggressiveInlining)]
        public static Camera GetMainCamera()
            => currentMainCamera && currentMainCamera.isActiveAndEnabled
                ? currentMainCamera
                : currentMainCamera = Camera.main;

        [MethodImpl(AggressiveInlining)]
        public static T Inject<T>(GameObject context)
            => context.GetComponent<T>();

        [MethodImpl(AggressiveInlining)]
        public static T[] InjectArray<T>(GameObject context)
            => context.GetComponents<T>();

        [MethodImpl(AggressiveInlining)]
        public static T InjectFromChildren<T>(GameObject context) where T : class
            => context.GetComponentInChildren(typeof(T), includeInactive: false) as T;

        [MethodImpl(AggressiveInlining)]
        public static T InjectFromChildrenIncludeInactive<T>(GameObject context) where T : class
            => context.GetComponentInChildren(typeof(T), includeInactive: true) as  T;

        [MethodImpl(AggressiveInlining)]
        public static T[] InjectFromChildrenArray<T>(GameObject context)
            => context.GetComponentsInChildren<T>(includeInactive: false);

        [MethodImpl(AggressiveInlining)]
        public static T[] InjectFromChildrenArrayIncludeInactive<T>(GameObject context)
            => context.GetComponentsInChildren<T>(includeInactive: true);

#if UNITY_2020_1_OR_NEWER
        [MethodImpl(AggressiveInlining)]
        public static T InjectFromParents<T>(GameObject context) where T : class
            => context.GetComponentInParent(includeInactive: false, type: typeof(T)) as T;

        [MethodImpl(AggressiveInlining)]
        public static T InjectFromParentsIncludingInactive<T>(GameObject context) where T : class
            => context.GetComponentInParent(includeInactive: true, type: typeof(T)) as T;
#else
        [MethodImpl(AggressiveInlining)]
        public static T InjectFromParents<T>(GameObject context) where T : class
            => context.GetComponentInParent(type: typeof(T)) as T;

        [MethodImpl(AggressiveInlining)]
        public static T InjectFromParentsIncludingInactive<T>(GameObject context) where T : class
            => context.GetComponentInParent(type: typeof(T)) as T;
#endif

        [MethodImpl(AggressiveInlining)]
        public static T[] InjectFromParentsArray<T>(GameObject context)
            => context.GetComponentsInParent<T>(includeInactive: false);

        [MethodImpl(AggressiveInlining)]
        public static T[] InjectFromParentsArrayIncludeInactive<T>(GameObject context)
            => context.GetComponentsInParent<T>(includeInactive: true);

        public static class Lazy
        {
            [MethodImpl(AggressiveInlining)]
            public static T[] InjectArray<T>(GameObject context) where T : class
                => context.GetComponentsNonAlloc<T>();

            [MethodImpl(AggressiveInlining)]
            public static T[] InjectFromChildrenArray<T>(GameObject context) where T : class
                => context.GetComponentsInChildrenNonAlloc<T>(includeInactive: false);

            [MethodImpl(AggressiveInlining)]
            public static T[] InjectFromChildrenArrayIncludeInactive<T>(GameObject context) where T : class
                => context.GetComponentsInChildrenNonAlloc<T>(includeInactive: true);

            [MethodImpl(AggressiveInlining)]
            public static T[] InjectFromParentsArray<T>(GameObject context) where T : class
                => context.GetComponentsInParentNonAlloc<T>(includeInactive: false);

            [MethodImpl(AggressiveInlining)]
            public static T[] InjectFromParentsArrayIncludeInactive<T>(GameObject context) where T : class
                => context.GetComponentsInParentNonAlloc<T>(includeInactive: true);
        }

#if UNITY_EDITOR
        // forces preloaded assets initialization in editor
        // inexplicably, Unity doesn't do this by default
        [UnityEditor.InitializeOnLoadMethod]
        public static void EditorInitializeOnLoad()
            => UnityEditor.PlayerSettings.GetPreloadedAssets();
#endif

        /// <summary>
        /// Re-initializes the properties injected using the [Inject] attribute family.
        /// This is useful when you've made changes to the GameObject's hierarchy or removed/added
        /// components, but should be used sparingly for performance reasons.
        /// </summary>
        [UsedImplicitly, SuppressMessage("ReSharper", "SuspiciousTypeConversion.Global")]
        public static void Reinject(this MonoBehaviour monoBehaviour)
            => (monoBehaviour as IMedicineComponent)?.Inject();

#if MEDICINE_DEBUG
        internal const bool MedicineDebug = true;
#else
        internal const bool MedicineDebug = false;
#endif

#if UNITY_EDITOR
        internal static bool ApplicationIsPlaying
            => Application.isPlaying;
#else
        // constant outside the editor
        internal const bool ApplicationIsPlaying = true;
#endif
    }
}
