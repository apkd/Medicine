#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Unity.Collections.LowLevel.Unsafe;
using static System.ComponentModel.EditorBrowsableState;
using static System.Runtime.CompilerServices.MethodImplOptions;

namespace Medicine.Internal
{
    [UsedImplicitly]
    [StructLayout(LayoutKind.Sequential)]
    sealed class ListView<T>
    {
        public T[]? Array;
        public int Count;
        public int Version;
    }

    /// <summary>
    /// Utility methods used by the Medicine library.
    /// Please do not rely on these - these are not considered part of the public API
    /// and might be changed in an update.
    /// </summary>
    [EditorBrowsable(Never)]
    public static class Utility
    {

#if UNITY_EDITOR
        // We can do better than Application.isPlaying!
        // - a simple static field, toggled by playModeStateChanged event, direct access
        // - skips the extern method call
        // - const in builds, allowing complete branch elimination
        public static bool EditMode
            = true; // seems reasonable to assume that the editor starts in edit mode...

        [UnityEditor.InitializeOnLoadMethod]
        static void Initialize()
        {
            EditMode = !UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode;
            UnityEditor.EditorApplication.playModeStateChanged
                += state => EditMode = state is UnityEditor.PlayModeStateChange.EnteredEditMode;
        }
#else
        public const bool EditMode
            = false; // always false
#endif

        // direct access to the list's backing array, version, etc.
        [MethodImpl(AggressiveInlining)]
        internal static ListView<T> AsInternalsView<T>(this List<T> list)
            => UnsafeUtility.As<List<T>, ListView<T>>(ref list);

        internal static void InvokeDispose<T>(this T disposable)
            where T : struct, IDisposable
            => disposable.Dispose();

        static readonly Func<UnityEngine.Object?, bool> isNativeObjectAliveDelegate
            = typeof(UnityEngine.Object)
                  .GetMethod("IsNativeObjectAlive", BindingFlags.NonPublic | BindingFlags.Static)
                  .CreateDelegate(typeof(Func<UnityEngine.Object?, bool>)) as Func<UnityEngine.Object?, bool>
              ?? throw new InvalidOperationException("Could not find the IsNativeObjectAlive method on UnityEngine.Object.");

        [MethodImpl(AggressiveInlining)]
        public static bool IsNativeObjectAlive(UnityEngine.Object? obj)
            => isNativeObjectAliveDelegate(obj);
    }
}