static class Med006Test
{
    public static readonly DiagnosticTest Case =
        new("MED006 when [Inject] assigns the same member more than once", Run);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED006",
            source: """
using Medicine;
using UnityEngine;

sealed partial class Med006_DuplicateInjectAssignment : MonoBehaviour
{
    [Inject]
    void Inject()
    {
        Duplicate = GetComponent<Transform>();
        Duplicate = GetComponent<Rigidbody>();
    }
}
"""
        );
}
