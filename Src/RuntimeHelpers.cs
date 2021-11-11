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
    /// You probably don't need to access these methods directly.
    /// </summary>
    public static partial class RuntimeHelpers
    {
        /// <remarks> This is a helper method. You don't usually need to use it directly. See <see cref="Inject"/> to learn more. </remarks>
        [MethodImpl(AggressiveInlining)]
        public static bool ValidateArray(Array array)
            => array.Length > 0;

        /// <remarks> This is a helper method. You don't usually need to use it directly. See <see cref="Inject.Single"/> to learn more. </remarks>
        [MethodImpl(AggressiveInlining)]
        public static Camera GetMainCamera()
#if UNITY_2020_2_OR_NEWER
            // this is now fast enough that there's no point in caching (better to have implementation parity with Camera.main)
            // see: https://blogs.unity3d.com/2020/09/21/new-performance-improvements-in-unity-2020-2/
            => Camera.main;
#else
            => currentMainCamera && currentMainCamera.isActiveAndEnabled
                ? currentMainCamera
                : currentMainCamera = Camera.main;

        static Camera currentMainCamera;
#endif

        /// <remarks> This is a helper method. You don't usually need to use it directly. See <see cref="Inject"/> to learn more. </remarks>
        [MethodImpl(AggressiveInlining)]
        public static T Inject<T>(GameObject context)
            => context.GetComponent<T>();

        /// <remarks> This is a helper method. You don't usually need to use it directly. See <see cref="Inject"/> to learn more. </remarks>
        [MethodImpl(AggressiveInlining)]
        public static T[] InjectArray<T>(GameObject context)
            => context.GetComponents<T>();

        /// <remarks> This is a helper method. You don't usually need to use it directly. See <see cref="Inject.FromChildren"/> to learn more. </remarks>
        [MethodImpl(AggressiveInlining)]
        public static T InjectFromChildren<T>(GameObject context) where T : class
            => context.GetComponentInChildren(typeof(T), includeInactive: false) as T;

        /// <remarks> This is a helper method. You don't usually need to use it directly. See <see cref="Inject.FromChildren"/> to learn more. </remarks>
        [MethodImpl(AggressiveInlining)]
        public static T InjectFromChildrenIncludeInactive<T>(GameObject context) where T : class
            => context.GetComponentInChildren(typeof(T), includeInactive: true) as T;

        /// <remarks> This is a helper method. You don't usually need to use it directly. See <see cref="Inject.FromChildren"/> to learn more. </remarks>
        [MethodImpl(AggressiveInlining)]
        public static T[] InjectFromChildrenArray<T>(GameObject context)
            => context.GetComponentsInChildren<T>(includeInactive: false);

        /// <remarks> This is a helper method. You don't usually need to use it directly. See <see cref="Inject.FromChildren"/> to learn more. </remarks>
        [MethodImpl(AggressiveInlining)]
        public static T[] InjectFromChildrenArrayIncludeInactive<T>(GameObject context)
            => context.GetComponentsInChildren<T>(includeInactive: true);

#if UNITY_2020_1_OR_NEWER
        /// <remarks> This is a helper method. You don't usually need to use it directly. See <see cref="Inject.FromParents"/> to learn more. </remarks>
        [MethodImpl(AggressiveInlining)]
        public static T InjectFromParents<T>(GameObject context) where T : class
            => context.GetComponentInParent(includeInactive: false, type: typeof(T)) as T;

        /// <remarks> This is a helper method. You don't usually need to use it directly. See <see cref="Inject.FromParents"/> to learn more. </remarks>
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

        /// <remarks> This is a helper method. You don't usually need to use it directly. See <see cref="Inject.FromParents"/> to learn more. </remarks>
        [MethodImpl(AggressiveInlining)]
        public static T[] InjectFromParentsArray<T>(GameObject context)
            => context.GetComponentsInParent<T>(includeInactive: false);

        /// <remarks> This is a helper method. You don't usually need to use it directly. See <see cref="Inject.FromParents"/> to learn more. </remarks>
        [MethodImpl(AggressiveInlining)]
        public static T[] InjectFromParentsArrayIncludeInactive<T>(GameObject context)
            => context.GetComponentsInParent<T>(includeInactive: true);

        public static class Lazy
        {
            /// <remarks> This is a helper method. You don't usually need to use it directly. See <see cref="Inject.Lazy"/> to learn more. </remarks>
            [MethodImpl(AggressiveInlining)]
            public static T[] InjectArray<T>(GameObject context) where T : class
                => context.GetComponentsNonAlloc<T>();

            /// <remarks> This is a helper method. You don't usually need to use it directly. See <see cref="Inject.FromChildren.Lazy"/> to learn more. </remarks>
            [MethodImpl(AggressiveInlining)]
            public static T[] InjectFromChildrenArray<T>(GameObject context) where T : class
                => context.GetComponentsInChildrenNonAlloc<T>(includeInactive: false);

            /// <remarks> This is a helper method. You don't usually need to use it directly. See <see cref="Inject.FromChildren.Lazy"/> to learn more. </remarks>
            [MethodImpl(AggressiveInlining)]
            public static T[] InjectFromChildrenArrayIncludeInactive<T>(GameObject context) where T : class
                => context.GetComponentsInChildrenNonAlloc<T>(includeInactive: true);

            /// <remarks> This is a helper method. You don't usually need to use it directly. See <see cref="Inject.FromParents.Lazy"/> to learn more. </remarks>
            [MethodImpl(AggressiveInlining)]
            public static T[] InjectFromParentsArray<T>(GameObject context) where T : class
                => context.GetComponentsInParentNonAlloc<T>(includeInactive: false);

            /// <remarks> This is a helper method. You don't usually need to use it directly. See <see cref="Inject.FromParents.Lazy"/> to learn more. </remarks>
            [MethodImpl(AggressiveInlining)]
            public static T[] InjectFromParentsArrayIncludeInactive<T>(GameObject context) where T : class
                => context.GetComponentsInParentNonAlloc<T>(includeInactive: true);
        }

#if UNITY_EDITOR
        [UsedImplicitly]
        static Action reinitializeAction;
        
        [UsedImplicitly]
        static Action debugAction;

        /// <summary>
        /// forces preloaded assets initialization in editor (eg. to make singletons register themselves)
        /// inexplicably, Unity doesn't do this by default
        /// </summary>
        [UnityEditor.InitializeOnLoadMethod]
        static void EditorInitializeOnLoad()
            => UnityEditor.PlayerSettings.GetPreloadedAssets();

        // static RuntimeHelpers()
        //     => UnityEditor.EditorApplication.playModeStateChanged += (x) =>
        //     {
        //         if (x is UnityEditor.PlayModeStateChange.ExitingEditMode)
        //             reinitializeAction?.Invoke();
        //     };

        [UnityEditor.MenuItem("Tools/Medicine/List registered objects")]
        static void MenuCommandDebug()
            => debugAction?.Invoke();

        [UnityEditor.MenuItem("Tools/Medicine/Clear registered objects")]
        static void MenuCommandReinitialize()
            => reinitializeAction?.Invoke();
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
