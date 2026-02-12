static class Med015Test
{
    public static readonly DiagnosticTest Case =
        new("MED015 when a [DisallowReadonly] struct is stored in a readonly field", Run);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED015",
            source: """
using Medicine;

[Medicine.DisallowReadonly]
struct Med015_MutableStruct
{
    public int Value;
}

sealed class Med015_ReadonlyFieldUsage
{
    readonly Med015_MutableStruct value;
}
"""
        );
}
