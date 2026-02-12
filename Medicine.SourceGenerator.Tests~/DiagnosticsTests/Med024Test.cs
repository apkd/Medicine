static class Med024Test
{
    public static readonly DiagnosticTest Case =
        new("MED024 when a union type is missing the [Union] attribute", Run);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED024",
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

public partial struct Med024_MissingUnionAttribute : MedUnionHeaderValid.Interface
{
    public MedUnionHeaderValid Header;
}
"""
        );
}
