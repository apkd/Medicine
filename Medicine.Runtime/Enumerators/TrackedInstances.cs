using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using UnityEngine.Assertions;
using UnityEngine.Pool;
using static System.ComponentModel.EditorBrowsableState;
using static System.Runtime.CompilerServices.MethodImplOptions;

// ReSharper disable UnusedMember.Global

namespace Medicine.Internal
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
    [EditorBrowsable(Never)]
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

        public T this[int index]
        {
            [MethodImpl(AggressiveInlining)]
            get => Storage.Instances<T>.List[index];
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
        /// Copies the tracked instance list to a pooled list.
        /// It is up to the caller to call <c>Dispose()</c> on the returned PooledObject handle.
        /// </summary>
        [MustDisposeResource]
        [MethodImpl(AggressiveInlining)]
        public PooledList<T> ToPooledList(out List<T> list)
        {
            var handle = PooledList.Get(out list);
            list.AddRange(Storage.Instances<T>.List);
            return handle;
        }

        public void CopyTo(List<T> destination, int extraCapacity = 16)
        {
            if (Count is 0)
                return;

            if (destination.Capacity < Count)
                destination.Capacity = Count + extraCapacity;

            var destinationListView = destination.AsInternalsView();
            destinationListView.Count = Count;

            Storage.Instances<T>.List.CopyTo(
                array: destinationListView.Array!,
                arrayIndex: 0
            );

            Array.Clear(
                array: destinationListView.Array!,
                index: destinationListView.Array!.Length - Count,
                length: Count
            );
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
                this.listView = view;
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
                span = array.AsSpan(0, n);
                return true;
#endif
            }

            bool IValueEnumerator<T>.TryCopyTo(Span<T> destination, Index offset)
            {
#if MODULE_ZLINQ && !DEBUG
                if (ZLinq.Internal.EnumeratorHelper.TryGetSlice<T>(array.AsSpan(0, n), offset, destination.Length, out var slice))
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
                span = array.AsSpan(0, n);
                return true;
#endif
            }

            bool IValueEnumerator<T>.TryCopyTo(Span<T> destination, Index offset)
            {
#if MODULE_ZLINQ && !DEBUG
                if (ZLinq.Internal.EnumeratorHelper.TryGetSlice<T>(array.AsSpan(0, n), offset, destination.Length, out var slice))
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