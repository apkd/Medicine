static class Med012Test
{
    public static readonly DiagnosticTest Case =
        new("MED012 when [Inject] is applied to a property", Run);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED012",
            source: """
using Medicine;
using UnityEngine;

sealed partial class Med012_LegacyInjectProperty : MonoBehaviour
{
    [Inject]
    public Transform? LegacyProperty { get; set; }
}
"""
        );
}
