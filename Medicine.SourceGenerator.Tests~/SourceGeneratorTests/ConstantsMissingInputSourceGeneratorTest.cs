static class ConstantsMissingInputSourceGeneratorTest
{
    public static readonly DiagnosticTest Case =
        new("Constants generator emits MED018 when TagManager input is missing", Run);

    static void Run()
        => SourceGeneratorTester.AssertEmittedDiagnostic(
            diagnosticId: "MED018",
            source: """
using Medicine;

[assembly: GenerateUnityConstants]
""",
            generator: new ConstantsSourceGenerator()
        );
}
