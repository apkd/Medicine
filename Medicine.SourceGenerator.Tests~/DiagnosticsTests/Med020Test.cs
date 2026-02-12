static class Med020Test
{
    public static readonly DiagnosticTest Case =
        new("MED020 when [UnionHeader] is missing TypeID field", Run);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED020",
            source: """
using Medicine;

public enum TypeIDs : byte
{
    Unset = 0,
    A = 1,
}

[UnionHeader]
public partial struct Med020_HeaderMissingTypeId
{
    public interface Interface { }
}
"""
        );
}
