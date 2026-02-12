static class Med005Test
{
    public static readonly DiagnosticTest Case =
        new("MED005 when a [Track] interface implementation is missing [Track]", Run);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED005",
            source: """
using Medicine;
using UnityEngine;

[Track]
interface Med005_TrackedInterface { }

sealed class Med005_ImplementationWithoutTrack : MonoBehaviour, Med005_TrackedInterface { }
"""
        );
}
