/// <summary>
/// A lazy initialization wrapper that defers the calculation of a value until it is accessed for the first time.
/// Based on the <see cref="Lazy{T}"/> class.
/// </summary>
/// <remarks>
/// Implements <see cref="IEquatable{T}"/> to always return true, so that it is ignored
/// for caching purposes in incremental source generators.
/// </remarks>
/// <typeparam name="T">
/// The type of the object that is being lazily initialized.
/// </typeparam>
public sealed class Defer<T>(Func<T> valueFactory) : Lazy<T>(valueFactory, LazyThreadSafetyMode.None), IEquatable<Defer<T>>
{
    bool IEquatable<Defer<T>>.Equals(Defer<T> other)
        => true; // ignored for caching

    public override int GetHashCode()
        => 0;
}