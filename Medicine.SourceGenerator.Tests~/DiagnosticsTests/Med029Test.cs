static class Med029Test
{
    public static readonly DiagnosticTest Case =
        new("MED029 when [Track(manual: true)] derives from an auto-tracked base", Run);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED029",
            source: """
using Medicine;
using UnityEngine;

[Track]
partial class Med029_TrackAutoBase : MonoBehaviour { }

[Track(manual: true)]
sealed partial class Med029_TrackManualDerivedFromAuto : Med029_TrackAutoBase { }
"""
        );
}
