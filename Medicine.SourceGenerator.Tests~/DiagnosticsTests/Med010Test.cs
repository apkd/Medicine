static class Med010Test
{
    public static readonly DiagnosticTest Case =
        new("MED010 when GetComponents<T>() is enumerated in an [Inject] method", Run);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED010",
            source: """
using Medicine;
using UnityEngine;

sealed partial class Med010_EnumeratingGetComponents : MonoBehaviour
{
    [Inject]
    void Inject()
    {
        foreach (var value in GetComponents<Transform>())
            _ = value;
    }
}
"""
        );
}
