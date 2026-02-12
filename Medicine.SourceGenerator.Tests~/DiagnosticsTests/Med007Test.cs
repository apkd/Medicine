static class Med007Test
{
    public static readonly DiagnosticTest Case =
        new("MED007 when GetComponent<T>() in Repro() can be cached", Run);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED007",
            source: """
using UnityEngine;

sealed partial class Med007_CanCacheGetComponent : MonoBehaviour
{
    void Awake() { }

    void Repro()
    {
        var value = GetComponent<Transform>();
        _ = value;
    }
}
"""
        );
}
