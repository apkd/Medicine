static class Med003Test
{
    public static readonly DiagnosticTest Case =
        new("MED003 when Find.Singleton<T>() is used with a UnityEngine type", Run);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED003",
            source: """
using Medicine;
using UnityEngine;

sealed class Med003_UnityTypeInFind
{
    public void Repro()
        => _ = Find.Singleton<Transform>();
}
"""
        );
}
