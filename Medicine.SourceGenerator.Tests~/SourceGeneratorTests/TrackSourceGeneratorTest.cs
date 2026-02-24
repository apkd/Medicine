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

    public static readonly DiagnosticTest CustomStorageCase =
        new("Track generator emits custom storage APIs and registration calls", RunCustomStorage);

    public static readonly DiagnosticTest InterfaceHelperCustomStorageCase =
        new("Track generator emits custom storage helper APIs for tracked interfaces", RunInterfaceHelperCustomStorage);

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

    static void RunCustomStorage()
    {
        var compilation = RoslynHarness.CreateCompilation(
            Stubs.Core,
            """
using Medicine;
using UnityEngine;

sealed class CustomPayload { }

[Track]
partial class TrackSourceGeneratorCustomStorage : MonoBehaviour, ICustomStorage<CustomPayload>, ITrackInstanceIDs
{
    void ICustomStorage<CustomPayload>.RegisterInstance(ref CustomPayload storage) { }
    void ICustomStorage<CustomPayload>.UnregisterInstance(ref CustomPayload storage, int instanceIndex) { }
}
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
            because: "custom-storage generation should not throw"
        );

        int classStoragePropertyCount = 0;
        int trackInstanceIdStoragePropertyCount = 0;
        int classStorageRegisterCount = 0;
        int classStorageUnregisterCount = 0;
        int trackInstanceIdStorageRegisterCount = 0;
        int trackInstanceIdStorageUnregisterCount = 0;
        foreach (var result in run.Results)
        foreach (var generatedSource in result.GeneratedSources)
        {
            var text = generatedSource.SourceText.ToString();
            classStoragePropertyCount += CountOccurrences(text, "ref global::CustomPayload CustomPayloadStorage");
            trackInstanceIdStoragePropertyCount += CountOccurrences(text, "ref global::Medicine.ITrackInstanceIDs.Storage Storage");
            classStorageRegisterCount += CountOccurrences(text, "Storage.Custom<global::TrackSourceGeneratorCustomStorage, global::CustomPayload>.Register(this)");
            classStorageUnregisterCount += CountOccurrences(text, "Storage.Custom<global::TrackSourceGeneratorCustomStorage, global::CustomPayload>.Unregister(this, index)");
            trackInstanceIdStorageRegisterCount += CountOccurrences(text, "Storage.Custom<global::TrackSourceGeneratorCustomStorage, global::Medicine.ITrackInstanceIDs.Storage>.Register(this)");
            trackInstanceIdStorageUnregisterCount += CountOccurrences(text, "Storage.Custom<global::TrackSourceGeneratorCustomStorage, global::Medicine.ITrackInstanceIDs.Storage>.Unregister(this, index)");
        }

        if (
            classStoragePropertyCount is 1 &&
            trackInstanceIdStoragePropertyCount is 1 &&
            classStorageRegisterCount is 1 &&
            classStorageUnregisterCount is 1 &&
            trackInstanceIdStorageRegisterCount is 1 &&
            trackInstanceIdStorageUnregisterCount is 1
           )
            return;

        throw new InvalidOperationException(
            "Expected tracked type custom storage generation to emit properties and lifecycle calls." + Environment.NewLine +
            $"Actual class property: {classStoragePropertyCount}, ITrackInstanceIDs property: {trackInstanceIdStoragePropertyCount}, " +
            $"class register: {classStorageRegisterCount}, class unregister: {classStorageUnregisterCount}, " +
            $"ITrackInstanceIDs register: {trackInstanceIdStorageRegisterCount}, ITrackInstanceIDs unregister: {trackInstanceIdStorageUnregisterCount}."
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

    static void RunInterfaceHelperCustomStorage()
    {
        var compilation = RoslynHarness.CreateCompilation(
            Stubs.Core,
            """
using Medicine;
using UnityEngine;

sealed class InterfacePayload { }

[Track]
partial interface ITrackSourceGeneratorInterfaceCustomStorage : ICustomStorage<InterfacePayload>, ITrackInstanceIDs
{
    void ICustomStorage<InterfacePayload>.RegisterInstance(ref InterfacePayload storage) { }
    void ICustomStorage<InterfacePayload>.UnregisterInstance(ref InterfacePayload storage, int instanceIndex) { }
}

[Track]
partial class TrackSourceGeneratorInterfaceCustomStorage : MonoBehaviour, ITrackSourceGeneratorInterfaceCustomStorage { }
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
            because: "interface custom-storage helper generation should not throw"
        );

        int helperPayloadPropertyCount = 0;
        int helperTrackInstanceIdsPropertyCount = 0;
        int helperPayloadRegisterCount = 0;
        int helperPayloadUnregisterCount = 0;
        int helperTrackInstanceIdsRegisterCount = 0;
        int helperTrackInstanceIdsUnregisterCount = 0;
        foreach (var result in run.Results)
        foreach (var generatedSource in result.GeneratedSources)
        {
            var text = generatedSource.SourceText.ToString();
            helperPayloadPropertyCount += CountOccurrences(text, "ref global::InterfacePayload InterfacePayloadStorage");
            helperTrackInstanceIdsPropertyCount += CountOccurrences(text, "ref global::Medicine.ITrackInstanceIDs.Storage Storage");
            helperPayloadRegisterCount += CountOccurrences(text, "Storage.Custom<global::ITrackSourceGeneratorInterfaceCustomStorage, global::InterfacePayload>.Register(this)");
            helperPayloadUnregisterCount += CountOccurrences(text, "Storage.Custom<global::ITrackSourceGeneratorInterfaceCustomStorage, global::InterfacePayload>.Unregister(this,");
            helperTrackInstanceIdsRegisterCount += CountOccurrences(text, "Storage.Custom<global::ITrackSourceGeneratorInterfaceCustomStorage, global::Medicine.ITrackInstanceIDs.Storage>.Register(this)");
            helperTrackInstanceIdsUnregisterCount += CountOccurrences(text, "Storage.Custom<global::ITrackSourceGeneratorInterfaceCustomStorage, global::Medicine.ITrackInstanceIDs.Storage>.Unregister(this,");
        }

        if (
            helperPayloadPropertyCount >= 1 &&
            helperTrackInstanceIdsPropertyCount >= 1 &&
            helperPayloadRegisterCount is 1 &&
            helperPayloadUnregisterCount is 1 &&
            helperTrackInstanceIdsRegisterCount is 1 &&
            helperTrackInstanceIdsUnregisterCount is 1
           )
            return;

        throw new InvalidOperationException(
            "Expected tracked-interface helper custom storage generation to emit properties and lifecycle calls." + Environment.NewLine +
            $"Actual payload property: {helperPayloadPropertyCount}, ITrackInstanceIDs property: {helperTrackInstanceIdsPropertyCount}, " +
            $"payload register: {helperPayloadRegisterCount}, payload unregister: {helperPayloadUnregisterCount}, " +
            $"ITrackInstanceIDs register: {helperTrackInstanceIdsRegisterCount}, ITrackInstanceIDs unregister: {helperTrackInstanceIdsUnregisterCount}."
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
