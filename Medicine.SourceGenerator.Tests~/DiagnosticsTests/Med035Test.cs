static class Med035Test
{
    public static readonly DiagnosticTest Case =
        new("MED035 when tracked enumeration mutates enabled state", Run);

    public static readonly DiagnosticTest DestroyCase =
        new("MED035 when tracked enumeration calls Object.Destroy", RunDestroy);

    public static readonly DiagnosticTest DestroyImmediateCase =
        new("MED035 when tracked enumeration calls Object.DestroyImmediate", RunDestroyImmediate);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED035",
            source: """
using Medicine;
using UnityEngine;

[Track]
sealed partial class Enemy : MonoBehaviour { }

sealed class Med035_MutationDuringEnumeration
{
    void Tick()
    {
        foreach (var enemy in Find.Instances<Enemy>())
            enemy.enabled = false;
    }
}
"""
        );

    static void RunDestroy()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED035",
            source: """
using Medicine;
using UnityEngine;

[Track]
sealed partial class Enemy : MonoBehaviour { }

sealed class Med035_DestroyDuringEnumeration
{
    void Tick()
    {
        foreach (var enemy in Find.Instances<Enemy>())
            Object.Destroy(enemy);
    }
}
"""
        );

    static void RunDestroyImmediate()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED035",
            source: """
using Medicine;
using UnityEngine;

[Track]
sealed partial class Enemy : MonoBehaviour { }

sealed class Med035_DestroyImmediateDuringEnumeration
{
    void Tick()
    {
        foreach (var enemy in Find.Instances<Enemy>())
            Object.DestroyImmediate(enemy);
    }
}
"""
        );
}
