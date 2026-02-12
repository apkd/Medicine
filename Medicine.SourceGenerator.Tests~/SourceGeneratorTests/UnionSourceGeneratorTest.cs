using Microsoft.CodeAnalysis;

static class UnionSourceGeneratorTest
{
    public static readonly DiagnosticTest Case =
        new("Union generator adds source for [UnionHeader] and [Union]", Run);

    public static readonly DiagnosticTest NoDerivedHeaderCase =
        new("Union generator supports [UnionHeader] without derived [Union] structs", RunNoDerivedHeader);

    public static readonly DiagnosticTest HeaderFieldForwardingCase =
        new("Union generator forwards header fields to [Union] structs", RunHeaderFieldForwarding);

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
    public int Version;
    public bool Enabled;
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
}
