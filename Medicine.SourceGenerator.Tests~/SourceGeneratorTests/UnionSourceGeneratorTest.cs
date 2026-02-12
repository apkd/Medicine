static class UnionSourceGeneratorTest
{
    public static readonly DiagnosticTest Case =
        new("Union generator adds source for [UnionHeader] and [Union]", Run);

    static void Run()
        => SourceGeneratorTester.AssertGeneratesSource(
            source: """
using Medicine;

public enum ContractTypeIDs : byte
{
    None = 0,
    A = 1,
}

[UnionHeader]
public partial struct ContractUnion
{
    public interface Interface
    {
        int Value { get; }
    }

    public ContractTypeIDs TypeID;
}

[Union(1)]
public partial struct ContractUnionA : ContractUnion.Interface
{
    public ContractUnion Header;
    public int Value => 42;
}
""",
            generator: new UnionStructSourceGenerator()
        );
}
