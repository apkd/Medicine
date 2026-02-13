static class Med017Test
{
    public static readonly DiagnosticTest Case =
        new("MED017 when IInstanceIndex is implemented without [Track]", Run);

    public static readonly DiagnosticTest InheritedInterfaceCase =
        new("MED017 is not emitted for inherited IInstanceIndex", RunInheritedInterfaceCase);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED017",
            source: """
using Medicine;

sealed class Med017_InstanceIndexWithoutTrack : IInstanceIndex
{
    public int InstanceIndex { get; set; }
}
"""
        );

    static void RunInheritedInterfaceCase()
    {
        const string source = """
using Medicine;
using UnityEngine;

[Track]
sealed partial class Med017_TrackedBase : MonoBehaviour, IInstanceIndex
{
    int IInstanceIndex.InstanceIndex { get; set; }
}

sealed class Med017_InheritsTrackedBase : Med017_TrackedBase { }
""";

        var compilation = RoslynHarness.CreateCompilation(Stubs.Core, source);
        var analyzerDiagnostics = RoslynHarness.RunAnalyzers(compilation, [new InstanceIndexAnalyzer()]);
        var generatorRun = RoslynHarness.RunGenerators(compilation, new InjectSourceGenerator(), new TrackSourceGenerator());

        var diagnostics = analyzerDiagnostics
            .Concat(generatorRun.GeneratorDiagnostics)
            .Concat(generatorRun.CompilationDiagnostics.Where(x => x.Id.StartsWith("MED", StringComparison.Ordinal)))
            .ToArray();

        RoslynHarness.AssertDoesNotContainDiagnostic(
            diagnostics: diagnostics,
            id: "MED017",
            because: "MED017 should apply only to direct IInstanceIndex implementations"
        );
    }
}
