static class Med021Test
{
    public static readonly DiagnosticTest Case =
        new("MED021 when a [Union] type does not implement the header interface", Run);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED021",
            source: """
using Medicine;

public enum TypeIDs : byte
{
    Unset = 0,
    A = 1,
}

[UnionHeader]
public partial struct MedUnionHeaderValid
{
    public interface Interface { }
    public TypeIDs TypeID;
}

[Union(21)]
public partial struct Med021_UnionMissingInterfaceImplementation
{
    public MedUnionHeaderValid Header;
}
"""
        );
}
