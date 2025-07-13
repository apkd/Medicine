using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static System.ComponentModel.EditorBrowsableState;
using static System.Runtime.CompilerServices.MethodImplOptions;

namespace Medicine.Internal
{
#if MODULE_ZLINQ
    using ZLinq;
#endif

    [EditorBrowsable(Never)]
    [StructLayout(LayoutKind.Auto)]
    public struct PooledListEnumerator<T> : IValueEnumerator<T>
        where T : class
    {
        PooledList<T> disposable;
        ListEnumerator<T> enumerator;

        internal PooledListEnumerator(PooledList<T> disposable)
        {
            this.disposable = disposable;
            enumerator = new(disposable.List);
        }

        [MethodImpl(AggressiveInlining)]
        public bool MoveNext()
            => enumerator.MoveNext();

        public ref readonly T Current
        {
            [MethodImpl(AggressiveInlining)]
            get => ref enumerator.Current;
        }

        public void Dispose()
        {
            enumerator.Dispose();
            disposable.Dispose(); // (the pool clears the list)
        }

        bool IValueEnumerator<T>.TryGetNext(out T current)
            => enumerator.TryGetNext(out current);

        bool IValueEnumerator<T>.TryGetNonEnumeratedCount(out int count)
            => enumerator.TryGetNonEnumeratedCount(out count);

        bool IValueEnumerator<T>.TryGetSpan(out ReadOnlySpan<T> span)
            => enumerator.TryGetSpan(out span);

        bool IValueEnumerator<T>.TryCopyTo(Span<T> destination, Index offset)
            => enumerator.TryCopyTo(destination, offset);
    }
}