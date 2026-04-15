using System.Collections;

/// <summary>
/// Array wrapper with value-based equality semantics.
/// </summary>
/// <typeparam name="T">Element type.</typeparam>
public readonly struct EquatableArray<T>(T[]? array) : IEquatable<EquatableArray<T>>, IEnumerable<T>, IReadOnlyCollection<T>
    where T : IEquatable<T>
{
    /// <summary>
    /// Empty array wrapper.
    /// </summary>
    public static readonly EquatableArray<T> Empty = new([]);

    /// <summary>
    /// Number of elements in the wrapped array.
    /// </summary>
    public int Length => array?.Length ?? 0;

    /// <summary>
    /// Returns whether both wrappers contain equal sequences.
    /// </summary>
    /// <param name="other">Wrapper to compare.</param>
    /// <returns><c>true</c> when both arrays contain equal elements in the same order.</returns>
    public bool Equals(EquatableArray<T> other)
        => AsSpan().SequenceEqual(other.AsSpan());

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is EquatableArray<T> other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        if (array is not { Length: > 0 })
            return 0;

        int hashCode = 0;

        foreach (var item in array)
            hashCode = hashCode * 31 + item.GetHashCode();

        return hashCode;
    }

    /// <summary>
    /// Returns the wrapped array, or an empty array when no array is present.
    /// </summary>
    /// <returns>The wrapped array contents.</returns>
    public T[] AsArray()
        => array ?? [];

    /// <summary>
    /// Returns the wrapped array as a span.
    /// </summary>
    /// <returns>A span over the wrapped array contents.</returns>
    public Span<T> AsSpan()
        => (array ?? []).AsSpan();

    /// <summary>
    /// Returns a span enumerator over the wrapped array.
    /// </summary>
    /// <returns>An enumerator over the current contents.</returns>
    public Span<T>.Enumerator GetEnumerator()
        => AsSpan().GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
        => (array ?? []).AsEnumerable().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
        => (array ?? []).GetEnumerator();

    int IReadOnlyCollection<T>.Count
        => Length;

    /// <summary>
    /// Wraps an array in an <see cref="EquatableArray{T}"/>.
    /// </summary>
    /// <param name="array">Array to wrap.</param>
    /// <returns>A value-equality wrapper for <paramref name="array"/>.</returns>
    public static implicit operator EquatableArray<T>(T[] array)
        => new(array);

    /// <summary>
    /// Returns whether both wrappers contain equal sequences.
    /// </summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><c>true</c> when both operands are equal.</returns>
    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right)
        => left.Equals(right);

    /// <summary>
    /// Returns whether two wrappers contain different sequences.
    /// </summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><c>true</c> when the operands are not equal.</returns>
    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right)
        => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString()
        => (array, typeof(T)) switch
        {
            ({Length: > 0}, { IsPrimitive: true })
                => $"[{array.Select(x => x?.ToString() ?? "null").Join(", ")}]",
            ({Length: > 0},  { FullName: "System.String" })
                => $"[{array.Select(x => $"\"{x as string ?? "null"}\"").Join(", ")}]",
            ({Length: > 0}, _) => $"{typeof(T).Name}[{array.Length}]",
            _ => "[]",
        };
}
