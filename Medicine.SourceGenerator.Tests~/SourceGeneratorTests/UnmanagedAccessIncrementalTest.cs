using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

static class UnmanagedAccessIncrementalTest
{
    public static readonly DiagnosticTest Case =
        new("UnmanagedAccess invalidates semantic dependencies and reuses unchanged output", Run);

    static void Run()
    {
        Check(
            "[Medicine.UnmanagedAccess] partial class Derived : Base { }",
            "partial class Base { int value; }",
            "partial class Base { long value; int added; }"
        );
        Check(
            "[Medicine.UnmanagedAccess] partial class Derived : Base { }",
            "partial class Base { int value; }",
            "partial class Base { }",
            "partial class Base { int other; }"
        );
        Check(
            "[Medicine.UnmanagedAccess] partial class Derived : Middle { }",
            "class Base { int value; }",
            "class Base { long value; }",
            "class Middle : Base { }"
        );
        Check(
            "[Medicine.UnmanagedAccess] partial class Derived : Middle { }",
            "class Middle : First { }",
            "class Middle : Second { }",
            "[Medicine.UnmanagedAccess] partial class First { int first; } [Medicine.UnmanagedAccess] partial class Second { int second; }"
        );
        Check(
            "[Medicine.UnmanagedAccess, Medicine.Track(cacheEnabledState: true)] partial class Derived : Base { }",
            "[Medicine.Track(cacheEnabledState: false)] class Base : UnityEngine.MonoBehaviour { }",
            "[Medicine.Track(cacheEnabledState: true)] class Base : UnityEngine.MonoBehaviour { }"
        );
        Check(
            "[Medicine.UnmanagedAccess] partial class Owner { Value value; }",
            "partial class Value { int value; }",
            "[Medicine.UnmanagedAccess] partial class Value { int value; }"
        );
        Check(
            "[Medicine.UnmanagedAccess] partial class Owner { Value value; Value[] array; System.Collections.Generic.List<Value> list; }",
            "[Medicine.UnmanagedAlias(typeof(int))] class Value { }",
            "[Medicine.UnmanagedAlias(typeof(long))] class Value { }"
        );
        Check(
            "[Medicine.UnmanagedAccess] partial class Owner { Value value; Value[] array; System.Collections.Generic.List<Value> list; }",
            "struct Inner { int value; }",
            "struct Inner { object value; }",
            "struct Value { Inner value; }"
        );
        Check(
            "[Medicine.UnmanagedAccess] partial class Owner { int value; }",
            "partial class Owner { public partial struct Unmanaged { public partial struct AccessRO { public int Value => 0; } } }",
            "partial class Owner { public partial struct Unmanaged { public partial struct AccessRO { public long Value => 0; } } }"
        );
        Check(
            """
            [Medicine.UnmanagedAccess] partial class Owner
            {
                int value;
                public partial struct Unmanaged
                {
                    public partial struct AccessRO
                    {
                        public int Read(int value = Defaults.Value) => value;
                    }
                }
            }
            """,
            "static class Defaults { public const int Value = 1; }",
            "static class Defaults { public const int Value = 2; }"
        );
        Check(
            "[Medicine.UnmanagedAccess] partial class Owner { int value; }",
            "class Unrelated { int value; }",
            "class Unrelated { long value; }",
            expectChange: false
        );
        Check(
            "[Medicine.UnmanagedAccess] partial class Owner : Base { }",
            "class Base { int value; int Compute() => 1; }",
            "class Base { int value; int Compute() => 2; }",
            expectChange: false
        );
    }

    static void Check(string target, string before, string after, string additional = "", bool expectChange = true)
    {
        var compilation = RoslynHarness.CreateCompilation(Stubs.Core, target, before, additional);
        var tree = compilation.SyntaxTrees.ElementAt(2);
        var parseOptions = (CSharpParseOptions)tree.Options;
        var driver = CreateDriver().RunGenerators(compilation);
        var original = Sources(driver);
        var changed = compilation.ReplaceSyntaxTree(
            tree,
            CSharpSyntaxTree.ParseText(after, parseOptions, tree.FilePath)
        );
        driver = driver.RunGenerators(changed);
        var updated = Sources(driver);
        var fresh = Sources(CreateDriver().RunGenerators(changed));

        if (!updated.SequenceEqual(fresh))
            throw new InvalidOperationException($"Reused driver returned stale output after changing: {before} -> {after}");

        if (original.SequenceEqual(updated) == expectChange)
            throw new InvalidOperationException($"Unexpected output change after: {before} -> {after}");

        if (!expectChange)
            AssertCached(driver);

        AssertCached(driver.RunGenerators(changed));

        GeneratorDriver CreateDriver()
            => CSharpGeneratorDriver.Create(
                generators: [new UnmanagedAccessSourceGenerator().AsSourceGenerator()],
                parseOptions: parseOptions,
                driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true)
            );
    }

    static string[] Sources(GeneratorDriver driver)
    {
        var run = driver.GetRunResult();
        if (run.Diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error))
            throw new InvalidOperationException(string.Join(Environment.NewLine, run.Diagnostics));

        return run.Results.SelectMany(x => x.GeneratedSources)
            .OrderBy(x => x.HintName, StringComparer.Ordinal)
            .Select(x => x.HintName + "\n" + x.SourceText)
            .ToArray();
    }

    static void AssertCached(GeneratorDriver driver)
    {
        var outputs = driver.GetRunResult().Results
            .SelectMany(x => x.TrackedOutputSteps.Values)
            .SelectMany(x => x)
            .SelectMany(x => x.Outputs)
            .ToArray();
        if (outputs.Length == 0 || outputs.Any(x => x.Reason != IncrementalStepRunReason.Cached))
            throw new InvalidOperationException("Unchanged semantic inputs should reuse generated output.");
    }
}
