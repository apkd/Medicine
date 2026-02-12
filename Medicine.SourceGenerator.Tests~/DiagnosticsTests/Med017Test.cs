static class Med017Test
{
    public static readonly DiagnosticTest Case =
        new("MED017 when IInstanceIndex is implemented without [Track]", Run);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED017",
            source: """
using Medicine;

sealed class Med017_InstanceIndexWithoutTrack : IInstanceIndex
{
    public int InstanceIndex { get; set; }
}
"""
        );
}
