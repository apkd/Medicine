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


        [MethodImpl(AggressiveInlining)]
        internal static Span<T> AsSpanUnsafe<T>(this List<T> list)
            => list.AsInternalsView().Array.AsSpan(0, list.Count);

        [MethodImpl(AggressiveInlining)]
        static unsafe void* AsPointer<T>(ref T value)
            => UnsafeUtility.AddressOf(ref UnsafeUtility.As<T, byte>(ref value));

        [MethodImpl(AggressiveInlining)]
        internal static unsafe Span<T> AsSpanUnsafe<T>(this T[]? array, int start = 0, int length = int.MinValue)
        {
            if (array is null)
                return default;
            if (length is 0)
                return default;
            if (length is int.MinValue)
                length = array.Length - start;
#if DEBUG
            if (start < 0 || (uint)start > (uint)array.Length)
                throw new ArgumentOutOfRangeException(nameof(start));
            if ((uint)length > (uint)(array.Length - start))
                throw new ArgumentOutOfRangeException(nameof(length));
#endif
            return new(AsPointer(ref array[start]), length);
        }

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
            => !ReferenceEquals(obj, null) && isNativeObjectAliveDelegate(obj);

        [MethodImpl(AggressiveInlining)]
        public static bool IsValueType<T>()
            => TypeCache<T>.IsValueType is 1;

        static class TypeCache<T>
        {
            public static readonly byte IsValueType = typeof(T).IsValueType ? (byte)1 : (byte)0;
        }
    }
}