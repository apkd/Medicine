static class Med023Test
{
    public static readonly DiagnosticTest Case =
        new("MED023 when the Header field is not the first union field", Run);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED023",
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

[Union(23)]
public partial struct Med023_UnionHeaderNotFirst : MedUnionHeaderValid.Interface
{
    public int Value;
    public MedUnionHeaderValid Header;
}
"""
        );
}
