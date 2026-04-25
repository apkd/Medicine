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

    public static readonly DiagnosticTest ManagedValueTypeProjectionCase =
        new("UnmanagedAccess generator projects managed value types as refs", RunManagedValueTypeProjectionContract);

    public static readonly DiagnosticTest NullableValueTypeProjectionCase =
        new("UnmanagedAccess generator projects nullable value types as refs", RunNullableValueTypeProjectionContract);

    public static readonly DiagnosticTest CastHelperCase =
        new("UnmanagedAccess generator emits inheritance cast extension helpers", RunCastHelperContract);

    public static readonly DiagnosticTest AccessROPartialForwardingCase =
        new("UnmanagedAccess generator forwards user AccessRO members from generated AccessRW", RunAccessROPartialForwardingContract);

    public static readonly DiagnosticTest UnityObjectIdentityCase =
        new("UnmanagedAccess generator emits EntityID for UnityEngine.Object types when GetEntityId is available", RunUnityObjectIdentityContract);

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
                .WithPreprocessorSymbols("MEDICINE_EXTENSIONS_LIB", "UNITY_6000_4_OR_NEWER")
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
                .WithPreprocessorSymbols("MEDICINE_EXTENSIONS_LIB", "UNITY_6000_4_OR_NEWER")
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
        AssertContains("public global::Unity.Collections.NativeArray<Medicine.UnmanagedRef<global::Inner>> Children");
        AssertContainsAny(
            "public global::Unity.Collections.NativeArray<Medicine.UnmanagedRef<global::System.String>> Names",
            "public global::Unity.Collections.NativeArray<Medicine.UnmanagedRef<string>> Names"
        );
        AssertContains("public global::Inner.Unmanaged.AccessRW AutoChild");
        AssertContains("public global::Unity.Collections.NativeArray<int> AutoValues");
        AssertContains("public global::Unity.Collections.NativeArray<Medicine.UnmanagedRef<global::Inner>> AutoChildren");

        AssertContains("public global::Inner.Unmanaged.AccessRO Child");
        AssertContains("public global::Unity.Collections.NativeArray<int>.ReadOnly Values");
        AssertContains("public global::Unity.Collections.NativeArray<Medicine.UnmanagedRef<global::Inner>>.ReadOnly Children");
        AssertContains("public global::Inner.Unmanaged.AccessRO AutoChild");
        AssertContains("public global::Unity.Collections.NativeArray<int>.ReadOnly AutoValues");
        AssertContains("public global::Unity.Collections.NativeArray<Medicine.UnmanagedRef<global::Inner>>.ReadOnly AutoChildren");

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

    static void RunUnityObjectIdentityContract()
    {
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview)
            .WithPreprocessorSymbols("MEDICINE_EXTENSIONS_LIB", "UNITY_6000_4_OR_NEWER");

        var compilation = RoslynHarness.CreateCompilation(
            parseOptions,
            Stubs.Core,
            """
using Medicine;
using UnityEngine;

[UnmanagedAccess]
sealed partial class UnmanagedAccessUnityObjectIdentityComponent : MonoBehaviour
{
    int counter;
}
"""
        );

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new UnmanagedAccessSourceGenerator().AsSourceGenerator()],
            parseOptions: parseOptions
        );

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var run = driver.GetRunResult();

        RoslynHarness.AssertDoesNotContainDiagnostic(
            diagnostics: run.Diagnostics.ToArray(),
            id: "MED911",
            because: "UnityEngine.Object identity accessor generation should not throw"
        );

        int entityIdPropertyCount = 0;
        int instanceIdPropertyCount = 0;
        int obsoleteInstanceIdCount = 0;
        int hiddenInstanceIdCount = 0;
        foreach (var result in run.Results)
        foreach (var generatedSource in result.GeneratedSources)
        {
            var text = generatedSource.SourceText.ToString();
            entityIdPropertyCount += CountOccurrences(text, "public global::UnityEngine.EntityId EntityID");
            instanceIdPropertyCount += CountOccurrences(text, "public int InstanceID");
            obsoleteInstanceIdCount += CountOccurrences(text, "[global::System.Obsolete(\"InstanceID APIs are obsolete on Unity >=6.4. Use EntityId APIs instead.\", true)]");
            hiddenInstanceIdCount += CountOccurrences(text, "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        }

        if (entityIdPropertyCount is not 2)
            throw new InvalidOperationException(
                "Expected unmanaged-access generation to emit the UnityEngine.EntityId-based EntityID accessor once for AccessRW and once for AccessRO." + Environment.NewLine +
                $"Actual EntityID count: {entityIdPropertyCount}."
            );

        if (instanceIdPropertyCount is 2 && obsoleteInstanceIdCount is 2 && hiddenInstanceIdCount is 2)
            return;

        throw new InvalidOperationException(
            "Expected EntityId-based unmanaged access generation to emit the legacy InstanceID accessor only as an obsolete erroring migration stub." + Environment.NewLine +
            $"Actual InstanceID count: {instanceIdPropertyCount}. Actual obsolete marker count: {obsoleteInstanceIdCount}. Actual hidden marker count: {hiddenInstanceIdCount}."
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

    static void RunAccessROPartialForwardingContract()
    {
        var compilation = RoslynHarness.CreateCompilation(
            Stubs.Core,
            """
using System;
using Medicine;
using UserNamespace;
using HelperAlias = UserNamespace.LocalHelper;
using ResultAlias = UserNamespace.LocalResult;

namespace UserNamespace
{
    public readonly struct LocalResult
    {
        public readonly int Value;

        public LocalResult(int value)
            => Value = value;
    }

    public static class LocalHelper
    {
        public static LocalResult Make(int value)
            => new(value);
    }
}

[UnmanagedAccess]
partial class AccessROPartialForwardingOwner
{
    public int Value;

    public static partial class Unmanaged
    {
        public readonly unsafe partial struct AccessRO
        {
            public readonly int ShouldNotCopyField;

            public ResultAlias Build()
                => HelperAlias.Make(Value);

            public ResultAlias Add(ResultAlias input)
            {
                ResultAlias result = HelperAlias.Make(Value + input.Value);
                return result;
            }

            public ResultAlias Result
                => HelperAlias.Make(Value);

            public ResultAlias this[int index]
                => HelperAlias.Make(Value + index);

            public ResultAlias Existing()
                => HelperAlias.Make(Value);
        }

        public readonly unsafe partial struct AccessRW
        {
            public ResultAlias Existing()
                => default;
        }
    }
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
            because: "AccessRO partial forwarding codegen should not throw in generator"
        );

        var generatedText = string.Join(
            Environment.NewLine,
            run.Results
                .SelectMany(static x => x.GeneratedSources)
                .Select(static x => x.SourceText.ToString())
        );

        AssertContains("public AccessRO AsReadOnly()");
        AssertContains("=> new(Ref, ref *layoutInfo);");
        AssertContains("public global::UserNamespace.LocalResult Build()");
        AssertContains("=> AsReadOnly().Build();");
        AssertContains("public global::UserNamespace.LocalResult Add(global::UserNamespace.LocalResult input)");
        AssertContains("=> AsReadOnly().Add(input);");
        AssertContains("public global::UserNamespace.LocalResult Result");
        AssertContains("=> AsReadOnly().Result;");
        AssertContains("=> AsReadOnly()[index];");
        AssertDoesNotContain("ResultAlias");
        AssertDoesNotContain("HelperAlias");
        AssertDoesNotContain("global::UserNamespace.LocalHelper.Make");
        AssertDoesNotContain("ShouldNotCopyField");
        AssertDoesNotContain("return result;");
        AssertDoesNotContain("=> AsReadOnly().Existing();");

        static void ThrowMissing(string expected)
            => throw new InvalidOperationException($"Expected generated source to contain: {expected}");

        static void ThrowUnexpected(string unexpected)
            => throw new InvalidOperationException($"Expected generated source to not contain: {unexpected}");

        void AssertContains(string expected)
        {
            if (generatedText.Contains(expected, StringComparison.Ordinal))
                return;

            ThrowMissing(expected);
        }

        void AssertDoesNotContain(string unexpected)
        {
            if (!generatedText.Contains(unexpected, StringComparison.Ordinal))
                return;

            ThrowUnexpected(unexpected);
        }
    }

    static void RunManagedValueTypeProjectionContract()
    {
        var compilation = RoslynHarness.CreateCompilation(
            Stubs.Core,
            """
using System;
using Medicine;

public struct ManagedPayload
{
    public object Target;
    public int Count;
}

[UnmanagedAccess]
partial class ManagedValueTypeOuter
{
    public ManagedPayload Payload;
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
            because: "managed value-type projection should not throw in generator"
        );

        var generatedText = string.Join(
            Environment.NewLine,
            run.Results
                .SelectMany(static x => x.GeneratedSources)
                .Select(static x => x.SourceText.ToString())
        );

        AssertContains("public ref global::ManagedPayload Payload");
        AssertContains("ᵐUtility.AsRefUnchecked<global::ManagedPayload>");
        AssertContains("layoutInfo->Payload");
        AssertContains("public ref readonly global::ManagedPayload Payload");
        AssertDoesNotContain("Medicine.UnmanagedRef<global::ManagedPayload>");

        static void ThrowMissing(string expected)
            => throw new InvalidOperationException($"Expected generated source to contain: {expected}");

        static void ThrowUnexpected(string unexpected)
            => throw new InvalidOperationException($"Expected generated source to not contain: {unexpected}");

        void AssertContains(string expected)
        {
            if (generatedText.Contains(expected, StringComparison.Ordinal))
                return;

            ThrowMissing(expected);
        }

        void AssertDoesNotContain(string unexpected)
        {
            if (!generatedText.Contains(unexpected, StringComparison.Ordinal))
                return;

            ThrowUnexpected(unexpected);
        }
    }

    static void RunNullableValueTypeProjectionContract()
    {
        var compilation = RoslynHarness.CreateCompilation(
            Stubs.Core,
            """
using Medicine;

[UnmanagedAccess]
partial class NullableValueTypeOuter
{
    public int? MaybeCount;
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
            because: "nullable value-type projection should not throw in generator"
        );

        var generatedText = string.Join(
            Environment.NewLine,
            run.Results
                .SelectMany(static x => x.GeneratedSources)
                .Select(static x => x.SourceText.ToString())
        );

        AssertContains("public ref int? MaybeCount");
        AssertContains("ᵐUtility.AsRefUnchecked<int?>");
        AssertContains("layoutInfo->MaybeCount");
        AssertContains("public ref readonly int? MaybeCount");
        AssertDoesNotContain("Ref.Read<int?>");

        static void ThrowMissing(string expected)
            => throw new InvalidOperationException($"Expected generated source to contain: {expected}");

        static void ThrowUnexpected(string unexpected)
            => throw new InvalidOperationException($"Expected generated source to not contain: {unexpected}");

        void AssertContains(string expected)
        {
            if (generatedText.Contains(expected, StringComparison.Ordinal))
                return;

            ThrowMissing(expected);
        }

        void AssertDoesNotContain(string unexpected)
        {
            if (!generatedText.Contains(unexpected, StringComparison.Ordinal))
                return;

            ThrowUnexpected(unexpected);
        }
    }

    static void RunCastHelperContract()
    {
        var compilation = RoslynHarness.CreateCompilation(
            Stubs.Core,
            """
using Medicine;

[UnmanagedAccess]
public abstract partial class AccessCastAnimal
{
    public int BaseValue;
}

[UnmanagedAccess]
public partial class AccessCastMammal : AccessCastAnimal
{
    public int MidValue;
}

[UnmanagedAccess]
public sealed partial class AccessCastHuman : AccessCastMammal
{
    public int DerivedValue;
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
            because: "cast helper generation should not throw"
        );

        var generatedText = string.Join(
            Environment.NewLine,
            run.Results
                .SelectMany(static x => x.GeneratedSources)
                .Select(static x => x.SourceText.ToString())
        );

        AssertContains("public static global::AccessCastAnimal.Unmanaged.AccessRW AsAccessCastAnimal(this in global::AccessCastHuman.Unmanaged.AccessRW access)");
        AssertContains("public static global::AccessCastHuman.Unmanaged.AccessRO AsAccessCastHuman(this in global::AccessCastAnimal.Unmanaged.AccessRO access)");
        AssertContains("public static global::AccessCastMammal.Unmanaged.AccessRW AsAccessCastMammal(this in global::AccessCastAnimal.Unmanaged.AccessRW access)");
        AssertContains("=> new(new global::Medicine.UnmanagedRef<global::AccessCastHuman>(access.Ref.Ptr));");

        static void ThrowMissing(string expected)
            => throw new InvalidOperationException($"Expected generated source to contain: {expected}");

        void AssertContains(string expected)
        {
            if (generatedText.Contains(expected, StringComparison.Ordinal))
                return;

            ThrowMissing(expected);
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
