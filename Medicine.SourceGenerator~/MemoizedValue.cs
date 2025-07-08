readonly struct MemoizedValue<T>(T value)
{
    public T From(Func<T> init) => value;
}

sealed class LazyMemoizedValue<T, TContext>(T value)
{
    public T From(TContext context, Func<TContext, T> init) => value;
}

static class Static
{
    public static readonly MemoizedValue<int> SomeKey = new(2);
    public static readonly LazyMemoizedValue<int, int> SomeOtherKey = new(2);
}

static class Usage
{
    static void Test()
    {
        var x = Static.SomeKey.From(static () => 2);
        var y = Static.SomeOtherKey.From(2, static z => z * 2);
    }
}