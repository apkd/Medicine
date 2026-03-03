using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Medicine.Internal;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Assertions;
using static System.ComponentModel.EditorBrowsableState;
using static System.Runtime.CompilerServices.MethodImplOptions;

// ReSharper disable UnusedMember.Global

namespace Medicine
{
#if MODULE_ZLINQ
    using ZLinq;
    using ZLinq.Linq;
    using MedicineUnsafeShim = Unsafe;
#endif

    /// <summary>
    /// Enumerable struct allowing for efficient enumeration of a tracked type's enabled instances.
    /// </summary>
    /// <remarks>
    /// You probably want to use <see cref="Find.Instances{T}"/> instead of working with this struct directly.
    /// </remarks>
    public readonly struct TrackedInstances<T> : ILinqFallbackEnumerable<TrackedInstances<T>.Enumerator, T>
        where T : class
    {
        /// <summary>
        /// Returns the number of instances currently being tracked.
        /// </summary>
        public int Count
        {
            [MethodImpl(AggressiveInlining)]
            get
            {
#if UNITY_EDITOR
                if (Utility.EditMode)
                    Storage.Instances<T>.EditMode.Refresh();
#endif

                return Storage.Instances<T>.List.Count;
            }
        }

        [EditorBrowsable(Never)]
        [MethodImpl(AggressiveInlining)]
        public Enumerator GetEnumerator()
        {
#if UNITY_EDITOR
            if (Utility.EditMode)
                Storage.Instances<T>.EditMode.Refresh();
#endif

            return new(Storage.Instances<T>.List);
        }

        /// <summary>
        /// Gives random access to the tracked instances (by storage index).
        /// Prefer using <c>foreach</c> over this indexer, as it is more efficient.
        /// </summary>
        public T this[int index]
        {
            // todo: generic static access to Storage.Instances<T> is expensive
            // maybe cache it in the TrackedInstances struct?
            [MethodImpl(AggressiveInlining)]
            get
            {
#if DEBUG
                return Storage.Instances<T>.List[index];
#else
                return Storage.Instances<T>.Array[index];
#endif
            }
        }

#if MODULE_ZLINQ
        /// <summary>
        /// Returns a ValueEnumerable struct that allows chaining of ZLinq operators.
        /// </summary>
        [MethodImpl(AggressiveInlining)]
        public ValueEnumerable<FromList<T>, T> AsValueEnumerable()
        {
#if UNITY_EDITOR
            if (Utility.EditMode)
                Storage.Instances<T>.EditMode.Refresh();
#endif
            return new(new(Storage.Instances<T>.List));
        }
#endif

        /// <summary>
        /// Copies the tracked instances to a pooled list.
        /// <br/><br/>
        /// Remember to call <c>Dispose()</c> <b>exactly once</b> to return the list to the pool.
        /// </summary>
        [MustDisposeResource]
        [MethodImpl(AggressiveInlining)]
        public PooledList<T> ToPooledList(out List<T> list)
        {
            var handle = PooledList.Get(out list);
            list.AddRange(Storage.Instances<T>.List);
            return handle;
        }

        /// <inheritdoc cref="ToPooledList(out List{T})"/>
        [MustDisposeResource]
        [MethodImpl(AggressiveInlining)]
        public PooledList<T> ToPooledList()
            => ToPooledList(out _);

        public void CopyTo(List<T> destination, int extraCapacity = 16)
        {
            int count = Count;
            if (count is 0)
                return;

            if (destination.Capacity < count)
                destination.Capacity = count + extraCapacity;

            var destinationListView = destination.AsInternalsView();
            int previousCount = destinationListView.Count;
            destinationListView.Count = count;
            var destinationArray = destinationListView.Array!;

            Storage.Instances<T>.List.CopyTo(
                array: destinationArray,
                arrayIndex: 0
            );

            if (previousCount > count)
            {
                Array.Clear(
                    array: destinationArray,
                    index: count,
                    length: previousCount - count
                );
            }
        }

        /// <summary>
        /// Allows direct access to the underlying tracked instance storage.
        /// </summary>
        /// <remarks><inheritdoc cref="UnsafeAPI"/></remarks>
        [EditorBrowsable(Advanced)]
        public UnsafeAPI Unsafe
        {
            [MethodImpl(AggressiveInlining)]
            get => default;
        }

        /// <remarks>
        /// Use with caution; do not modify the tracked instance list, and keep in mind that
        /// enabling/disabling instances will mutate the storage.
        /// </remarks>
        [EditorBrowsable(Never)]
        public struct UnsafeAPI
        {
            /// <summary>
            /// Directly returns the actual list of tracked instances.
            /// </summary>
            /// <remarks><inheritdoc cref="UnsafeAPI"/></remarks>
            [MethodImpl(AggressiveInlining)]
            public List<T> AsList()
                => Storage.Instances<T>.List;

            /// <summary>
            /// Returns a span view over the tracked instance list.
            /// </summary>
            /// <remarks><inheritdoc cref="UnsafeAPI"/></remarks>
            [MethodImpl(AggressiveInlining)]
            public ReadOnlySpan<T> AsSpan()
                => Storage.Instances<T>.AsSpan();

            /// <summary>
            /// Returns the underlying array of tracked instance storage.
            /// Not all array slots will contain valid references - the tail of the array will be filled with <c>null</c>.
            /// </summary>
            /// <remarks><inheritdoc cref="UnsafeAPI"/></remarks>
            [MethodImpl(AggressiveInlining)]
            public T[] AsArray()
                => Storage.Instances<T>.Array;

            /// <summary>
            /// Returns an UnsafeList view over the tracked instance list.
            /// Uses <see cref="UnmanagedRef{TClass}"/> to wrap instance references for compatibility with unmanaged code.
            /// </summary>
            /// <remarks><inheritdoc cref="UnsafeAPI"/></remarks>
            [MethodImpl(AggressiveInlining)]
            public UnsafeList<UnmanagedRef<T>> AsUnsafeList()
                => Storage.Instances<T>.AsUnmanaged();
        }

        /// <summary>
        /// Allows direct access to the underlying <see cref="IUnmanagedData{TData}"/> storage.
        /// </summary>
        /// <remarks>
        /// Use with caution; keep in mind that enabling/disabling instances will mutate the storage.
        /// </remarks>
        [EditorBrowsable(Advanced)]
        [MethodImpl(AggressiveInlining)]
        public UnmanagedDataAPI<TData> UnmanagedData<TData>() where TData : unmanaged
            => default;

        [EditorBrowsable(Never)] // ReSharper disable once UnusedTypeParameter
        public struct UnmanagedDataAPI<TData> where TData : unmanaged
        {
            /// <summary>
            /// Directly returns the underlying NativeList
            /// containing the unmanaged data for the tracked instances.
            /// </summary>
            [MethodImpl(AggressiveInlining)]
            public NativeList<TData> AsNativeList()
                => Storage.UnmanagedData<T, TData>.List;

            /// <summary>
            /// Returns a NativeArray view over the unmanaged data for the tracked instances.
            /// </summary>
            [MethodImpl(AggressiveInlining)]
            public NativeArray<TData> AsNativeArray()
                => Storage.UnmanagedData<T, TData>.Array;

            /// <summary>
            /// Returns a span view over the unmanaged data for the tracked instances.
            /// </summary>
            [MethodImpl(AggressiveInlining)]
            public Span<TData> AsSpan()
                => Storage.UnmanagedData<T, TData>.Array.AsSpan();

            /// <summary>
            /// Returns an UnsafeList view over the unmanaged data for the tracked instances.
            /// </summary>
            [MethodImpl(AggressiveInlining)]
            public unsafe ref UnsafeList<TData> AsUnsafeList()
                => ref *Storage.UnmanagedData<T, TData>.List.GetUnsafeList();

            /// <summary>
            /// Returns a reference to the unmanaged data for the tracked instance at the specified index.
            /// </summary>
            [MethodImpl(AggressiveInlining)]
            public unsafe ref TData ElementAt(int index)
                => ref ((TData*)Storage.UnmanagedData<T, TData>.Array.GetUnsafePtr())[index];
        }

        /// <summary>
        /// Stores the internal backing list before enumeration, instead of enumerating it directly.
        /// This is useful when instances are enabled/disabled during enumeration.
        /// </summary>
        /// <remarks>
        /// Adds a small overhead proportional to the number of objects.
        /// The components will be copied to a pooled list, which avoids unnecessary allocations.
        /// The list is returned to the pool automatically after enumeration.
        /// </remarks>
        public ImmediateEnumerable WithCopy
        {
            [MethodImpl(AggressiveInlining)]
            get => default;
        }

        public struct ImmediateEnumerable : ILinqFallbackEnumerable<PooledListEnumerator<T>, T>
        {
            [EditorBrowsable(Never)]
            [MethodImpl(AggressiveInlining)]
            public PooledListEnumerator<T> GetEnumerator()
            {
#if UNITY_EDITOR
                if (Utility.EditMode)
                    Storage.Instances<T>.EditMode.Refresh();
#endif

                var list = PooledList.Get<T>();
                list.List.AddRange(Storage.Instances<T>.List);
                return new(list);
            }

#if MODULE_ZLINQ
            /// <summary>
            /// Returns a ValueEnumerable struct that allows chaining of ZLinq operators.
            /// </summary>
            [MethodImpl(AggressiveInlining)]
            public ValueEnumerable<PooledListEnumerator<T>, T> AsValueEnumerable()
                => new(GetEnumerator());
#endif
        }

        /// <summary>
        /// Returns a <see cref="StrideEnumerable" /> that allows enumeration with a specified stride.
        /// The enumerable will yield every N-th element, with an offset incremented each frame.
        /// This is useful for creating a "rare update" mechanism that updates each object roughly once per N frames.
        /// </summary>
        /// <remarks>
        /// Keep in mind that objects are re-ordered in internal storage when they are enabled and disabled.
        /// This might result in missed or repeated instances when enumerated in consecutive frames.
        /// While this level of consistency is sufficient for many systems, you might want to implement a custom
        /// mechanism if you require better predictability.
        /// </remarks>
        /// <param name="stride">
        /// The step size to use for iterating through the instances.
        /// For example, <c>stride: 1</c> is equivalent to normally enumerating the sequence,
        /// <c>stride: 2</c> yield every second element, <c>stride: 3</c> yields every third, and so on.
        /// </param>
        [MethodImpl(AggressiveInlining)]
        public StrideEnumerable WithStride(int stride)
            => new(stride);

        public readonly struct StrideEnumerable : ILinqFallbackEnumerable<StrideEnumerator, T>
        {
            readonly int stride;

            public StrideEnumerable(int stride)
                => this.stride = stride;

            [MethodImpl(AggressiveInlining)]
            public StrideEnumerator GetEnumerator()
                => new(Storage.Instances<T>.List, stride);

#if MODULE_ZLINQ
            /// <summary>
            /// Returns a ValueEnumerable struct that allows chaining of ZLinq operators.
            /// </summary>
            [MethodImpl(AggressiveInlining)]
            public ValueEnumerable<StrideEnumerator, T> AsValueEnumerable()
                => new(GetEnumerator());
#endif
        }

        public struct StrideEnumerator : IValueEnumerator<T>
        {
            readonly T[] array;
            readonly int n;
            readonly int stride;
#if DEBUG
            readonly ListView<T> listView;
            readonly int version;
#endif
            int index;

            internal StrideEnumerator(List<T> list, int stride)
            {
                Assert.IsTrue(stride > 0, "Stride must be greater than 0.");
                int frameCount = UnityEngine.Time.frameCount;
                var view = list.AsInternalsView();
                array = view.Array;
                n = view.Count;
                index = frameCount % stride - stride;
                this.stride = stride;
#if DEBUG
                listView = view;
                version = view.Version;
#endif
            }

            [MethodImpl(AggressiveInlining)]
            public bool MoveNext()
            {
#if DEBUG
                [MethodImpl(NoInlining)]
                static void VersionHasChanged()
                {
                    string type = typeof(T).Name;
                    string msg
                        = $"An instance of '{type}' was enabled or disabled during " +
                          $"{nameof(Find)}.{nameof(Find.Instances)}<{type}> enumeration. This will result in invalid behaviour. " +
                          $"You can use .{nameof(WithCopy)} to work around this problem at a small performance cost.";

                    UnityEngine.Debug.LogError(msg);
                }

                if (listView.Version != version)
                {
                    VersionHasChanged();
                    return false;
                }
#endif

                return (index += stride) < n;
            }

            public ref readonly T Current
            {
                [MethodImpl(AggressiveInlining)]
                get => ref array[index];
            }

            bool IValueEnumerator<T>.TryGetNext(out T current)
            {
                if (MoveNext())
                {
                    current = Current;
                    return true;
                }

                MedicineUnsafeShim.SkipInit(out current);
                return false;
            }

            bool IValueEnumerator<T>.TryGetNonEnumeratedCount(out int count)
            {
                count = n;
                return true;
            }

            bool IValueEnumerator<T>.TryGetSpan(out ReadOnlySpan<T> span)
            {
#if DEBUG
                span = default;
                return false;
#else
                span = array.AsSpanUnsafe(0, n);
                return true;
#endif
            }

            bool IValueEnumerator<T>.TryCopyTo(Span<T> destination, Index offset)
            {
#if MODULE_ZLINQ && !DEBUG
                if (ZLinq.Internal.EnumeratorHelper.TryGetSlice<T>(array.AsSpanUnsafe(0, n), offset, destination.Length, out var slice))
                {
                    slice.CopyTo(destination);
                    return true;
                }
#endif
                return false;
            }

            void IDisposable.Dispose() { }
        }

        [DisallowReadonly]
        [StructLayout(LayoutKind.Auto)]
        public struct Enumerator : IValueEnumerator<T>
        {
            readonly T[] array;
            readonly int n;
#if DEBUG
            readonly ListView<T> listView;
            readonly int version;
#endif
            int index;

            internal Enumerator(List<T> list)
            {
                var listView = list.AsInternalsView();
                array = listView.Array;
                n = listView.Count;
                index = -1;
#if DEBUG
                this.listView = listView;
                version = listView.Version;
#endif
            }

            [MethodImpl(AggressiveInlining)]
            public bool MoveNext()
            {
#if DEBUG
                [MethodImpl(NoInlining)]
                static void VersionHasChanged()
                {
                    string type = typeof(T).Name;
                    string msg
                        = $"An instance of '{type}' was enabled or disabled during " +
                          $"{nameof(Find)}.{nameof(Find.Instances)}<{type}> enumeration. This will result in invalid behaviour. " +
                          $"You can use .{nameof(WithCopy)} to work around this problem at a small performance cost.";

                    UnityEngine.Debug.LogError(msg);
                }

                if (listView.Version != version)
                {
                    VersionHasChanged();
                    return false;
                }
#endif

                return ++index < n;
            }

            public ref readonly T Current
            {
                [MethodImpl(AggressiveInlining)]
                get => ref array[index];
            }

            bool IValueEnumerator<T>.TryGetNext(out T current)
            {
                if (MoveNext())
                {
                    current = Current;
                    return true;
                }

                MedicineUnsafeShim.SkipInit(out current);
                return false;
            }

            bool IValueEnumerator<T>.TryGetNonEnumeratedCount(out int count)
            {
                count = n;
                return true;
            }

            bool IValueEnumerator<T>.TryGetSpan(out ReadOnlySpan<T> span)
            {
#if DEBUG
                span = default;
                return false;
#else
                span = array.AsSpanUnsafe(0, n);
                return true;
#endif
            }

            bool IValueEnumerator<T>.TryCopyTo(Span<T> destination, Index offset)
            {
#if MODULE_ZLINQ && !DEBUG
                if (ZLinq.Internal.EnumeratorHelper.TryGetSlice<T>(array.AsSpanUnsafe(0, n), offset, destination.Length, out var slice))
                {
                    slice.CopyTo(destination);
                    return true;
                }
#endif
                return false;
            }

            void IDisposable.Dispose() { }
        }
    }
}
