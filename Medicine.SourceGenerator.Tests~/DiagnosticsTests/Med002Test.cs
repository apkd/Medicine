static class Med002Test
{
    public static readonly DiagnosticTest Case =
        new("MED002 when Find.Instances<T>() targets a type without [Track]", Run);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED002",
            source: """
using Medicine;

sealed class Med002_MissingTrackUsage
{
    sealed class NotTracked { }

    public void Repro()
        => _ = Find.Instances<NotTracked>();
}
"""
        );
}
