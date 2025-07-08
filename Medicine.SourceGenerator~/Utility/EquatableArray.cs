using System.Collections;

public readonly struct EquatableArray<T>(T[]? array) : IEquatable<EquatableArray<T>>, IEnumerable<T>, IReadOnlyCollection<T>
    where T : IEquatable<T>
{
    public static readonly EquatableArray<T> Empty = new([]);

    public int Length => array?.Length ?? 0;

    public bool Equals(EquatableArray<T> other)
        => AsSpan().SequenceEqual(other.AsSpan());

    public override bool Equals(object? obj)
        => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (array is not { Length: > 0 })
            return 0;

        int hashCode = 0;

        foreach (var item in array)
            hashCode = hashCode * 31 + item.GetHashCode();

        return hashCode;
    }

    public T[] AsArray()
        => array ?? [];

    public Span<T> AsSpan()
        => array.AsSpan();

    public Span<T>.Enumerator GetEnumerator()
        => AsSpan().GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
        => (array ?? []).AsEnumerable().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
        => (array ?? []).GetEnumerator();

    int IReadOnlyCollection<T>.Count
        => Length;

    public static implicit operator EquatableArray<T>(T[] array)
        => new(array);

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right)
        => left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right)
        => !left.Equals(right);
}