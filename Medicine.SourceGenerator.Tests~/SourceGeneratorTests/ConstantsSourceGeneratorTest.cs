static class ConstantsSourceGeneratorTest
{
    public static readonly DiagnosticTest Case =
        new("Constants generator adds source when [GenerateUnityConstants] has TagManager input", Run);

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
}
