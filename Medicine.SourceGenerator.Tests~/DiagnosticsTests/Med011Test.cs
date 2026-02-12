static class Med011Test
{
    public static readonly DiagnosticTest Case =
        new("MED011 when Object.FindObjectsOfType<T>() is used directly", Run);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED011",
            source: """
using UnityEngine;
using Object = UnityEngine.Object;

static class Med011_FindObjectsOfTypeUsage
{
    public static void Repro()
        => _ = Object.FindObjectsOfType<Transform>();
}
"""
        );
}
