static class Med022Test
{
    public static readonly DiagnosticTest Case =
        new("MED022 when a [Union] type is missing the Header field", Run);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED022",
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

[Union(22)]
public partial struct Med022_UnionMissingHeaderField : MedUnionHeaderValid.Interface
{
    public int Value;
}
"""
        );
}
