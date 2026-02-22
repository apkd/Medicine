using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

static class TrackSourceGeneratorTest
{
    public static readonly DiagnosticTest Case =
        new("Track generator adds more source when [Track] is used", Run);

    public static readonly DiagnosticTest CacheEnabledStateInheritanceCase =
        new("Track generator suppresses duplicated cacheEnabledState in inheritance", RunCacheEnabledStateInheritance);

    public static readonly DiagnosticTest ImplicitInstanceIndexCase =
        new("Track generator emits InstanceIndex for [Track] without IInstanceIndex", RunImplicitInstanceIndex);

    public static readonly DiagnosticTest InterfaceInstanceIndexCase =
        new("Track generator emits interface-specific internal instance indices", RunInterfaceInstanceIndex);

    public static readonly DiagnosticTest GenericInheritanceRegistrationCase =
        new("Track generator emits new + base registration methods for generic inheritance", RunGenericInheritanceRegistration);

    public static readonly DiagnosticTest InterfaceHelperCase =
        new("Track generator emits helper API for tracked interfaces", RunInterfaceHelper);

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

    static void RunImplicitInstanceIndex()
    {
        var compilation = RoslynHarness.CreateCompilation(
            Stubs.Core,
            """
using Medicine;
using UnityEngine;

[Track]
partial class TrackImplicitInstanceIndex : MonoBehaviour, IUnmanagedData<int> { }
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
            because: "track generation without explicit IInstanceIndex should not throw"
        );

        int instanceIndexPropertyCount = 0;
        foreach (var result in run.Results)
        foreach (var generatedSource in result.GeneratedSources)
        {
            var text = generatedSource.SourceText.ToString();
            instanceIndexPropertyCount += CountOccurrences(text, "public int InstanceIndex =>");
        }

        if (instanceIndexPropertyCount is 1)
            return;

        throw new InvalidOperationException(
            "Expected tracked type without IInstanceIndex to still emit InstanceIndex." + Environment.NewLine +
            $"Actual InstanceIndex count: {instanceIndexPropertyCount}."
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

    static void RunInterfaceInstanceIndex()
    {
        var compilation = RoslynHarness.CreateCompilation(
            Stubs.Core,
            """
using Medicine;
using UnityEngine;

[Track]
partial interface ITrackSourceGeneratorInterfaceInstanceIndex { }

[Track]
partial class TrackSourceGeneratorInterfaceInstanceIndex : MonoBehaviour, ITrackSourceGeneratorInterfaceInstanceIndex { }
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
            because: "interface-based track generation should not throw"
        );

        int interfaceIndexTypeReferenceCount = 0;
        int interfaceIndexMemberReferenceCount = 0;
        int interfaceIndexFieldReferenceCount = 0;
        foreach (var result in run.Results)
        foreach (var generatedSource in result.GeneratedSources)
        {
            var text = generatedSource.SourceText.ToString();
            interfaceIndexTypeReferenceCount += CountOccurrences(
                text,
                "global::Medicine.Internal.IInstanceIndex<global::ITrackSourceGeneratorInterfaceInstanceIndex>"
            );
            interfaceIndexMemberReferenceCount += CountOccurrences(
                text,
                "IInstanceIndex<global::ITrackSourceGeneratorInterfaceInstanceIndex>.InstanceIndex"
            );
            interfaceIndexFieldReferenceCount += CountOccurrences(text, "MedicineInternalInstanceIndexFor");
        }

        if (interfaceIndexTypeReferenceCount >= 2 && interfaceIndexMemberReferenceCount is 1 && interfaceIndexFieldReferenceCount >= 3)
            return;

        throw new InvalidOperationException(
            "Expected tracked-interface generation to emit interface-specific internal instance-index storage." + Environment.NewLine +
            $"Actual type references: {interfaceIndexTypeReferenceCount}, member references: {interfaceIndexMemberReferenceCount}, field references: {interfaceIndexFieldReferenceCount}."
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

    static void RunGenericInheritanceRegistration()
    {
        var compilation = RoslynHarness.CreateCompilation(
            Stubs.Core,
            """
using System;
using Medicine;
using UnityEngine;

[Track]
abstract partial class GenericTrackedBase<T> : MonoBehaviour
    where T : unmanaged { }

[Track]
sealed partial class GenericTrackedDerived : GenericTrackedBase<int> { }
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
            because: "generic inheritance registration emission should not throw"
        );

        int newOnEnableCount = 0;
        int newOnDisableCount = 0;
        int baseOnEnableCount = 0;
        int baseOnDisableCount = 0;
        foreach (var result in run.Results)
        foreach (var generatedSource in result.GeneratedSources)
        {
            var text = generatedSource.SourceText.ToString();
            newOnEnableCount += CountOccurrences(text, "new void OnEnableINTERNAL()");
            newOnDisableCount += CountOccurrences(text, "new void OnDisableINTERNAL()");
            baseOnEnableCount += CountOccurrences(text, "base.OnEnableINTERNAL();");
            baseOnDisableCount += CountOccurrences(text, "base.OnDisableINTERNAL();");
        }

        if (newOnEnableCount is 1 && newOnDisableCount is 1 && baseOnEnableCount is 1 && baseOnDisableCount is 1)
            return;

        throw new InvalidOperationException(
            "Expected derived generic tracked type to emit new + base registration methods." + Environment.NewLine +
            $"Actual new OnEnable: {newOnEnableCount}, new OnDisable: {newOnDisableCount}, base OnEnable: {baseOnEnableCount}, base OnDisable: {baseOnDisableCount}."
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

    static void RunInterfaceHelper()
    {
        var compilation = RoslynHarness.CreateCompilation(
            Stubs.Core,
            """
using Medicine;
using UnityEngine;

struct InterfaceHelperData
{
    public int Value;
}

[Track(transformAccessArray: true)]
partial interface ITrackSourceGeneratorInterfaceHelper : IFindByID<int>, IUnmanagedData<InterfaceHelperData> { }

[Track]
partial class TrackSourceGeneratorInterfaceHelper : MonoBehaviour, ITrackSourceGeneratorInterfaceHelper { }
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
            because: "tracked-interface helper generation should not throw"
        );

        int helperContainerCount = 0;
        int helperClassCount = 0;
        int instancesCount = 0;
        int transformAccessCount = 0;
        int transformInitializeCount = 0;
        int instanceIdsCount = 0;
        int unmanagedCount = 0;
        int findByIdCount = 0;
        foreach (var result in run.Results)
        foreach (var generatedSource in result.GeneratedSources)
        {
            var text = generatedSource.SourceText.ToString();
            helperContainerCount += CountOccurrences(text, "partial interface ITrackSourceGeneratorInterfaceHelper");
            helperClassCount += CountOccurrences(text, "static class Track");
            instancesCount += CountOccurrences(text, "TrackedInstances<global::ITrackSourceGeneratorInterfaceHelper> Instances");
            transformAccessCount += CountOccurrences(text, "TransformAccessArray");
            transformInitializeCount += CountOccurrences(text, "Storage.TransformAccess<global::ITrackSourceGeneratorInterfaceHelper>.Initialize(");
            instanceIdsCount += CountOccurrences(text, "NativeArray<int> InstanceIDs");
            unmanagedCount += CountOccurrences(text, "Storage.UnmanagedData<global::ITrackSourceGeneratorInterfaceHelper, global::InterfaceHelperData>");
            findByIdCount += CountOccurrences(text, "LookupByID<global::ITrackSourceGeneratorInterfaceHelper,");
        }

        if (
            helperContainerCount >= 1 &&
            helperClassCount is 1 &&
            instancesCount >= 1 &&
            transformAccessCount >= 1 &&
            transformInitializeCount is 1 &&
            instanceIdsCount is 0 &&
            unmanagedCount >= 1 &&
            findByIdCount >= 1
           )
            return;

        throw new InvalidOperationException(
            "Expected tracked-interface helper generation to emit parity APIs." + Environment.NewLine +
            $"Actual helper containers: {helperContainerCount}, helper classes: {helperClassCount}, instances: {instancesCount}, transforms: {transformAccessCount}, transform inits: {transformInitializeCount}, ids: {instanceIdsCount}, unmanaged: {unmanagedCount}, findById: {findByIdCount}."
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
