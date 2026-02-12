static class Med008Test
{
    public static readonly DiagnosticTest Case =
        new("MED008 when GetComponent<T>() bypasses an existing injected cache", Run);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED008",
            source: """
using Medicine;
using UnityEngine;

sealed partial class Med008_ShouldUseExistingCache : MonoBehaviour
{
    Transform? cached;

    [Inject]
    void Inject()
    {
        cached = GetComponent<Transform>();
    }

    void Repro()
    {
        var value = GetComponent<Transform>();
        _ = value;
    }
}
"""
        );
}
