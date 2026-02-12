static class Med009Test
{
    public static readonly DiagnosticTest Case =
        new("MED009 when a cacheable GetComponent<T>() call has no Awake()", Run);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED009",
            source: """
using UnityEngine;

sealed partial class Med009_NeedsAwakeForCaching : MonoBehaviour
{
    void Repro()
    {
        var value = GetComponent<Transform>();
        _ = value;
    }
}
"""
        );
}
