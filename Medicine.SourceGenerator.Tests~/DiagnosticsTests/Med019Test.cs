static class Med019Test
{
    public static readonly DiagnosticTest Case =
        new("MED019 when [UnionHeader] is missing nested Interface", Run);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED019",
            source: """
using Medicine;

public enum TypeIDs : byte
{
    Unset = 0,
    A = 1,
}

[UnionHeader]
public partial struct Med019_HeaderMissingInterface
{
    public TypeIDs TypeID;
}
"""
        );
}
