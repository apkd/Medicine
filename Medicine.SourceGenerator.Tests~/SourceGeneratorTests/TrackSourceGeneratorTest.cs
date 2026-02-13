using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

static class TrackSourceGeneratorTest
{
    public static readonly DiagnosticTest Case =
        new("Track generator adds more source when [Track] is used", Run);

    public static readonly DiagnosticTest CacheEnabledStateInheritanceCase =
        new("Track generator suppresses duplicated cacheEnabledState in inheritance", RunCacheEnabledStateInheritance);

    static void Run()
        => SourceGeneratorTester.AssertGeneratesMoreSourcesThanBaseline(
            baselineSource: """
using UnityEngine;

sealed partial class TrackGenerationContractComponent : MonoBehaviour { }
""",
            attributedSource: """
using Medicine;
using UnityEngine;

[Track]
sealed partial class TrackGenerationContractComponent : MonoBehaviour { }
""",
            generator: new TrackSourceGenerator()
        );

    static void RunCacheEnabledStateInheritance()
    {
        var compilation = RoslynHarness.CreateCompilation(
            Stubs.Core,
            """
using Medicine;
using UnityEngine;

[Track(cacheEnabledState: true)]
partial class CacheEnabledBase : MonoBehaviour { }

[Track(cacheEnabledState: true)]
partial class CacheEnabledDerived : CacheEnabledBase { }
"""
        );

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new TrackSourceGenerator().AsSourceGenerator()],
            parseOptions: CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersion.Preview)
                .WithPreprocessorSymbols("MEDICINE_EXTENSIONS_LIB")
        );

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var run = driver.GetRunResult();

        RoslynHarness.AssertDoesNotContainDiagnostic(
            diagnostics: run.Diagnostics.ToArray(),
            id: "MED911",
            because: "cache-enabled inheritance should not throw in generator"
        );

        int propertyCount = 0;
        int assignTrueCount = 0;
        int assignFalseCount = 0;
        foreach (var result in run.Results)
        foreach (var generatedSource in result.GeneratedSources)
        {
            var text = generatedSource.SourceText.ToString();
            propertyCount += CountOccurrences(text, "public new bool enabled");
            assignTrueCount += CountOccurrences(text, "MedicineInternalCachedEnabledState = true");
            assignFalseCount += CountOccurrences(text, "MedicineInternalCachedEnabledState = false");
        }

        if (propertyCount is 1 && assignTrueCount is 1 && assignFalseCount is 1)
            return;

        throw new InvalidOperationException(
            "Expected exactly one cache-enabled emission across base+derived tracked types." + Environment.NewLine +
            $"Actual property declarations: {propertyCount}, true-assignments: {assignTrueCount}, false-assignments: {assignFalseCount}."
        );

        static int CountOccurrences(string source, string value)
        {
            int count = 0;
            int index = 0;
            while (true)
            {
                index = source.IndexOf(value, index, StringComparison.Ordinal);
                if (index < 0)
                    return count;

                count++;
                index += value.Length;
            }
        }
    }
}
