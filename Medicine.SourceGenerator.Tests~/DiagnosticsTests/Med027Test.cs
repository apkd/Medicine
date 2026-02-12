static class Med027Test
{
    public static readonly DiagnosticTest Case =
        new("MED027 when a Unity object null check uses != null", Run);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED027",
            source: """
using UnityEngine;

static class Med027_UseIsNotNull
{
    public static bool Repro(GameObject value)
        => value != null;
}
"""
        );
}
