static class Med032Test
{
    public static readonly DiagnosticTest Case =
        new("MED032 when [Union] custom ctor does not assign header TypeID", Run);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED032",
            source: """
using Medicine;

public enum TypeIDs : byte
{
    Unset = 0,
    A = 1,
}

[UnionHeader]
public partial struct Med032_Header
{
    public interface Interface { }
    public TypeIDs TypeID;
}

[Union(32)]
public partial struct Med032_Union : Med032_Header.Interface
{
    public Med032_Header Header;

    public Med032_Union(int value)
    {
    }
}
"""
        );
}
