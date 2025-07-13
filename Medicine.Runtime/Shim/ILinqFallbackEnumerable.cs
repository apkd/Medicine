#nullable enable
using System.ComponentModel;
using static System.ComponentModel.EditorBrowsableState;

namespace Medicine.Internal
{
#if MODULE_ZLINQ
    /// <summary>
    /// When ZLinq is installed, we don't allow LINQ to avoid accidental usage.
    /// Use AsValueEnumerable() instead.
    /// </summary>
    // ReSharper disable once UnusedTypeParameter
    [EditorBrowsable(Never)]
    public interface ILinqFallbackEnumerable<out TEnumerator, out T>
        where TEnumerator : ZLinq.IValueEnumerator<T> { }
#else
    using System;
    using System.Collections.Generic;
    using System.Collections;

    /// <summary>
    /// An interface which implements IEnumerable{T} via IValueEnumerator when ZLinq isn't
    /// available to allow for LINQ usage when the user doesn't want a ZLinq dependency.
    /// </summary>
    /// <remarks>
    /// This allocates some memory here and there and isn't very efficient,
    /// but if you care about efficiency, you should just use ZLinq instead...
    /// </remarks>
    /// <seealso href="https://github.com/Cysharp/ZLinq"/>
    [EditorBrowsable(Never)]
    public interface ILinqFallbackEnumerable<out TEnumerator, out T>
        : IEnumerable<T>
        where TEnumerator : IValueEnumerator<T>
    {
        new TEnumerator GetEnumerator();

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            using var enumerator = GetEnumerator();

            while (enumerator.TryGetNext(out var current))
                yield return current;
        }

        IEnumerator IEnumerable.GetEnumerator()
            => (this as IEnumerable<T>).GetEnumerator();
    }

    /// <summary>
    /// This is a sneaky shim for the IValueEnumerator{T} interface,
    /// letting us implement and refer to it even when ZLinq isn't installed.
    /// </summary>
    [EditorBrowsable(Never)]
    public interface IValueEnumerator<T> : IDisposable
    {
        bool TryGetNext(out T current);

        bool TryGetNonEnumeratedCount(out int count)
        {
            count = 0;
            return false;
        }

        bool TryGetSpan(out ReadOnlySpan<T> span)
        {
            span = default;
            return false;
        }

        bool TryCopyTo(Span<T> destination, Index offset)
            => false;
    }
#endif
}