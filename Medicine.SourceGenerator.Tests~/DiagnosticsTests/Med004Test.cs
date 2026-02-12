static class Med004Test
{
    public static readonly DiagnosticTest Case =
        new("MED004 when a type combines [Singleton] and [Track]", Run);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED004",
            source: """
using Medicine;
using UnityEngine;

[Singleton, Track]
sealed partial class Med004_BothSingletonAndTrack : MonoBehaviour { }
"""
        );
}
