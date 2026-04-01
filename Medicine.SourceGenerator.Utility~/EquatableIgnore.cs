/// <summary>
/// A wrapper struct that overrides equality and hash code behavior to make all instances equal,
/// regardless of the wrapped value.
/// As a result, the source generator ignores the value for caching purposes.
/// </summary>
/// <typeparam name="T">The type of the value being wrapped.</typeparam>
public readonly struct EquatableIgnore<T>(T value) : IEquatable<EquatableIgnore<T>>
{
    public readonly T Value = value;

    bool IEquatable<EquatableIgnore<T>>.Equals(EquatableIgnore<T> other) => true;
    public override bool Equals(object? obj) => obj is EquatableIgnore<T>;
    public override int GetHashCode() => 0;

    public static implicit operator EquatableIgnore<T>(T value) => new(value);
    public static implicit operator T(EquatableIgnore<T> value) => value.Value;
}