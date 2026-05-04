using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

static class UnmanagedInvokeSourceGeneratorTest
{
    public static readonly DiagnosticTest StaticCase =
        new("UnmanagedInvoke generator emits static managed-call scaffolds", RunStaticContract);

    public static readonly DiagnosticTest InstanceCase =
        new("UnmanagedInvoke generator emits UnmanagedAccess instance helpers", RunInstanceContract);

    public static readonly DiagnosticTest AbstractInstanceCase =
        new("UnmanagedInvoke generator emits abstract base helpers and derived forwarders", RunAbstractInstanceContract);

    public static readonly DiagnosticTest StructCase =
        new("UnmanagedInvoke generator emits struct method helpers", RunStructContract);

    public static readonly DiagnosticTest InvalidSignatureCase =
        new("MED038 when UnmanagedInvoke targets unsupported signatures", RunInvalidSignatureContract);

    public static readonly DiagnosticTest HelperCollisionCase =
        new("MED039 when UnmanagedInvoke helper signatures collide after projection", RunHelperCollisionContract);

    static void RunStaticContract()
    {
        var run = RunGenerator(
            """
using Medicine;

public sealed class Payload
{
    public int Value;
}

public static partial class InvokeStaticHost
{
    public struct Position
    {
        public int Value;
    }

    [UnmanagedInvoke]
    public static Payload Replace(Payload payload, ref Payload current, out Payload previous, int value, in int add)
    {
        previous = current;
        current = new Payload { Value = payload.Value + value + add };
        return current;
    }

    [UnmanagedInvoke]
    public static Position Move(Position position, int offset)
        => new() { Value = position.Value + offset };
}
"""
        );

        AssertNoGeneratorException(run);

        var generatedText = GetGeneratedText(run);
        AssertContains(generatedText, "using ᵐInline = global::System.Runtime.CompilerServices.MethodImplAttribute;");
        AssertContains(generatedText, "static partial class InvokeStaticHost");
        AssertContains(generatedText, "static class ReplaceUnmanagedCallScaffold_");
        AssertContains(generatedText, "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        AssertContains(generatedText, "delegate nint UnmanagedDelegate(");
        AssertContains(generatedText, "static readonly global::Unity.Burst.SharedStatic<global::Unity.Burst.FunctionPointer<UnmanagedDelegate>> SharedStaticFunctionPointer");
        AssertContains(generatedText, "= global::Unity.Burst.SharedStatic<global::Unity.Burst.FunctionPointer<UnmanagedDelegate>>.GetOrCreate<SharedStaticKey>();");
        AssertContains(generatedText, "static class ManagedStorage");
        AssertContains(generatedText, "static readonly UnmanagedDelegate ManagedDelegate = Managed;");
        AssertContains(generatedText, "[global::UnityEngine.RuntimeInitializeOnLoadMethod(global::UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]");
        AssertContains(generatedText, "[global::AOT.MonoPInvokeCallbackAttribute(typeof(UnmanagedDelegate))]");
        AssertNestedAfter(generatedText, "static class ManagedStorage", "static readonly UnmanagedDelegate ManagedDelegate = Managed;");
        AssertNestedAfter(generatedText, "static class ManagedStorage", "[global::AOT.MonoPInvokeCallbackAttribute(typeof(UnmanagedDelegate))]");
        AssertContains(generatedText, "public static global::Medicine.UnmanagedRef<global::Payload> ReplaceUnmanaged(");
        AssertContains(generatedText, "global::Medicine.UnmanagedRef<global::Payload> payload,");
        AssertContains(generatedText, "ref global::Medicine.UnmanagedRef<global::Payload> current,");
        AssertContains(generatedText, "out global::Medicine.UnmanagedRef<global::Payload> previous,");
        AssertContains(generatedText, "var ᵐcurrentPtr = current.Ptr;");
        AssertContains(generatedText, "nint ᵐpreviousPtr;");
        AssertContains(generatedText, "var ᵐcurrentManaged = new global::Medicine.UnmanagedRef<global::Payload>(current).Resolve();");
        AssertContains(generatedText, "global::Payload ᵐpreviousManaged;");
        AssertContains(generatedText, "global::InvokeStaticHost.Replace(new global::Medicine.UnmanagedRef<global::Payload>(payload).Resolve(), ref ᵐcurrentManaged, out ᵐpreviousManaged, value, in add);");
        AssertContains(generatedText, "current = new global::Medicine.UnmanagedRef<global::Payload>(ᵐcurrentManaged).Ptr;");
        AssertContains(generatedText, "previous = new global::Medicine.UnmanagedRef<global::Payload>(ᵐpreviousManaged).Ptr;");
        AssertContains(generatedText, "return new global::Medicine.UnmanagedRef<global::Payload>(result).Ptr;");
        AssertContains(generatedText, "current = new global::Medicine.UnmanagedRef<global::Payload>(ᵐcurrentPtr);");
        AssertContains(generatedText, "previous = new global::Medicine.UnmanagedRef<global::Payload>(ᵐpreviousPtr);");
        AssertContains(generatedText, "delegate void UnmanagedDelegate(");
        AssertContains(generatedText, "in global::InvokeStaticHost.Position position,");
        AssertContains(generatedText, "out global::InvokeStaticHost.Position ᵐresult");
        AssertContains(generatedText, "static void Managed(");
        AssertContains(generatedText, "var ᵐreturnValue = global::InvokeStaticHost.Move(position, offset);");
        AssertContains(generatedText, "ᵐresult = ᵐreturnValue;");
        AssertContains(generatedText, "public static void Invoke(");
        AssertContains(generatedText, "SharedStaticFunctionPointer.Data.Invoke(in position, offset, out ᵐresult);");
        AssertContains(generatedText, "public static global::InvokeStaticHost.Position MoveUnmanaged(");
        AssertContains(generatedText, "global::InvokeStaticHost.Position position,");
        AssertContains(generatedText, "MoveUnmanagedCallScaffold_");
        AssertContains(generatedText, ".Invoke(in position, offset, out var ᵐreturnValue);");
        AssertContains(generatedText, "return ᵐreturnValue;");
    }

    static void RunInstanceContract()
    {
        var run = RunGenerator(
            """
using Medicine;

public sealed class Payload
{
    public int Value;
}

[UnmanagedAccess]
public partial class InvokeInstanceHost
{
    public int Value;

    [UnmanagedInvoke]
    public virtual int Add(Payload payload, ref int value)
    {
        value += payload.Value;
        return Value + value;
    }
}
"""
        );

        AssertNoGeneratorException(run);

        var generatedText = GetGeneratedText(run);
        AssertContains(generatedText, "static class AddUnmanagedCallScaffold_");
        AssertContains(generatedText, "nint self,");
        AssertContains(generatedText, "new global::Medicine.UnmanagedRef<global::InvokeInstanceHost>(self).Resolve().Add(new global::Medicine.UnmanagedRef<global::Payload>(payload).Resolve(), ref value)");
        AssertContains(generatedText, "public static partial class Unmanaged");
        AssertContains(generatedText, "public readonly unsafe partial struct AccessRW");
        AssertContains(generatedText, "public int AddUnmanaged(");
        AssertContains(generatedText, "return AddUnmanagedCallScaffold_");
        AssertContains(generatedText, ".Invoke(Ref.Ptr, payload.Ptr, ref value);");
        AssertContains(generatedText, "public readonly unsafe partial struct AccessRO");
    }

    static void RunAbstractInstanceContract()
    {
        var run = RunGenerator(
            """
using Medicine;

public sealed class Payload
{
    public int Value;
}

[UnmanagedAccess]
public abstract partial class AbstractInvokeAgent
{
    public int Value;

    [UnmanagedInvoke]
    public abstract int Score(Payload payload, ref int value);
}

[UnmanagedAccess]
public sealed partial class AbstractInvokeHuman : AbstractInvokeAgent
{
    public override int Score(Payload payload, ref int value)
    {
        value += payload.Value;
        return Value + value;
    }
}
"""
        );

        AssertNoGeneratorException(run);

        RoslynHarness.AssertDoesNotContainDiagnostic(
            diagnostics: run.Diagnostics.ToArray(),
            id: "MED038",
            because: "abstract class UnmanagedInvoke methods should be supported"
        );

        var generatedText = GetGeneratedText(run);
        AssertContains(generatedText, "new global::Medicine.UnmanagedRef<global::AbstractInvokeAgent>(self).Resolve().Score(new global::Medicine.UnmanagedRef<global::Payload>(payload).Resolve(), ref value)");
        AssertContains(generatedText, "partial class AbstractInvokeHuman");
        AssertContains(generatedText, "public int ScoreUnmanaged(");
        AssertContains(generatedText, "this.AsAbstractInvokeAgent().ScoreUnmanaged(payload, ref value);");
    }

    static void RunStructContract()
    {
        var run = RunGenerator(
            """
using Medicine;

public partial struct InvokeStructHost
{
    public int Value;

    [UnmanagedInvoke]
    public static int AddStatic(int left, int right)
        => left + right;

    [UnmanagedInvoke]
    public int Add(int value)
    {
        Value += value;
        return Value;
    }
}
"""
        );

        AssertNoGeneratorException(run);

        RoslynHarness.AssertDoesNotContainDiagnostic(
            diagnostics: run.Diagnostics.ToArray(),
            id: "MED038",
            because: "UnmanagedInvoke methods declared in structs should be supported"
        );

        var generatedText = GetGeneratedText(run);
        AssertContains(generatedText, "partial struct InvokeStructHost");
        AssertContains(generatedText, "global::InvokeStructHost.AddStatic(left, right)");
        AssertContains(generatedText, "public static int AddStaticUnmanaged(");
        AssertContains(generatedText, "static unsafe int Managed(");
        AssertContains(generatedText, "ref var ᵐself = ref global::Unity.Collections.LowLevel.Unsafe.UnsafeUtility.AsRef<global::InvokeStructHost>((void*)self);");
        AssertContains(generatedText, "ᵐself.Add(value)");
        AssertContains(generatedText, "public static unsafe int AddUnmanaged(");
        AssertContains(generatedText, "ref global::InvokeStructHost self");
        AssertContains(generatedText, ".Invoke((nint)global::Unity.Collections.LowLevel.Unsafe.UnsafeUtility.AddressOf(ref self), value);");
    }

    static void RunInvalidSignatureContract()
    {
        var run = RunGenerator(
            """
using Medicine;

public partial class InvalidInvokeHost
{
    [UnmanagedInvoke]
    public static void Generic<T>(T value) { }
}

public partial interface IInvalidInvokeInterface
{
    [UnmanagedInvoke]
    void InterfaceMethod();
}

public partial class LocalFunctionInvokeHost
{
    public void Outer()
    {
        [UnmanagedInvoke]
        static void Local() { }
    }
}
"""
        );

        RoslynHarness.AssertContainsDiagnostic(
            diagnostics: run.Diagnostics.ToArray(),
            id: "MED038",
            because: "generic UnmanagedInvoke methods are intentionally unsupported"
        );

        RoslynHarness.AssertDoesNotContainDiagnostic(
            diagnostics: run.Diagnostics.ToArray(),
            id: "MED911",
            because: "unsupported UnmanagedInvoke signatures should use a dedicated diagnostic"
        );

        AssertContainsDiagnosticMessage(
            diagnostics: run.Diagnostics.ToArray(),
            id: "MED038",
            expectedMessage: "[UnmanagedInvoke] cannot be used on local functions."
        );
    }

    static void RunHelperCollisionContract()
    {
        var run = RunGenerator(
            """
using Medicine;

public sealed class Payload { }

public partial class CollisionInvokeHost
{
    [UnmanagedInvoke]
    public static void Foo(Payload payload) { }

    [UnmanagedInvoke]
    public static void Foo(UnmanagedRef<Payload> payload) { }
}
"""
        );

        RoslynHarness.AssertContainsDiagnostic(
            diagnostics: run.Diagnostics.ToArray(),
            id: "MED039",
            because: "projected UnmanagedInvoke helper signatures collide"
        );

        RoslynHarness.AssertDoesNotContainDiagnostic(
            diagnostics: run.Diagnostics.ToArray(),
            id: "MED911",
            because: "projected helper collisions should use a dedicated diagnostic"
        );
    }

    static GeneratorDriverRunResult RunGenerator(string source)
    {
        var compilation = RoslynHarness.CreateCompilation(Stubs.Core, source);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new UnmanagedInvokeSourceGenerator().AsSourceGenerator()],
            parseOptions: CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersion.Preview)
                .WithPreprocessorSymbols("MEDICINE_EXTENSIONS_LIB")
        );

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    static string GetGeneratedText(GeneratorDriverRunResult run)
        => string.Join(
            Environment.NewLine,
            run.Results
                .SelectMany(static x => x.GeneratedSources)
                .Select(static x => x.SourceText.ToString())
        );

    static void AssertNoGeneratorException(GeneratorDriverRunResult run)
        => RoslynHarness.AssertDoesNotContainDiagnostic(
            diagnostics: run.Diagnostics.ToArray(),
            id: "MED911",
            because: "UnmanagedInvoke source generation should not throw"
        );

    static void AssertContains(string source, string expected)
    {
        if (source.Contains(expected, StringComparison.Ordinal))
            return;

        throw new InvalidOperationException($"Expected generated source to contain: {expected}");
    }

    static void AssertNestedAfter(string source, string container, string expected)
    {
        var containerIndex = source.IndexOf(container, StringComparison.Ordinal);
        var expectedIndex = source.IndexOf(expected, StringComparison.Ordinal);

        if (containerIndex >= 0 && expectedIndex > containerIndex)
            return;

        throw new InvalidOperationException($"Expected generated source to contain {expected} after {container}");
    }

    static void AssertContainsDiagnosticMessage(Diagnostic[] diagnostics, string id, string expectedMessage)
    {
        foreach (var diagnostic in diagnostics)
            if (diagnostic.Id.Equals(id, StringComparison.Ordinal) &&
                diagnostic.GetMessage().Contains(expectedMessage, StringComparison.Ordinal))
                return;

        throw new InvalidOperationException(
            $"Expected diagnostic '{id}' with message containing: {expectedMessage}{Environment.NewLine}" +
            $"Actual:{Environment.NewLine}{RoslynHarness.FormatDiagnostics(diagnostics)}"
        );
    }
}
