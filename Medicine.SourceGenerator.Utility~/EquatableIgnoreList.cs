using System.Collections;

/// <summary>
/// List wrapper whose equality intentionally ignores its contents.
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

    /// <summary>
    /// Returns an enumerator over the underlying list.
    /// </summary>
    /// <returns>A list enumerator.</returns>
    public List<T>.Enumerator GetEnumerator()
        => list.GetEnumerator();

    /// <inheritdoc/>
    public void Add(T item)
        => list.Add(item);

    /// <inheritdoc/>
    public void Clear()
        => list.Clear();

    /// <inheritdoc/>
    public bool Contains(T item)
        => list.Contains(item);

    /// <inheritdoc/>
    public void CopyTo(T[] array, int arrayIndex)
        => list.CopyTo(array, arrayIndex);

    /// <inheritdoc/>
    public bool Remove(T item)
        => list.Remove(item);

    /// <inheritdoc/>
    public int Count
        => list.Count;

    /// <inheritdoc/>
    public bool IsReadOnly
        => false;

    /// <inheritdoc/>
    public int IndexOf(T item)
        => list.IndexOf(item);

    /// <inheritdoc/>
    public void Insert(int index, T item)
        => list.Insert(index, item);

    /// <inheritdoc/>
    public void RemoveAt(int index)
        => list.RemoveAt(index);

    /// <inheritdoc/>
    public T this[int index]
    {
        get => list[index];
        set => list[index] = value;
    }

    /// <summary>
    /// Returns <c>true</c> for any other <see cref="EquatableIgnoreList{T}"/> instance.
    /// </summary>
    /// <returns>Always <c>true</c>.</returns>
    public bool Equals(EquatableIgnoreList<T> other)
        => true;

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is EquatableIgnoreList<T>;

    /// <inheritdoc/>
    public override int GetHashCode()
        => 0;

    /// <summary>
    /// Returns <c>true</c> for any two operands.
    /// </summary>
    /// <returns>Always <c>true</c>.</returns>
    public static bool operator ==(EquatableIgnoreList<T> left, EquatableIgnoreList<T> right)
        => true;

    /// <summary>
    /// Returns <c>true</c> for any two operands.
    /// </summary>
    /// <returns>Always <c>true</c>.</returns>
    public static bool operator !=(EquatableIgnoreList<T> left, EquatableIgnoreList<T> right)
        => true;
}
