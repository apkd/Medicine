using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

static class UnmanagedAccessSourceGeneratorTest
{
    public static readonly DiagnosticTest Case =
        new("UnmanagedAccess generator adds more source when [UnmanagedAccess] is used", Run);

    public static readonly DiagnosticTest CacheEnabledStateInheritanceCase =
        new("UnmanagedAccess generator handles cacheEnabledState inheritance", RunCacheEnabledStateInheritance);

    public static readonly DiagnosticTest DirectIInstanceIndexCase =
        new("UnmanagedAccess generator handles direct IInstanceIndex", RunDirectIInstanceIndex);

    public static readonly DiagnosticTest ImplicitIInstanceIndexCase =
        new("UnmanagedAccess generator handles tracked types without direct IInstanceIndex", RunImplicitIInstanceIndex);

    public static readonly DiagnosticTest RangeIndexerCase =
        new("UnmanagedAccess generator emits AccessArray range indexers", RunRangeIndexerContract);

    public static readonly DiagnosticTest ProjectionCase =
        new("UnmanagedAccess generator projects nested access types and arrays", RunProjectionContract);

    static void Run()
        => SourceGeneratorTester.AssertGeneratesMoreSourcesThanBaseline(
            baselineSource: """
using UnityEngine;

sealed partial class UnmanagedAccessGenerationContractComponent : MonoBehaviour
{
    int counter;
}
""",
            attributedSource: """
using Medicine;
using UnityEngine;

[UnmanagedAccess]
sealed partial class UnmanagedAccessGenerationContractComponent : MonoBehaviour
{
    int counter;
}
""",
            generator: new UnmanagedAccessSourceGenerator()
        );

    static void RunCacheEnabledStateInheritance()
        => AssertNoGeneratorException(
            """
using Medicine;
using UnityEngine;

[Track(cacheEnabledState: true)]
[UnmanagedAccess]
partial class UnmanagedAccessCacheEnabledBase : MonoBehaviour
{
    int baseValue;
}

[Track(cacheEnabledState: true)]
[UnmanagedAccess]
partial class UnmanagedAccessCacheEnabledDerived : UnmanagedAccessCacheEnabledBase
{
    int derivedValue;
}
"""
        );

    static void RunDirectIInstanceIndex()
        => AssertNoGeneratorException(
            """
using Medicine;
using UnityEngine;

[Track]
[UnmanagedAccess]
partial class UnmanagedAccessDirectInstanceIndexComponent : MonoBehaviour, IInstanceIndex
{
    int IInstanceIndex.InstanceIndex { get; set; }
}
"""
        );

    static void RunImplicitIInstanceIndex()
    {
        var compilation = RoslynHarness.CreateCompilation(
            Stubs.Core,
            """
using Medicine;
using UnityEngine;

[Track]
[UnmanagedAccess]
partial class UnmanagedAccessImplicitInstanceIndexComponent : MonoBehaviour
{
    int counter;
}
"""
        );

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new UnmanagedAccessSourceGenerator().AsSourceGenerator()],
            parseOptions: CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersion.Preview)
                .WithPreprocessorSymbols("MEDICINE_EXTENSIONS_LIB")
        );

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var run = driver.GetRunResult();

        RoslynHarness.AssertDoesNotContainDiagnostic(
            diagnostics: run.Diagnostics.ToArray(),
            id: "MED911",
            because: "tracked unmanaged-access generation should not throw without direct IInstanceIndex"
        );

        int generatedFieldReferenceCount = 0;
        foreach (var result in run.Results)
        foreach (var generatedSource in result.GeneratedSources)
        {
            var text = generatedSource.SourceText.ToString();
            generatedFieldReferenceCount += CountOccurrences(text, "MedicineInternalInstanceIndex");
        }

        if (generatedFieldReferenceCount > 0)
            return;

        throw new InvalidOperationException(
            "Expected tracked UnmanagedAccess generation to include the internal instance-index field."
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

    static void RunRangeIndexerContract()
    {
        var compilation = RoslynHarness.CreateCompilation(
            Stubs.Core,
            """
using Medicine;
using UnityEngine;

[Track]
[UnmanagedAccess]
sealed partial class UnmanagedAccessRangeContractComponent : MonoBehaviour
{
    int counter;
}
"""
        );

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new UnmanagedAccessSourceGenerator().AsSourceGenerator()],
            parseOptions: CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersion.Preview)
                .WithPreprocessorSymbols("MEDICINE_EXTENSIONS_LIB")
        );

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var run = driver.GetRunResult();

        RoslynHarness.AssertDoesNotContainDiagnostic(
            diagnostics: run.Diagnostics.ToArray(),
            id: "MED911",
            because: "range indexer codegen should not throw in generator"
        );

        int rwRangeIndexerCount = 0;
        int roRangeIndexerCount = 0;
        foreach (var result in run.Results)
        foreach (var generatedSource in result.GeneratedSources)
        {
            var text = generatedSource.SourceText.ToString();
            rwRangeIndexerCount += CountOccurrences(text, "public AccessArray this[global::System.Range range]");
            roRangeIndexerCount += CountOccurrences(text, "public ReadOnly this[global::System.Range range]");
        }

        if (rwRangeIndexerCount is 1 && roRangeIndexerCount is 1)
            return;

        throw new InvalidOperationException(
            "Expected exactly one RW and one RO range indexer in generated AccessArray wrappers." + Environment.NewLine +
            $"Actual RW count: {rwRangeIndexerCount}, RO count: {roRangeIndexerCount}."
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

    static void RunProjectionContract()
    {
        var compilation = RoslynHarness.CreateCompilation(
            Stubs.Core,
            """
using System;
using Medicine;

[UnmanagedAccess]
partial class Inner
{
    public int Value;
}

[UnmanagedAccess]
partial class Outer
{
    public Inner Child;
    public int[] Values;
    public Inner[] Children;
    public string[] Names;
    public Inner AutoChild { get; set; } = new();
    public int[] AutoValues { get; set; } = Array.Empty<int>();
    public Inner[] AutoChildren { get; set; } = Array.Empty<Inner>();
}
"""
        );

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new UnmanagedAccessSourceGenerator().AsSourceGenerator()],
            parseOptions: CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersion.Preview)
                .WithPreprocessorSymbols("MEDICINE_EXTENSIONS_LIB")
        );

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var run = driver.GetRunResult();

        RoslynHarness.AssertDoesNotContainDiagnostic(
            diagnostics: run.Diagnostics.ToArray(),
            id: "MED911",
            because: "projection codegen should not throw in generator"
        );

        var generatedText = string.Join(
            Environment.NewLine,
            run.Results
                .SelectMany(static x => x.GeneratedSources)
                .Select(static x => x.SourceText.ToString())
        );

        AssertContains("public global::Inner.Unmanaged.AccessRW Child");
        AssertContains("public global::Unity.Collections.NativeArray<int> Values");
        AssertContains("public global::Inner.Unmanaged.AccessArray Children");
        AssertContainsAny(
            "public ref Medicine.UnmanagedRef<global::System.String[]> Names",
            "public ref Medicine.UnmanagedRef<string[]> Names"
        );
        AssertContains("public global::Inner.Unmanaged.AccessRW AutoChild");
        AssertContains("public global::Unity.Collections.NativeArray<int> AutoValues");
        AssertContains("public global::Inner.Unmanaged.AccessArray AutoChildren");

        AssertContains("public global::Inner.Unmanaged.AccessRO Child");
        AssertContains("public global::Unity.Collections.NativeArray<int>.ReadOnly Values");
        AssertContains("public global::Inner.Unmanaged.AccessArray.ReadOnly Children");
        AssertContains("public global::Inner.Unmanaged.AccessRO AutoChild");
        AssertContains("public global::Unity.Collections.NativeArray<int>.ReadOnly AutoValues");
        AssertContains("public global::Inner.Unmanaged.AccessArray.ReadOnly AutoChildren");

        static void ThrowMissing(string expected)
            => throw new InvalidOperationException($"Expected generated source to contain: {expected}");

        static void ThrowMissingAny(string first, string second)
            => throw new InvalidOperationException($"Expected generated source to contain one of: {first} || {second}");

        void AssertContains(string expected)
        {
            if (generatedText.Contains(expected, StringComparison.Ordinal))
                return;

            ThrowMissing(expected);
        }

        void AssertContainsAny(string first, string second)
        {
            if (generatedText.Contains(first, StringComparison.Ordinal) || generatedText.Contains(second, StringComparison.Ordinal))
                return;

            ThrowMissingAny(first, second);
        }
    }

    static void AssertNoGeneratorException(string source)
    {
        var run = RoslynHarness.RunGenerators(
            RoslynHarness.CreateCompilation(Stubs.Core, source),
            new UnmanagedAccessSourceGenerator()
        );

        RoslynHarness.AssertDoesNotContainDiagnostic(
            diagnostics: run.GeneratorDiagnostics,
            id: "MED911",
            because: "source generation should not throw"
        );

        if (run.GeneratedSourceCount > 0)
            return;

        throw new InvalidOperationException("Expected generator to emit at least one source file.");
    }
}
