static class Med001Test
{
    public static readonly DiagnosticTest Case =
        new("MED001 when Find.Singleton<T>() targets a type without [Singleton]", Run);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED001",
            source: """
using Medicine;

sealed class Med001_MissingSingletonUsage
{
    sealed class NotSingleton { }

    public void Repro()
        => _ = Find.Singleton<NotSingleton>();
}
"""
        );
}
