using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
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
            get => Storage.Instances<T>.List.Count;
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
            => Storage.Instances<T>.List[index];

#if MODULE_ZLINQ
        /// <summary>
        /// Returns a ValueEnumerable struct that allows chaining of ZLinq operators.
        /// </summary>
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
        public PooledObject<List<T>> ToPooledList(out List<T> list)
        {
            var handle = ListPool<T>.Get(out list);
            list.AddRange(Storage.Instances<T>.List);
            return handle;
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
        public WithImmediateCopy Immediate
            => default;

        public struct WithImmediateCopy : ILinqFallbackEnumerable<PooledListEnumerator<T>, T>
        {
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
            public ValueEnumerable<PooledListEnumerator<T>, T> AsValueEnumerable()
                => new(GetEnumerator());
#endif
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
                          $"You can use .{nameof(Immediate)} to work around this problem at a small performance cost.";

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
                MedicineUnsafeShim.SkipInit(out count);
                return false;
            }

            bool IValueEnumerator<T>.TryGetSpan(out ReadOnlySpan<T> span)
            {
                span = default;
                return false;
            }

            bool IValueEnumerator<T>.TryCopyTo(Span<T> destination, Index offset)
                => false;

            void IDisposable.Dispose() { }
        }
    }
}