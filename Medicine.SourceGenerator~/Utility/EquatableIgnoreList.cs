using System.Collections;

/// <summary>
/// A wrapper class that overrides equality and hash code behavior to make all instances equal.
/// As a result, the source generator ignores the list contents for caching purposes.
/// </summary>
/// <typeparam name="T">The type of elements in the list.</typeparam>
public sealed class EquatableIgnoreList<T> : IList<T>, IEquatable<EquatableIgnoreList<T>>
{
    readonly List<T> list = [];

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        foreach (var item in list)
            yield return item;
    }

    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();

    public List<T>.Enumerator GetEnumerator()
        => list.GetEnumerator();

    public void Add(T item)
        => list.Add(item);

    public void Clear()
        => list.Clear();

    public bool Contains(T item)
        => list.Contains(item);

    public void CopyTo(T[] array, int arrayIndex)
        => list.CopyTo(array, arrayIndex);

    public bool Remove(T item)
        => list.Remove(item);

    public int Count
        => list.Count;

    public bool IsReadOnly
        => false;

    public int IndexOf(T item)
        => list.IndexOf(item);

    public void Insert(int index, T item)
        => list.Insert(index, item);

    public void RemoveAt(int index)
        => list.RemoveAt(index);

    public T this[int index]
    {
        get => list[index];
        set => list[index] = value;
    }

    public bool Equals(EquatableIgnoreList<T> other)
        => true;

    public override bool Equals(object? obj)
        => obj is EquatableIgnoreList<T>;

    public override int GetHashCode()
        => 0;

    public static bool operator ==(EquatableIgnoreList<T> left, EquatableIgnoreList<T> right)
        => true;

    public static bool operator !=(EquatableIgnoreList<T> left, EquatableIgnoreList<T> right)
        => true;
}