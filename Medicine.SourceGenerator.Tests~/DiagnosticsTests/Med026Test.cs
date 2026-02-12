static class Med026Test
{
    public static readonly DiagnosticTest Case =
        new("MED026 when a Unity object null check uses == null", Run);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED026",
            source: """
using UnityEngine;

static class Med026_UseIsNull
{
    public static bool Repro(GameObject value)
        => value == null;
}
"""
        );
}
