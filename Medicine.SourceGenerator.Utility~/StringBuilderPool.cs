using System.Collections.Concurrent;
using System.Text;

/// <summary>
/// Provides pooled <see cref="StringBuilder"/> instances for allocation-sensitive code paths.
/// </summary>
public static class StringBuilderPool
{
    static readonly ConcurrentStack<StringBuilder> Stack = [];

    /// <summary>
    /// Rents a reusable <see cref="StringBuilder"/>.
    /// </summary>
    /// <returns>A cleared builder with at least the default initial capacity.</returns>
    public static StringBuilder Rent()
        => Stack.TryPop(out var stringBuilder)
            ? stringBuilder
            : new(capacity: 1024);

    /// <summary>
    /// Returns a builder to the pool after clearing its contents.
    /// </summary>
    /// <param name="stringBuilder">Builder to recycle.</param>
    public static void Return(StringBuilder stringBuilder)
        => Stack.Push(stringBuilder.Clear());
}
