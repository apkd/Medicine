using System.Collections.Concurrent;
using System.Text;

/// <summary>
/// Provides a pool of reusable <see cref="StringBuilder"/> instances to minimize
/// allocations and improve performance for high-frequency string manipulations.
/// </summary>
public static class StringBuilderPool
{
    static readonly ConcurrentStack<StringBuilder> Stack = [];

    public static StringBuilder Rent()
        => Stack.TryPop(out var stringBuilder)
            ? stringBuilder
            : new(capacity: 1024);

    public static void Return(StringBuilder stringBuilder)
        => Stack.Push(stringBuilder.Clear());
}