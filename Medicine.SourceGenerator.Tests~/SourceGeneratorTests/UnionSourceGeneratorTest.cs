using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

static class UnionSourceGeneratorTest
{
    public static readonly DiagnosticTest Case =
        new("Union generator adds source for [UnionHeader] and [Union]", Run);

    public static readonly DiagnosticTest NoDerivedHeaderCase =
        new("Union generator supports [UnionHeader] without derived [Union] structs", RunNoDerivedHeader);

    public static readonly DiagnosticTest HeaderFieldForwardingCase =
        new("Union generator forwards header fields to [Union] structs", RunHeaderFieldForwarding);

    public static readonly DiagnosticTest HeaderPropertyAccessorForwardingCase =
        new("Union generator forwards only accessible header property accessors", RunHeaderPropertyAccessorForwarding);

    public static readonly DiagnosticTest WrapperCase =
        new("Union generator emits Wrapper for non-generic headers", RunWrapper);

    public static readonly DiagnosticTest SerializedWrapperCase =
        new("Union generator emits Unity-serializable Wrapper storage", RunSerializedWrapper);

    public static readonly DiagnosticTest GenericWrapperSkipCase =
        new("Union generator does not emit Wrapper for generic headers", RunGenericWrapperSkip);

    static void Run()
        => SourceGeneratorTester.AssertGeneratesSource(
            source: """
using Medicine;

public enum ContractTypeIDs : byte
{
    None = 0,
    A = 1,
}

[UnionHeader]
public partial struct ContractUnion
{
    public interface Interface
    {
        int Value { get; }
    }

    public ContractTypeIDs TypeID;
}

[Union(1)]
public partial struct ContractUnionA : ContractUnion.Interface
{
    public ContractUnion Header;
    public int Value => 42;
}
""",
            generator: new UnionStructSourceGenerator()
        );

    static void RunNoDerivedHeader()
    {
        var compilation = RoslynHarness.CreateCompilation(
            Stubs.Core,
            """
using Medicine;

[UnionHeader]
public partial struct ContractUnion
{
    public interface Interface
    {
        int Value();
    }

    public TypeIDs TypeID;
}
"""
        );

        var run = RoslynHarness.RunGenerators(
            compilation,
            [new UnionStructSourceGenerator()],
            []
        );

        RoslynHarness.AssertDoesNotContainDiagnostic(
            diagnostics: run.GeneratorDiagnostics,
            id: "MED911",
            because: "the no-derived union-header path should not throw in the generator"
        );

        if (run.GeneratedSourceCount == 0)
            throw new InvalidOperationException("Expected generator to emit source for [UnionHeader] without derived [Union] structs.");

        var typeIdResolutionErrors = run.CompilationDiagnostics
            .Where(x => x.Severity == DiagnosticSeverity.Error)
            .Where(x => x.Id is "CS0246" && x.GetMessage().Contains("TypeIDs"))
            .ToArray();

        if (typeIdResolutionErrors.Length == 0)
            return;

        throw new InvalidOperationException(
            "Expected [UnionHeader] without derived [Union] structs to resolve generated TypeIDs." + Environment.NewLine +
            $"Actual diagnostics:{Environment.NewLine}{RoslynHarness.FormatDiagnostics(typeIdResolutionErrors)}"
        );
    }

    static void RunHeaderFieldForwarding()
    {
        var compilation = RoslynHarness.CreateCompilation(
            Stubs.Core,
            """
using Medicine;

[UnionHeader]
public partial struct ContractUnion
{
    public interface Interface
    {
        int Value { get; }
    }

    public TypeIDs TypeID;
    public int Version { get; set; }
    public bool Enabled { get; set; }
}

[Union(1)]
public partial struct ContractUnionA : ContractUnion.Interface
{
    public ContractUnion Header;
    public int Value => 42;
}

static class Usage
{
    public static int Run()
    {
        var item = new ContractUnionA();
        item.Version = 7;
        item.Enabled = true;
        return item.Version + (item.Enabled ? 1 : 0);
    }
}
"""
        );

        var run = RoslynHarness.RunGenerators(
            compilation,
            [new UnionStructSourceGenerator()],
            []
        );

        RoslynHarness.AssertDoesNotContainDiagnostic(
            diagnostics: run.GeneratorDiagnostics,
            id: "MED911",
            because: "header-field forwarding should not throw in generator"
        );

        var missingForwardingMembers = run.CompilationDiagnostics
            .Where(x => x.Severity == DiagnosticSeverity.Error)
            .Where(x => x.Id is "CS1061")
            .Where(x =>
                x.GetMessage().Contains("Version", StringComparison.Ordinal) ||
                x.GetMessage().Contains("Enabled", StringComparison.Ordinal)
            )
            .ToArray();

        if (missingForwardingMembers.Length == 0)
            return;

        throw new InvalidOperationException(
            "Expected [Union] structs to expose [UnionHeader] fields via generated forwarding properties." + Environment.NewLine +
            $"Actual diagnostics:{Environment.NewLine}{RoslynHarness.FormatDiagnostics(missingForwardingMembers)}"
        );
    }

    static void RunHeaderPropertyAccessorForwarding()
    {
        var compilation = RoslynHarness.CreateCompilation(
            Stubs.Core,
            """
using Medicine;

[UnionHeader]
public partial struct ContractUnion
{
    public interface Interface
    {
        int Value { get; }
    }

    public TypeIDs TypeID;
    public int ReadOnlyForUnion { get; private set; }
    public int WriteOnlyForUnion { private get; set; }

    public void Initialize()
    {
        ReadOnlyForUnion = 11;
        WriteOnlyForUnion = 22;
    }
}

[Union(1)]
public partial struct ContractUnionA : ContractUnion.Interface
{
    public ContractUnion Header;
    public int Value => 42;
}

static class UsagePositive
{
    public static int Run()
    {
        var item = new ContractUnionA();
        item.Header.Initialize();
        item.WriteOnlyForUnion = 33;
        return item.ReadOnlyForUnion;
    }
}

static class UsageNegative
{
    public static int Run()
    {
        var item = new ContractUnionA();
        item.ReadOnlyForUnion = 5;
        return item.WriteOnlyForUnion;
    }
}
"""
        );

        var run = RoslynHarness.RunGenerators(
            compilation,
            [new UnionStructSourceGenerator()],
            []
        );

        RoslynHarness.AssertDoesNotContainDiagnostic(
            diagnostics: run.GeneratorDiagnostics,
            id: "MED911",
            because: "property accessor forwarding should not throw in generator"
        );

        var missingMembers = run.CompilationDiagnostics
            .Where(x => x.Severity == DiagnosticSeverity.Error)
            .Where(x => x.Id is "CS1061")
            .Where(x =>
                x.GetMessage().Contains("ReadOnlyForUnion", StringComparison.Ordinal) ||
                x.GetMessage().Contains("WriteOnlyForUnion", StringComparison.Ordinal)
            )
            .ToArray();

        if (missingMembers.Length > 0)
            throw new InvalidOperationException(
                "Expected accessible property accessors to be forwarded to [Union] structs." + Environment.NewLine +
                $"Actual diagnostics:{Environment.NewLine}{RoslynHarness.FormatDiagnostics(missingMembers)}"
            );

        var expectedInaccessibleAccessorErrors = run.CompilationDiagnostics
            .Where(x => x.Severity == DiagnosticSeverity.Error)
            .Where(x => x.Id is "CS0200" or "CS0154")
            .Where(x =>
                x.GetMessage().Contains("ReadOnlyForUnion", StringComparison.Ordinal) ||
                x.GetMessage().Contains("WriteOnlyForUnion", StringComparison.Ordinal)
            )
            .ToArray();

        if (expectedInaccessibleAccessorErrors.Length == 2)
            return;

        throw new InvalidOperationException(
            "Expected inaccessible accessors to be omitted from forwarded properties." + Environment.NewLine +
            $"Actual diagnostics:{Environment.NewLine}{RoslynHarness.FormatDiagnostics(expectedInaccessibleAccessorErrors)}"
        );
    }

    static void RunWrapper()
    {
        var compilation = RoslynHarness.CreateCompilation(
            Stubs.Core,
            """
using Medicine;

[UnionHeader]
public partial struct ContractUnion
{
    public interface Interface
    {
        int Value { get; }
        int Scale(int amount);
    }

    public TypeIDs TypeID;
    public int Version { get; set; }
}

[Union(1)]
public partial struct ContractUnionA : ContractUnion.Interface
{
    public ContractUnion Header;
    public int Value => 42;
    public int Scale(int amount) => amount * 2;
}

static class Usage
{
    public static int Run()
    {
        var raw = new ContractUnion.Wrapper();
        raw.Version = 7;
        ref var typed = ref raw.AsContractUnionA();
        var variant = new ContractUnionA
        {
            Header =
            {
                TypeID = ContractUnion.TypeIDs.ContractUnionA,
            },
        };
        ref var wrappedVariant = ref variant.Wrap();
        wrappedVariant.Version = 5;
        ref var wrappedHeader = ref variant.Header.Wrap();
        wrappedHeader.Version = 6;

        ContractUnion.Interface wrapper = new ContractUnion.Wrapper
        {
            Header = new ContractUnion
            {
                TypeID = ContractUnion.TypeIDs.ContractUnionA,
            },
        };

        return raw.Version + typed.Scale(2) + wrappedVariant.Version + wrappedHeader.Version + wrapper.Value + wrapper.Scale(3);
    }
}
"""
        );

        var run = RoslynHarness.RunGenerators(
            compilation,
            [new UnionStructSourceGenerator()],
            []
        );

        RoslynHarness.AssertDoesNotContainDiagnostic(
            diagnostics: run.GeneratorDiagnostics,
            id: "MED911",
            because: "wrapper generation should not throw in generator"
        );

        var missingWrapperTypeErrors = run.CompilationDiagnostics
            .Where(x => x.Severity == DiagnosticSeverity.Error)
            .Where(x => x.Id is "CS0426" && x.GetMessage().Contains("Wrapper", StringComparison.Ordinal))
            .ToArray();

        if (missingWrapperTypeErrors.Length > 0)
            throw new InvalidOperationException(
                "Expected generated Wrapper type to exist for non-generic [UnionHeader]." + Environment.NewLine +
                $"Actual diagnostics:{Environment.NewLine}{RoslynHarness.FormatDiagnostics(missingWrapperTypeErrors)}"
            );

        var wrapperInterfaceErrors = run.CompilationDiagnostics
            .Where(x => x.Severity == DiagnosticSeverity.Error)
            .Where(x => x.Id is "CS0535" && x.GetMessage().Contains("Wrapper", StringComparison.Ordinal))
            .ToArray();

        if (wrapperInterfaceErrors.Length == 0)
            return;

        throw new InvalidOperationException(
            "Expected generated Wrapper to implement all interface members." + Environment.NewLine +
            $"Actual diagnostics:{Environment.NewLine}{RoslynHarness.FormatDiagnostics(wrapperInterfaceErrors)}"
        );
    }

    static void RunSerializedWrapper()
    {
        var compilation = RoslynHarness.CreateCompilation(
            Stubs.Core,
            """
using Medicine;

[UnionHeader]
public partial struct CompactUnion
{
    public interface Interface
    {
        byte Value { get; }
    }

    public TypeIDs TypeID;
    public byte Payload;
}

[Union(1)]
public partial struct CompactUnionA : CompactUnion.Interface
{
    public CompactUnion Header;
    public byte Value => Header.Payload;
}
"""
        );

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new UnionStructSourceGenerator().AsSourceGenerator()],
            parseOptions: CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersion.Preview)
                .WithPreprocessorSymbols("MEDICINE_EXTENSIONS_LIB")
        );

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var run = driver.GetRunResult();

        RoslynHarness.AssertDoesNotContainDiagnostic(
            diagnostics: run.Diagnostics.ToArray(),
            id: "MED911",
            because: "serialized wrapper generation should not throw in generator"
        );

        var wrapperSource = run.Results
            .SelectMany(static x => x.GeneratedSources)
            .Select(static x => x.SourceText.ToString())
            .FirstOrDefault(x =>
                x.Contains("public struct Wrapper", StringComparison.Ordinal) &&
                x.Contains("CompactUnion", StringComparison.Ordinal)
            );

        if (wrapperSource is null)
            throw new InvalidOperationException("Expected generator to emit Wrapper source for CompactUnion.");

        RequirePattern(@"\[global::System\.Serializable\]");
        RequirePattern(@"StructLayout\(global::System\.Runtime\.InteropServices\.LayoutKind\.Explicit, Size = 8\)");
        RequirePattern(@"\[global::System\.NonSerialized\]\s+\[global::System\.Runtime\.InteropServices\.FieldOffset\(0\)\][\s\S]*?public global::CompactUnion Header;");
        RequirePattern(@"\[global::UnityEngine\.SerializeField\]\s+\[global::UnityEngine\.HideInInspector\]\s+\[global::System\.Runtime\.InteropServices\.FieldOffset\(0\)\]\s+ulong bytes0000;");

        if (wrapperSource.Contains("serialized0008", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Expected compact wrapper to round up to a single serialized ulong field." + Environment.NewLine +
                wrapperSource
            );

        void RequirePattern(string pattern)
        {
            if (Regex.IsMatch(wrapperSource, pattern, RegexOptions.CultureInvariant))
                return;

            throw new InvalidOperationException(
                $"Expected generated wrapper source to match regex: {pattern}" + Environment.NewLine +
                wrapperSource
            );
        }
    }

    static void RunGenericWrapperSkip()
    {
        var compilation = RoslynHarness.CreateCompilation(
            Stubs.Core,
            """
using Medicine;

[UnionHeader]
public partial struct ContractUnion<T>
{
    public interface Interface
    {
        int Value { get; }
    }

    public TypeIDs TypeID;
}

[Union(1)]
public partial struct ContractUnionA : ContractUnion<int>.Interface
{
    public ContractUnion<int> Header;
    public int Value => 42;
}

static class Usage
{
    public static object Run()
    {
        return new ContractUnion<int>.Wrapper();
    }
}
"""
        );

        var run = RoslynHarness.RunGenerators(
            compilation,
            [new UnionStructSourceGenerator()],
            []
        );

        RoslynHarness.AssertDoesNotContainDiagnostic(
            diagnostics: run.GeneratorDiagnostics,
            id: "MED911",
            because: "generic-wrapper suppression should not throw in generator"
        );

        var missingWrapperDiagnostic = run.CompilationDiagnostics
            .Where(x => x.Severity == DiagnosticSeverity.Error)
            .FirstOrDefault(x => x.Id is "CS0426" && x.GetMessage().Contains("Wrapper", StringComparison.Ordinal));

        if (missingWrapperDiagnostic is not null)
            return;

        throw new InvalidOperationException(
            "Expected generic [UnionHeader] to skip Wrapper emission and fail when Wrapper is referenced." + Environment.NewLine +
            $"Actual diagnostics:{Environment.NewLine}{RoslynHarness.FormatDiagnostics(run.CompilationDiagnostics)}"
        );
    }
}
