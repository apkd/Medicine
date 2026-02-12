static class WrapValueEnumerableSourceGeneratorTest
{
    public static readonly DiagnosticTest Case =
        new("WrapValueEnumerable generator adds source for attributed members", Run);

    static void Run()
        => SourceGeneratorTester.AssertGeneratesSource(
            source: """
using Medicine;

readonly struct WrapContractEnumerator<TInner>
{
    public bool MoveNext()
        => false;

    public TInner Current
        => default!;
}

readonly struct WrapContractEnumerable<TState, TElement>
{
    public WrapContractEnumerator<TState> GetEnumerator()
        => default;
}

static partial class WrapContract
{
    [WrapValueEnumerable]
    public static Query Wrapped()
        => default(WrapContractEnumerable<int, int>);
}
""",
            generator: SourceGeneratorFactory.Create("WrapValueEnumerableSourceGenerator")
        );
}
