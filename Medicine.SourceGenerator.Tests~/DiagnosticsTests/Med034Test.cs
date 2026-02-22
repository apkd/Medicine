static class Med034Test
{
    public static readonly DiagnosticTest Case =
        new("MED034 when [Inject] cleanup is never called from teardown", Run);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED034",
            source: """
using System;
using Medicine;
using UnityEngine;

sealed partial class Med034_MissingCleanup : MonoBehaviour
{
    [Inject]
    void Inject()
    {
        Resource = new Resource().Cleanup(static x => x.Dispose());
    }

    Resource Resource;

    sealed class Resource : IDisposable
    {
        public void Dispose() { }
    }
}
"""
        );
}
