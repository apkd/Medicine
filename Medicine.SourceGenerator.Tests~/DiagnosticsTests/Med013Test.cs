static class Med013Test
{
    public static readonly DiagnosticTest Case =
        new("MED013 when a [Singleton] type is accessed via Object.FindObjectOfType<T>()", Run);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED013",
            source: """
using Medicine;
using UnityEngine;
using Object = UnityEngine.Object;

[Singleton]
sealed partial class Med013_SingletonTarget : MonoBehaviour { }

static class Med013_UseSingletonDirectAccess
{
    public static void Repro()
        => _ = Object.FindObjectOfType<Med013_SingletonTarget>();
}
"""
        );
}
