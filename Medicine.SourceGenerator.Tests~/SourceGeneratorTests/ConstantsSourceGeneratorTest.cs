using Microsoft.CodeAnalysis;

static class ConstantsSourceGeneratorTest
{
    public static readonly DiagnosticTest Case =
        new("Constants generator adds source when [GenerateUnityConstants] has TagManager input", Run);

    public static readonly DiagnosticTest CustomNamespaceAndClassCase =
        new("Constants generator respects custom namespace/class parameters", RunCustomNamespaceAndClass);

    static void Run()
    {
        var tagManager = RoslynHarness.AdditionalText(
            path: "ProjectSettings/TagManager.asset",
            content: """
tags:
  - Player
layers:
  - Default
"""
        );

        SourceGeneratorTester.AssertGeneratesSource(
            source: """
using Medicine;

[assembly: GenerateUnityConstants]
""",
            generator: new ConstantsSourceGenerator(),
            additionalTexts: [tagManager]
        );
    }

    static void RunCustomNamespaceAndClass()
    {
        var tagManager = RoslynHarness.AdditionalText(
            path: "ProjectSettings/TagManager.asset",
            content: """
tags:
  - Player
layers:
  - Default
"""
        );

        var run = RoslynHarness.RunGenerators(
            compilation: RoslynHarness.CreateCompilation(
                Stubs.Core,
                """
using Medicine;

[assembly: GenerateUnityConstants(@namespace: "Project.Generated", @class: "UnityConstants")]

namespace Medicine
{
    public static class Constants { }
    public static class UnityConstants { }
}

namespace Project.Generated
{
    public static class Constants { }
}
"""
            ),
            incrementalGenerators: [new ConstantsSourceGenerator()],
            additionalTexts: [tagManager]
        );

        RoslynHarness.AssertDoesNotContainDiagnostic(
            diagnostics: run.GeneratorDiagnostics,
            id: "MED911",
            because: "happy-path source generator contracts should not rely on exception diagnostics"
        );

        if (run.GeneratedSourceCount is 0)
            throw new InvalidOperationException("Expected generator to add at least one source file.");

        var errors = run.CompilationDiagnostics
            .Where(x => x.Severity is DiagnosticSeverity.Error)
            .ToArray();

        if (errors.Length is 0)
            return;

        throw new InvalidOperationException(
            "Expected generated source to compile without errors." + Environment.NewLine +
            $"Actual diagnostics:{Environment.NewLine}{RoslynHarness.FormatDiagnostics(errors)}"
        );
    }
}
