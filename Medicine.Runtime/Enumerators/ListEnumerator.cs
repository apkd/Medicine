#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using static System.ComponentModel.EditorBrowsableState;
using static System.Runtime.CompilerServices.MethodImplOptions;

namespace Medicine.Internal
{
#if MODULE_ZLINQ
    using ZLinq;
    using ZLinq.Internal;
    using MedicineUnsafeShim = Unsafe;
#endif

    /// <summary>
    /// This version of <see cref="List{T}.Enumerator"/> skips list version checks in release builds.
    /// </summary>
    [EditorBrowsable(Never)]
    [DisallowReadonly]
    public struct ListEnumerator<T> : IValueEnumerator<T>
    {
        readonly T[]? array;
        readonly int n;
        int i;

#if DEBUG
        readonly ListView<T> list;
        readonly int version;
#endif

        [MethodImpl(AggressiveInlining)]
        public bool MoveNext()
        {
#if DEBUG
            [MethodImpl(NoInlining)]
            static void VersionHasChanged()
                => UnityEngine.Debug.LogError($"The List<{typeof(T).Name} was modified during enumeration.");

            if (array is null)
                return false;

            if (list.Version != version)
                VersionHasChanged();
#endif
            return ++i < n;
        }

        public ref readonly T Current
        {
            [MethodImpl(AggressiveInlining)]
            get => ref array![i];
        }

        public ListEnumerator(List<T> list)
        {
            var listView = list.AsInternalsView();
            array = listView.Array;
            n = listView.Count;
            i = -1;
#if DEBUG
            this.list = listView;
            version = listView.Version;
#endif
        }

        public void Dispose() { }

        public bool TryGetNext(out T current)
        {
            if (MoveNext())
            {
                current = Current;
                return true;
            }

            MedicineUnsafeShim.SkipInit(out current);
            return false;
        }

        public bool TryGetNonEnumeratedCount(out int count)
        {
            count = n - i - 1;
            return true;
        }

        public bool TryGetSpan(out ReadOnlySpan<T> span)
        {
#if MODULE_ZLINQ
            span = array.AsSpan(start: 0, length: n);
            return true;
#else
            span = default;
            return false;
#endif
        }

        public bool TryCopyTo(Span<T> destination, Index offset)
        {
#if MODULE_ZLINQ
            if (EnumeratorHelper.TryGetSlice<T>(array.AsSpan(start: 0, length: n), offset, destination.Length, out var slice))
            {
                slice.CopyTo(destination);
                return true;
            }
#endif

            return false;
        }
    }
}