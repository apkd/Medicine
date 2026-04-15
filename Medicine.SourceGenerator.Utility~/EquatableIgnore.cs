/// <summary>
/// Wraps a value that should be ignored by equality-based incremental caching.
/// </summary>
/// <typeparam name="T">The type of the value being wrapped.</typeparam>
public readonly struct EquatableIgnore<T>(T value) : IEquatable<EquatableIgnore<T>>
{
    /// <summary>
    /// The wrapped value.
    /// </summary>
    public readonly T Value = value;

    bool IEquatable<EquatableIgnore<T>>.Equals(EquatableIgnore<T> other) => true;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is EquatableIgnore<T>;

    /// <inheritdoc/>
    public override int GetHashCode() => 0;

    public static implicit operator EquatableIgnore<T>(T value) => new(value);

    /// <summary>
    /// Returns the wrapped value.
    /// </summary>
    /// <param name="value">Wrapper to unwrap.</param>
    /// <returns>The underlying value.</returns>
    public static implicit operator T(EquatableIgnore<T> value) => value.Value;
}
