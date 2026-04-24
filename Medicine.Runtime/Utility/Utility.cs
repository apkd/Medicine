#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEditor;
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
        public nint Bounds;
        public long Length;
        public T Elements = default!;
    }

    /// <summary>
    /// Utility methods used by the Medicine library.
    /// Please do not rely on these - these are not considered part of the public API
    /// and might be changed in an update.
    /// </summary>
    [EditorBrowsable(Never)]
    public static partial class Utility
    {
#if UNITY_EDITOR
        // We can do better than Application.isPlaying!
        // - a simple static field, toggled by playModeStateChanged event, direct access
        // - skips the extern method call
        // - const in builds, allowing complete branch elimination
        public static bool EditMode
            = true; // seems reasonable to assume that the editor starts in edit mode...

        /// <inheritdoc cref="AssetDatabase.IsAssetImportWorkerProcess"/>
        public static bool IsAssetImportWorkerProcess;

        [UnityEditor.InitializeOnLoadMethod]
        static void Initialize()
        {
            EditMode = !UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode;
            IsAssetImportWorkerProcess = UnityEditor.AssetDatabase.IsAssetImportWorkerProcess();
            UnityEditor.EditorApplication.playModeStateChanged
                += state => EditMode = state is UnityEditor.PlayModeStateChange.EnteredEditMode;
        }
#else
        public const bool EditMode
            = false; // always false

        public const bool IsAssetImportWorkerProcess
            = false; // always false
#endif

        // direct access to the list's backing array, version, etc.
        [MethodImpl(AggressiveInlining)]
        internal static ListView<T> AsInternalsView<T>(this List<T> list)
            => UnsafeUtility.As<List<T>, ListView<T>>(ref list);

        internal static void Deconstruct<T>(this ListView<T> list, out T[]? array, out int count)
            => (array, count) = (list.Array, list.Count);

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
        public static unsafe UnsafeList<TTo> AsUnsafeList<TFrom, TTo>(TFrom[]? array)
            where TTo : unmanaged
        {
            if (array is not { Length: > 0 })
                return default;

            var arrayData = UnsafeUtility.As<TFrom[], ArrayView<TTo>>(ref array);
            return new((TTo*)UnsafeUtility.AddressOf(ref arrayData.Elements), array.Length);
        }

        [MethodImpl(AggressiveInlining)]
        public static UnsafeList<T> AsUnsafeList<T>(T[]? array)
            where T : unmanaged
            => AsUnsafeList<T, T>(array);

        [MethodImpl(AggressiveInlining)]
        public static unsafe NativeArray<TTo> AsNativeArray<TFrom, TTo>(TFrom[]? array)
            where TTo : unmanaged
        {
            if (array is not { Length: > 0 })
                return default;

            var arrayData = UnsafeUtility.As<TFrom[], ArrayView<TTo>>(ref array);
            var nativeArray = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<TTo>(
                dataPointer: UnsafeUtility.AddressOf(ref arrayData.Elements),
                length: array.Length,
                allocator: Allocator.None
            );
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref nativeArray, AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            return nativeArray;
        }

        [MethodImpl(AggressiveInlining)]
        public static NativeArray<TTo>.ReadOnly AsNativeArrayRO<TFrom, TTo>(TFrom[]? array)
            where TTo : unmanaged
        {
            var nativeArray = AsNativeArray<TFrom, TTo>(array);
            return nativeArray.AsReadOnly();
        }

        [MethodImpl(AggressiveInlining)]
        public static int GetArrayLength<T>(UnmanagedRef<T[]> arrayRef, int arrayLengthOffset)
            => arrayRef.Ptr is 0 ? 0 : (int)arrayRef.Read<long>(arrayLengthOffset);

        public static unsafe ushort GetArrayLengthOffset<T>()
        {
            var array = new T[1];
            var arrayRef = new UnmanagedRef<T[]>(array);
            var view = UnsafeUtility.As<T[], ArrayView<T>>(ref array);
            return (ushort)((nint)UnsafeUtility.AddressOf(ref view.Length) - arrayRef.Ptr);
        }

        public static unsafe ushort GetArrayDataOffset<TFrom, TTo>()
            where TTo : unmanaged
        {
            var array = new TFrom[1];
            var arrayRef = new UnmanagedRef<TFrom[]>(array);
            var view = UnsafeUtility.As<TFrom[], ArrayView<TTo>>(ref array);
            return (ushort)((nint)UnsafeUtility.AddressOf(ref view.Elements) - arrayRef.Ptr);
        }

        [MethodImpl(AggressiveInlining)]
        public static unsafe NativeArray<TTo> AsNativeArray<TFrom, TTo>(
            UnmanagedRef<TFrom[]> arrayRef,
            int length,
            int arrayDataOffset
        ) where TTo : unmanaged
        {
            if (arrayRef.Ptr is 0 || length <= 0)
                return default;

            var nativeArray = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<TTo>(
                dataPointer: (void*)(arrayRef.Ptr + arrayDataOffset),
                length: length,
                allocator: Allocator.None
            );
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref nativeArray, AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            return nativeArray;
        }

        [MethodImpl(AggressiveInlining)]
        public static NativeArray<TTo>.ReadOnly AsNativeArrayRO<TFrom, TTo>(
            UnmanagedRef<TFrom[]> arrayRef,
            int length,
            int arrayDataOffset
        ) where TTo : unmanaged
        {
            var nativeArray = AsNativeArray<TFrom, TTo>(arrayRef, length, arrayDataOffset);
            return nativeArray.AsReadOnly();
        }

        [MethodImpl(AggressiveInlining)]
        public static NativeArray<TTo> AsNativeArray<TFrom, TTo>(
            UnmanagedRef<List<TFrom>> listRef,
            int itemsOffset,
            int countOffset,
            int arrayDataOffset
        ) where TTo : unmanaged
        {
            if (listRef.Ptr is 0)
                return default;

            var arrayRef = listRef.Read<UnmanagedRef<TFrom[]>>(itemsOffset);
            return AsNativeArray<TFrom, TTo>(arrayRef, listRef.Read<int>(countOffset), arrayDataOffset);
        }

        [MethodImpl(AggressiveInlining)]
        public static unsafe NativeArray<TTo> AsNativeArray<TFrom, TTo>(List<TFrom>? list)
            where TTo : unmanaged
        {
            if (list is not { Count: > 0 })
                return default;

            var view = list.AsInternalsView();
            var array = view.Array;

            if (array is not { Length: > 0 })
                return default;

            var arrayData = UnsafeUtility.As<TFrom[], ArrayView<TTo>>(ref array);
            var nativeArray = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<TTo>(
                dataPointer: UnsafeUtility.AddressOf(ref arrayData.Elements),
                length: view.Count,
                allocator: Allocator.None
            );
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref nativeArray, AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            return nativeArray;
        }

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

        [MethodImpl(AggressiveInlining)]
        public static unsafe NativeArray<T> AsNativeArray<T>(T[]? array)
            where T : unmanaged
        {
            if (array is not { Length: > 0 })
                return default;

            var arrayData = UnsafeUtility.As<T[], ArrayView<T>>(ref array);
            var nativeArray = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(
                dataPointer: UnsafeUtility.AddressOf(ref arrayData.Elements),
                length: array.Length,
                allocator: Allocator.None
            );
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref nativeArray, AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            return nativeArray;
        }

        [MethodImpl(AggressiveInlining)]
        public static NativeArray<T>.ReadOnly AsNativeArrayRO<T>(T[]? array)
            where T : unmanaged
        {
            var nativeArray = AsNativeArray(array);
            return nativeArray.AsReadOnly();
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

        [UsedImplicitly]
        [StructLayout(LayoutKind.Sequential)]
        sealed class UnityObjectInternals
        {
            public nint m_CachedPtr;
        }

        [MethodImpl(AggressiveInlining)]
        public static bool IsNativeObjectAlive([NotNullWhen(true)] UnityEngine.Object? obj)
            => !ReferenceEquals(obj, null) && UnsafeUtility.As<UnityEngine.Object, UnityObjectInternals>(ref obj).m_CachedPtr != 0;

        [MethodImpl(AggressiveInlining)]
        public static bool IsNativeObjectDead([NotNullWhen(false)] UnityEngine.Object? obj)
            => ReferenceEquals(obj, null) || UnsafeUtility.As<UnityEngine.Object, UnityObjectInternals>(ref obj).m_CachedPtr == 0;

        [MethodImpl(AggressiveInlining)]
        public static bool IsNativeObjectAlive([NotNullWhen(true)] object? obj)
            => !ReferenceEquals(obj, null) && (obj is not UnityEngine.Object || UnsafeUtility.As<object, UnityObjectInternals>(ref obj).m_CachedPtr != 0);

        [MethodImpl(AggressiveInlining)]
        public static bool IsNativeObjectDead([NotNullWhen(false)] object? obj)
            => ReferenceEquals(obj, null) || obj is UnityEngine.Object && UnsafeUtility.As<object, UnityObjectInternals>(ref obj).m_CachedPtr == 0;

        [MethodImpl(AggressiveInlining)]
        internal static bool Has(this SingletonAttribute.Strategy strategy, SingletonAttribute.Strategy flag)
            => (strategy & flag) == flag;

        [MethodImpl(AggressiveInlining)]
        public static T[] FallbackToEmpty<T>(this T[]? array)
            => array ?? Array.Empty<T>();

        /// <summary> Sets the type of managed object. </summary>
        /// <remarks> Mono runtime hack. Use with high caution. </remarks>
        [EditorBrowsable(Never)]
        [MethodImpl(AggressiveInlining)]
        public static unsafe T? SetManagedObjectType<T>(object? obj) where T : class
        {
            if (obj is null)
                return null;

            var ptr = UnsafeUtility.As<object, IntPtr>(ref obj);
            UnsafeUtility.AsRef<ObjectHeader>((void*)ptr) = TypeHeaders<T>.Header;
            return obj as T;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ObjectHeader
        {
            readonly IntPtr data0;
            readonly IntPtr data1;
        }

        static unsafe class TypeHeaders<T>
        {
            public static readonly ObjectHeader Header = Make();

            static ObjectHeader Make()
            {
                // create a temporary instance of a managed type to read the type header
                // this is done once per type in the lifetime of the program

                var tempInstance = typeof(T).IsArray
                    // create an array of length 0
                    ? Array.CreateInstance(typeof(T).GetElementType()!, 0)
                    // create an object instance without calling the ctor
                    : System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(T));

                var ptr = UnsafeUtility.PinGCObjectAndGetAddress(tempInstance, out ulong gcHandle);
                var value = UnsafeUtility.AsRef<ObjectHeader>(ptr);
                UnsafeUtility.ReleaseGCObject(gcHandle);
                return value;
            }
        }
    }
}
