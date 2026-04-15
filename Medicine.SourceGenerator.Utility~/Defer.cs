/// <summary>
/// Lazily creates a value while excluding the wrapper from equality-based caching.
/// Based on the <see cref="Lazy{T}"/> class.
/// </summary>
/// <remarks>
/// Implements <see cref="IEquatable{T}"/> to always return true, so that it is ignored
/// for caching purposes in incremental source generators.
/// </remarks>
/// <typeparam name="T">
/// The type of the object that is being lazily initialized.
/// </typeparam>
/// <param name="valueFactory">Factory invoked on first access to <see cref="Lazy{T}.Value"/>.</param>
public sealed class Defer<T>(Func<T> valueFactory) : Lazy<T>(valueFactory, LazyThreadSafetyMode.None), IEquatable<Defer<T>>
{
    bool IEquatable<Defer<T>>.Equals(Defer<T> other)
        => true; // ignored for caching

    /// <inheritdoc/>
    public override int GetHashCode()
        => 0;
}
