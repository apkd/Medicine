#define DEBUG
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using static System.Reflection.BindingFlags;

public readonly struct ClearDisposable<TCollection>(TCollection collection) : IDisposable
    where TCollection : new()
{
    void IDisposable.Dispose()
        => Scratch.ClearCollection(collection);
}

static class Scratch
{
    public static ClearDisposable<T> RentA<T>(out T value) where T : new()
        => Get(ref Storage<T>.a, out value);

    public static ClearDisposable<T> RentB<T>(out T value) where T : new()
        => Get(ref Storage<T>.b, out value);

    public static ClearDisposable<T> RentC<T>(out T value) where T : new()
        => Get(ref Storage<T>.c, out value);

    public static ClearDisposable<T> RentD<T>(out T value) where T : new()
        => Get(ref Storage<T>.d, out value);

    public static void ClearCollection<TCollection>(TCollection collection)
        where TCollection : new()
        => Storage<TCollection>.TypeInfo.Clear(collection);

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

    public static class Storage<T> where T : new()
    {
        [ThreadStatic] internal static T? a;
        [ThreadStatic] internal static T? b;
        [ThreadStatic] internal static T? c;
        [ThreadStatic] internal static T? d;

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
