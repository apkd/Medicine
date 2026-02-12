static class Med016Test
{
    public static readonly DiagnosticTest Case =
        new("MED016 when Lazy.From(...) is called with null", Run);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED016",
            source: """
using Medicine;

static class Med016_InvalidLazyFromArgument
{
    public static void Repro()
        => _ = Lazy.From(null);
}
"""
        );
}
