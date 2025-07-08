public readonly struct CacheIgnore<T>(T value) : IEquatable<CacheIgnore<T>>
{
    public readonly T Value = value;

    bool IEquatable<CacheIgnore<T>>.Equals(CacheIgnore<T> other) => true;
    public override bool Equals(object? obj) => obj is CacheIgnore<T>;
    public override int GetHashCode() => 0;

    public static implicit operator CacheIgnore<T>(T value) => new(value);
    public static implicit operator T(CacheIgnore<T> value) => value.Value;
}