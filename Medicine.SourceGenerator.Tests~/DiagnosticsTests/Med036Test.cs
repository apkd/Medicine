static class Med036Test
{
    public static readonly DiagnosticTest TrackCase =
        new("MED036 when [Track] class is missing partial", RunTrackCase);

    public static readonly DiagnosticTest SingletonCase =
        new("MED036 when [Singleton] class is missing partial", RunSingletonCase);

    public static readonly DiagnosticTest InjectCase =
        new("MED036 when class with an [Inject] method is missing partial", RunInjectCase);

    static void RunTrackCase()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED036",
            source: """
using Medicine;
using UnityEngine;

[Track]
sealed class Med036_MissingPartialTrack : MonoBehaviour
{
}
"""
        );

    static void RunSingletonCase()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED036",
            source: """
using Medicine;
using UnityEngine;

[Singleton]
sealed class Med036_MissingPartialSingleton : MonoBehaviour
{
}
"""
        );

    static void RunInjectCase()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED036",
            source: """
using Medicine;
using UnityEngine;

sealed class Med036_MissingPartialInject : MonoBehaviour
{
    [Inject]
    void Setup()
    {
        _ = 0;
    }
}
"""
        );
}
