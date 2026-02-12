static class Med028Test
{
    public static readonly DiagnosticTest Case =
        new("MED028 when singleton strategy combines Replace and KeepExisting", Run);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED028",
            source: """
using Medicine;
using UnityEngine;

[Singleton(strategy: SingletonAttribute.Strategy.Replace | SingletonAttribute.Strategy.KeepExisting)]
sealed partial class Med028_ConflictingSingletonStrategy : MonoBehaviour { }
"""
        );
}
