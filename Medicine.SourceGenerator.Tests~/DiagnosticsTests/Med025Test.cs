static class Med025Test
{
    public static readonly DiagnosticTest Case =
        new("MED025 when two [Union] types share the same union id", Run);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED025",
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

[Union(25)]
public partial struct Med025_FirstDuplicateUnion : MedUnionHeaderValid.Interface
{
    public MedUnionHeaderValid Header;
}

[Union(25)]
public partial struct Med025_SecondDuplicateUnion : MedUnionHeaderValid.Interface
{
    public MedUnionHeaderValid Header;
}
"""
        );
}
