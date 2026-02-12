static class Med030Test
{
    public static readonly DiagnosticTest Case =
        new("MED030 when Optional() is used outside an [Inject] context", Run);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED030",
            source: """
using Medicine;

static class Med030_OptionalOutsideInject
{
    public static void Repro()
        => _ = "value".Optional();
}
"""
        );
}
