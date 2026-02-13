using Microsoft.CodeAnalysis;

static class UnionNestedSourceGeneratorTest
{
    public static readonly DiagnosticTest Case =
        new("Union generator supports nested [UnionHeader] families", Run);

    public static readonly DiagnosticTest HeaderFieldForwardingCase =
        new("Union generator forwards nested header fields to [Union] structs", RunHeaderFieldForwarding);

    static void Run()
        => SourceGeneratorTester.AssertGeneratesSource(
            source: """
using Medicine;

[UnionHeader]
public partial struct BaseEntityState
{
    public interface IDerived
    {
        bool CanAbort(int unit);
    }

    public TypeIDs TypeID;
}

[UnionHeader]
public partial struct WeaponState
{
    public interface IDerivedWeapon : BaseEntityState.IDerived
    {
        bool CanBeginAttack(int unit);
    }

    public BaseEntityState Header;
}

[Union(1)]
public partial struct ItemState : BaseEntityState.IDerived
{
    public BaseEntityState Header;
    public bool CanAbort(int unit) => false;
}

[Union(2)]
public partial struct RangeWeaponState : WeaponState.IDerivedWeapon
{
    public WeaponState Header;
    public bool CanAbort(int unit) => false;
    public bool CanBeginAttack(int unit) => false;
}
""",
            generator: new UnionStructSourceGenerator()
        );

    static void RunHeaderFieldForwarding()
    {
        var compilation = RoslynHarness.CreateCompilation(
            Stubs.Core,
            """
using Medicine;

[UnionHeader]
public partial struct RootState
{
    public interface IDerived
    {
        bool IsReady(int value);
    }

    public TypeIDs TypeID;
    public int RootCounter;
}

[UnionHeader]
public partial struct ChildState
{
    public interface IDerivedChild : RootState.IDerived
    {
        bool CanRun(int value);
    }

    public RootState Header;
    public int Counter;
}

[Union(1)]
public partial struct ChildAState : ChildState.IDerivedChild
{
    public ChildState Header;
    public bool IsReady(int value) => true;
    public bool CanRun(int value) => true;
}

static class Usage
{
    public static int Run()
    {
        var child = new ChildAState();
        child.Counter = 9;
        ref var childHeader = ref child.Header;
        ref var root = ref childHeader.AsRootState();
        root.RootCounter = 10;

        ref var childWrapper = ref child.Wrap();
        ref var rootWrapper = ref childWrapper.AsRootState();
        rootWrapper.RootCounter = 20;

        return child.Counter + root.RootCounter + rootWrapper.RootCounter;
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
            because: "nested header-field forwarding should not throw in generator"
        );

        var missingForwardingMembers = run.CompilationDiagnostics
            .Where(x => x.Severity == DiagnosticSeverity.Error)
            .Where(x => x.Id is "CS1061")
            .Where(x =>
                x.GetMessage().Contains("Counter", StringComparison.Ordinal) ||
                x.GetMessage().Contains("AsRootState", StringComparison.Ordinal) ||
                x.GetMessage().Contains("Wrap", StringComparison.Ordinal)
            )
            .ToArray();

        if (missingForwardingMembers.Length == 0)
            return;

        throw new InvalidOperationException(
            "Expected nested [Union] structs to expose nested [UnionHeader] fields via generated forwarding properties." + Environment.NewLine +
            $"Actual diagnostics:{Environment.NewLine}{RoslynHarness.FormatDiagnostics(missingForwardingMembers)}"
        );
    }
}
