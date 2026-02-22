static class Med033Test
{
    public static readonly DiagnosticTest Case =
        new("MED033 when [Track(transformAccessArray: true)] targets a non-MonoBehaviour class", Run);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED033",
            source: """
using Medicine;
using UnityEngine;

[Track(transformAccessArray: true)]
sealed partial class Med033_TrackTransformsOnScriptableObject : ScriptableObject { }
"""
        );
}
