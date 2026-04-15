#define DEBUG
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using static System.Reflection.BindingFlags;

/// <summary>
/// Clears a rented scratch collection when disposed.
/// </summary>
/// <typeparam name="TCollection">Collection type being returned to scratch storage.</typeparam>
/// <param name="collection">Collection instance to clear on disposal.</param>
public readonly struct ClearDisposable<TCollection>(TCollection collection) : IDisposable
    where TCollection : new()
{
    void IDisposable.Dispose()
        => Scratch.ClearCollection(collection);
}

/// <summary>
/// Provides thread-local reusable scratch collections for generator transforms.
/// </summary>
public static class Scratch
{
    /// <summary>
    /// Rents scratch slot A for a collection type.
    /// </summary>
    /// <typeparam name="T">Collection type to rent.</typeparam>
    /// <param name="value">Receives the rented collection.</param>
    /// <returns>A disposable that clears the collection when disposed.</returns>
    public static ClearDisposable<T> RentA<T>(out T value) where T : new()
        => Get(ref Storage<T>.a, out value);

    /// <summary>
    /// Rents scratch slot B for a collection type.
    /// </summary>
    /// <typeparam name="T">Collection type to rent.</typeparam>
    /// <param name="value">Receives the rented collection.</param>
    /// <returns>A disposable that clears the collection when disposed.</returns>
    public static ClearDisposable<T> RentB<T>(out T value) where T : new()
        => Get(ref Storage<T>.b, out value);

    /// <summary>
    /// Rents scratch slot C for a collection type.
    /// </summary>
    /// <typeparam name="T">Collection type to rent.</typeparam>
    /// <param name="value">Receives the rented collection.</param>
    /// <returns>A disposable that clears the collection when disposed.</returns>
    public static ClearDisposable<T> RentC<T>(out T value) where T : new()
        => Get(ref Storage<T>.c, out value);

    /// <summary>
    /// Rents scratch slot D for a collection type.
    /// </summary>
    /// <typeparam name="T">Collection type to rent.</typeparam>
    /// <param name="value">Receives the rented collection.</param>
    /// <returns>A disposable that clears the collection when disposed.</returns>
    public static ClearDisposable<T> RentD<T>(out T value) where T : new()
        => Get(ref Storage<T>.d, out value);

    /// <summary>
    /// Clears a scratch collection using its cached <c>Clear</c> delegate.
    /// </summary>
    /// <typeparam name="TCollection">Collection type to clear.</typeparam>
    /// <param name="collection">Collection instance to clear.</param>
    public static void ClearCollection<TCollection>(TCollection collection)
        where TCollection : new()
        => Storage<TCollection>.TypeInfo.Clear(collection);

    /// <summary>
    /// Rents the specified scratch storage slot.
    /// </summary>
    /// <typeparam name="T">Collection type to rent.</typeparam>
    /// <param name="storage">Thread-local storage slot.</param>
    /// <param name="result">Receives the rented collection.</param>
    /// <returns>A disposable that clears the collection when disposed.</returns>
    public static ClearDisposable<T> Get<T>(ref T? storage, out T result) where T : new()
    {
        storage ??= Alloc();
#if DEBUG
        if (Storage<T>.TypeInfo.GetCount(storage) > 0)
            throw new InvalidOperationException($"Scratch<{typeof(T).Name}> is not empty.");
#endif
        return new(result = storage);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static T Alloc()
        {
            if (typeof(T).IsGenericType)
            {
                if (typeof(T) == typeof(HashSet<string>))
                    return (T)(object)new HashSet<string>(StringComparer.Ordinal);

                if (typeof(T) == typeof(HashSet<ISymbol>))
                    return (T)(object)new HashSet<ISymbol>(SymbolEqualityComparer.Default);

                if (typeof(T) == typeof(HashSet<INamedTypeSymbol>))
                    return (T)(object)new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

                if (typeof(T) == typeof(HashSet<MethodSignatureKey>))
                    return (T)(object)new HashSet<MethodSignatureKey>(MethodSignatureComparer.Instance);

                if (typeof(T) == typeof(HashSet<PropertySignatureKey>))
                    return (T)(object)new HashSet<PropertySignatureKey>(PropertySignatureComparer.Instance);

                if (typeof(T).GetGenericTypeDefinition() == typeof(List<>))
                    return new();

                if (typeof(T).GetGenericTypeDefinition() == typeof(HashSet<>))
                    return new();
            }

            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Exposes the thread-local storage slots used by <see cref="Scratch"/>.
    /// </summary>
    /// <typeparam name="T">Collection type stored in the slots.</typeparam>
    public static class Storage<T> where T : new()
    {
        [ThreadStatic] internal static T? a;
        [ThreadStatic] internal static T? b;
        [ThreadStatic] internal static T? c;
        [ThreadStatic] internal static T? d;

        /// <summary>
        /// Cached delegates used to inspect and clear scratch collections of type <typeparamref name="T"/>.
        /// </summary>
        public static class TypeInfo
        {
            internal static readonly Func<T, int> GetCount = BuildCount();
            internal static readonly Action<T> Clear = BuildClear();

            static Func<T, int> BuildCount()
            {
                var type = typeof(T);

                if (type.GetProperty("Count", Public | Instance) is not { } countProp)
                    throw new NotSupportedException($"No 'Count' property found for '{type.FullName}'.");

                var parameter = Expression.Parameter(type, "c");
                var body = Expression.Property(parameter, countProp);
                return Expression.Lambda<Func<T, int>>(body, parameter).Compile();
            }

            static Action<T> BuildClear()
            {
                var type = typeof(T);

                if (type.GetMethod("Clear", Public | Instance) is not { } clearMethod)
                    throw new NotSupportedException($"No 'Clear' method found for '{type.FullName}'.");

                var parameter = Expression.Parameter(type, "c");
                var body = Expression.Call(parameter, clearMethod);
                return Expression.Lambda<Action<T>>(body, parameter).Compile();
            }
        }
    }
}


