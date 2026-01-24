#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using static System.ComponentModel.EditorBrowsableState;
using static System.Runtime.CompilerServices.MethodImplOptions;

namespace Medicine.Internal
{
    [UsedImplicitly]
    sealed class ListView<T>
    {
        public T[]? Array;
        public int Count;
        public int Version;
    }

    [UsedImplicitly, StructLayout(LayoutKind.Sequential)]
    sealed class ArrayView<T>
    {
        public long Length;
        public long Bounds;
        public T Elements = default!;
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
            => list.AsInternalsView().Array.AsSpanUnsafe(0, list.Count);

        [MethodImpl(AggressiveInlining)]
        internal static unsafe UnsafeList<TTo> AsUnsafeList<TFrom, TTo>(this List<TFrom> list)
            where TTo : unmanaged
        {
            var array = list.AsInternalsView().Array;

            if (ReferenceEquals(array, null))
                return default;

            var arrayData = UnsafeUtility.As<TFrom[], ArrayView<TTo>>(ref array);
            return new((TTo*)UnsafeUtility.AddressOf(ref arrayData.Elements), list.Count);
        }

        [MethodImpl(AggressiveInlining)]
        internal static UnsafeList<T> AsUnsafeList<T>(this List<T> list)
            where T : unmanaged
            => list.AsUnsafeList<T, T>();

        [MethodImpl(AggressiveInlining)]
        internal static Span<T> AsSpanUnsafe<T>(this T[]? array, int start = 0, int length = int.MinValue)
        {
            if (array is not { Length: > 0 })
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

            ref var first = ref UnsafeUtility.As<T[], ArrayView<T>>(ref array).Elements;
            return MemoryMarshal.CreateSpan(ref first, array.Length)[start..(start + length)];
        }

        public static ushort GetFieldOffset(Type type, string fieldName, BindingFlags flags)
        {
            FieldInfo GetFieldInHierarchy()
            {
                for (var t = type; t != null; t = t.BaseType)
                    if (t.GetField(fieldName, flags) is { } result)
                        return result;

                throw new ArgumentException($"Field '{fieldName}' not found in type '{type.FullName}'.");
            }

            return (ushort)UnsafeUtility.GetFieldOffset(GetFieldInHierarchy());
        }

        internal static void InvokeDispose<T>(this T disposable)
            where T : struct, IDisposable
            => disposable.Dispose();

        [MethodImpl(AggressiveInlining)]
        public static unsafe bool IsNativeObjectAlive([NotNullWhen(true)] UnityEngine.Object? obj)
        {
            nint ptr = UnsafeUtility.As<UnityEngine.Object?, nint>(ref obj);
            nint nativePtr = ptr != 0
                ? *(nint*)(ptr + sizeof(ulong) * 2)
                : 0;

            return nativePtr is not 0;
        }

        public static class TypeInfo<T>
        {
            public static bool IsValueType => (Flags & TypeFlags.IsValueType) != 0;

            // public static bool IsUnityEngineObject => (Flags & TypeFlags.IsUnityEngineObject) != 0;
            public static bool IsScriptableObject => (Flags & TypeFlags.IsScriptableObject) != 0;
            // public static bool IsMonoBehaviour => (Flags & TypeFlags.IsMonoBehaviour) != 0;

            public static readonly TypeFlags Flags
                = 0
                  | (typeof(T).IsValueType ? TypeFlags.IsValueType : 0)
                  // | (typeof(UnityEngine.Object).IsAssignableFrom(typeof(T)) ? TypeFlags.IsUnityEngineObject : 0)
                  | (typeof(ScriptableObject).IsAssignableFrom(typeof(T)) ? TypeFlags.IsScriptableObject : 0);
                  // | (typeof(MonoBehaviour).IsAssignableFrom(typeof(T)) ? TypeFlags.IsMonoBehaviour : 0);
        }

        [Flags]
        public enum TypeFlags : ushort
        {
            IsValueType = 1 << 0,
            IsUnityEngineObject = 1 << 1,
            IsScriptableObject = 1 << 2,
            IsMonoBehaviour = 1 << 3,
        }
    }
}