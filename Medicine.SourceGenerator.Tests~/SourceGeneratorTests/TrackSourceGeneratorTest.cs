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

    public static readonly DiagnosticTest InheritedMemberHidingCase =
        new("Track generator emits new for inherited tracked API members", RunInheritedMemberHiding);

    public static readonly DiagnosticTest InterfaceHelperCase =
        new("Track generator emits helper API for tracked interfaces", RunInterfaceHelper);

    public static readonly DiagnosticTest CustomStorageCase =
        new("Track generator emits custom storage APIs and registration calls", RunCustomStorage);

    public static readonly DiagnosticTest InterfaceHelperCustomStorageCase =
        new("Track generator emits custom storage helper APIs for tracked interfaces", RunInterfaceHelperCustomStorage);

    public static readonly DiagnosticTest NonTrackedInterfaceUnmanagedPropagationCase =
        new("Track generator treats non-tracked inherited unmanaged-data interfaces like direct implementation", RunNonTrackedInterfaceUnmanagedPropagation);

    public static readonly DiagnosticTest TrackedInterfaceUnmanagedNoImplicitClassPropagationCase =
        new("Track generator does not implicitly copy unmanaged-data from tracked interfaces to implementing class type", RunTrackedInterfaceUnmanagedNoImplicitClassPropagation);

    public static readonly DiagnosticTest NonTrackedInterfaceUnmanagedNoDerivedClassInheritanceCase =
        new("Track generator does not implicitly inherit non-tracked unmanaged-data marker interfaces through class inheritance", RunNonTrackedInterfaceUnmanagedNoDerivedClassInheritance);

    public static readonly DiagnosticTest ExplicitClassUnmanagedAlongTrackedInterfaceCase =
        new("Track generator keeps direct class unmanaged-data implementation even when tracked interface also provides same unmanaged-data type", RunExplicitClassUnmanagedAlongTrackedInterface);

    public static readonly DiagnosticTest ExecuteAlwaysMetadataCase =
        new("Track generator bakes inherited ExecuteAlways metadata", RunExecuteAlwaysMetadata);

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

    static void RunExecuteAlwaysMetadata()
    {
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview)
            .WithPreprocessorSymbols("MEDICINE_EXTENSIONS_LIB", "DEBUG", "UNITY_EDITOR");

        var compilation = RoslynHarness.CreateCompilation(
            parseOptions,
            Stubs.Core,
            """
            using Medicine;
            using UnityEngine;

            [Track]
            partial class PlainTrackedExecuteAlwaysMetadata : MonoBehaviour { }

            [Track, ExecuteAlways]
            partial class DirectTrackedExecuteAlwaysMetadata : MonoBehaviour { }

            [ExecuteAlways]
            abstract partial class ExecuteAlwaysMetadataBase : MonoBehaviour { }

            [Track]
            partial class InheritedTrackedExecuteAlwaysMetadata : ExecuteAlwaysMetadataBase { }

            [Track, ExecuteAlways]
            partial class ScriptableObjectTrackedExecuteAlwaysMetadata : ScriptableObject { }
            """
        );

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new TrackSourceGenerator().AsSourceGenerator()],
            parseOptions: parseOptions
        );

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var run = driver.GetRunResult();

        RoslynHarness.AssertDoesNotContainDiagnostic(
            diagnostics: run.Diagnostics.ToArray(),
            id: "MED911",
            because: "ExecuteAlways metadata generation should not throw"
        );

        int executeAlwaysFlagCount = 0;
        bool directTypeFlagged = false;
        bool inheritedTypeFlagged = false;
        bool plainTypeFlagged = false;
        bool scriptableObjectTypeFlagged = false;
        int monoBehaviourEditModeFallbackDocCount = 0;
        int executeAlwaysEditModeDocCount = 0;
        int scriptableObjectEditModeDocCount = 0;
        foreach (var result in run.Results)
        foreach (var generatedSource in result.GeneratedSources)
        {
            var text = generatedSource.SourceText.ToString();
            executeAlwaysFlagCount += CountOccurrences(text, "Utility.TypeFlags.IsExecuteAlways");
            monoBehaviourEditModeFallbackDocCount += CountOccurrences(
                text,
                "this MonoBehaviour uses a cached <see cref=\"global::UnityEngine.Object.FindObjectsByType(System.Type)\"/> fallback"
            );
            executeAlwaysEditModeDocCount += CountOccurrences(
                text,
                "this [<see cref=\"global::UnityEngine.ExecuteAlways\"/>] MonoBehaviour uses regular OnEnable/OnDisable tracking"
            );
            scriptableObjectEditModeDocCount += CountOccurrences(
                text,
                "this ScriptableObject uses a cached <see cref=\"global::UnityEngine.Resources.FindObjectsOfTypeAll(System.Type)\"/> fallback"
            );

            if (text.Contains("BakedTypeInfo<global::DirectTrackedExecuteAlwaysMetadata>", StringComparison.Ordinal))
                directTypeFlagged = text.Contains("Utility.TypeFlags.IsExecuteAlways", StringComparison.Ordinal);

            if (text.Contains("BakedTypeInfo<global::InheritedTrackedExecuteAlwaysMetadata>", StringComparison.Ordinal))
                inheritedTypeFlagged = text.Contains("Utility.TypeFlags.IsExecuteAlways", StringComparison.Ordinal);

            if (text.Contains("BakedTypeInfo<global::PlainTrackedExecuteAlwaysMetadata>", StringComparison.Ordinal))
                plainTypeFlagged = text.Contains("Utility.TypeFlags.IsExecuteAlways", StringComparison.Ordinal);

            if (text.Contains("BakedTypeInfo<global::ScriptableObjectTrackedExecuteAlwaysMetadata>", StringComparison.Ordinal))
                scriptableObjectTypeFlagged = text.Contains("Utility.TypeFlags.IsExecuteAlways", StringComparison.Ordinal);
        }

        if (
            executeAlwaysFlagCount is 2 &&
            directTypeFlagged &&
            inheritedTypeFlagged &&
            !plainTypeFlagged &&
            !scriptableObjectTypeFlagged &&
            monoBehaviourEditModeFallbackDocCount is 1 &&
            executeAlwaysEditModeDocCount is 2 &&
            scriptableObjectEditModeDocCount is 1
        )
            return;

        throw new InvalidOperationException(
            "Expected ExecuteAlways metadata only for direct and inherited ExecuteAlways tracked MonoBehaviour types." + Environment.NewLine +
            $"Actual flag count: {executeAlwaysFlagCount}, direct: {directTypeFlagged}, inherited: {inheritedTypeFlagged}, plain: {plainTypeFlagged}, scriptable object: {scriptableObjectTypeFlagged}. " +
            $"Docs: MonoBehaviour fallback: {monoBehaviourEditModeFallbackDocCount}, ExecuteAlways: {executeAlwaysEditModeDocCount}, ScriptableObject: {scriptableObjectEditModeDocCount}."
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

    static void RunInheritedMemberHiding()
    {
        var compilation = RoslynHarness.CreateCompilation(
            Stubs.Core,
            """
            using Medicine;
            using UnityEngine;

            struct InheritedMemberData
            {
                public int Value;
            }

            [Track]
            abstract partial class BaseTrackedInheritedMembers : MonoBehaviour, IUnmanagedData<InheritedMemberData> { }

            [Track]
            sealed partial class DerivedTrackedInheritedMembers : BaseTrackedInheritedMembers, IUnmanagedData<InheritedMemberData> { }
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
            because: "tracked inheritance with inherited helper members should not throw"
        );

        int newInstanceIndexCount = 0;
        int newLocalAccessorCount = 0;
        foreach (var result in run.Results)
        foreach (var generatedSource in result.GeneratedSources)
        {
            var text = generatedSource.SourceText.ToString();
            newInstanceIndexCount += CountOccurrences(text, "public new int InstanceIndex =>");
            newLocalAccessorCount += CountOccurrences(text, "public new ref global::InheritedMemberData LocalInheritedMemberData");
        }

        if (newInstanceIndexCount is 1 && newLocalAccessorCount is 1)
            return;

        throw new InvalidOperationException(
            "Expected derived tracked type to emit new for inherited tracked API members." + Environment.NewLine +
            $"Actual new InstanceIndex: {newInstanceIndexCount}, new Local accessor: {newLocalAccessorCount}."
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
        RunCase(unity6000_4OrNewer: false);
        RunCase(unity6000_4OrNewer: true);

        static void RunCase(bool unity6000_4OrNewer)
        {
            var parseOptions = CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersion.Preview);

            parseOptions = unity6000_4OrNewer
                ? parseOptions.WithPreprocessorSymbols("MEDICINE_EXTENSIONS_LIB", "UNITY_6000_4_OR_NEWER")
                : parseOptions.WithPreprocessorSymbols("MEDICINE_EXTENSIONS_LIB");

            var compilation = RoslynHarness.CreateCompilation(
                parseOptions,
                Stubs.Core,
                """
                using Medicine;
                using UnityEngine;

                sealed class CustomPayload { }

                [Track]
                partial class TrackSourceGeneratorCustomStorage : MonoBehaviour, ICustomStorage<CustomPayload>,
                #if UNITY_6000_4_OR_NEWER
                    ITrackEntityIDs
                #else
                    ITrackInstanceIDs
                #endif
                {
                    void ICustomStorage<CustomPayload>.RegisterInstance(ref CustomPayload storage) { }
                    void ICustomStorage<CustomPayload>.UnregisterInstance(ref CustomPayload storage, int instanceIndex) { }
                }
                """
            );

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                generators: [new TrackSourceGenerator().AsSourceGenerator()],
                parseOptions: parseOptions
            );

            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
            var run = driver.GetRunResult();

            RoslynHarness.AssertDoesNotContainDiagnostic(
                diagnostics: run.Diagnostics.ToArray(),
                id: "MED911",
                because: unity6000_4OrNewer
                    ? "custom-storage generation should not throw on Unity 6000.4+"
                    : "custom-storage generation should not throw before Unity 6000.4"
            );

            var interfaceName = unity6000_4OrNewer ? "ITrackEntityIDs" : "ITrackInstanceIDs";

            int classStoragePropertyCount = 0;
            int trackStoragePropertyCount = 0;
            int classStorageRegisterCount = 0;
            int classStorageUnregisterCount = 0;
            int trackStorageRegisterCount = 0;
            int trackStorageUnregisterCount = 0;
            foreach (var result in run.Results)
            foreach (var generatedSource in result.GeneratedSources)
            {
                var text = generatedSource.SourceText.ToString();
                classStoragePropertyCount += CountOccurrences(text, "ref global::CustomPayload CustomPayloadStorage");
                trackStoragePropertyCount += CountOccurrences(text, $"ref global::Medicine.{interfaceName}.Storage Storage");
                classStorageRegisterCount += CountOccurrences(text, "Storage.Custom<global::TrackSourceGeneratorCustomStorage, global::CustomPayload>.Register(this)");
                classStorageUnregisterCount += CountOccurrences(text, "Storage.Custom<global::TrackSourceGeneratorCustomStorage, global::CustomPayload>.Unregister(this, index)");
                trackStorageRegisterCount += CountOccurrences(text, $"Storage.Custom<global::TrackSourceGeneratorCustomStorage, global::Medicine.{interfaceName}.Storage>.Register(this)");
                trackStorageUnregisterCount += CountOccurrences(text, $"Storage.Custom<global::TrackSourceGeneratorCustomStorage, global::Medicine.{interfaceName}.Storage>.Unregister(this, index)");
            }

            if (
                classStoragePropertyCount is 1 &&
                trackStoragePropertyCount is 1 &&
                classStorageRegisterCount is 1 &&
                classStorageUnregisterCount is 1 &&
                trackStorageRegisterCount is 1 &&
                trackStorageUnregisterCount is 1
            )
                return;

            throw new InvalidOperationException(
                "Expected tracked type custom storage generation to emit properties and lifecycle calls." + Environment.NewLine +
                $"Unity branch: {interfaceName}. " +
                $"Actual class property: {classStoragePropertyCount}, {interfaceName} property: {trackStoragePropertyCount}, " +
                $"class register: {classStorageRegisterCount}, class unregister: {classStorageUnregisterCount}, " +
                $"{interfaceName} register: {trackStorageRegisterCount}, {interfaceName} unregister: {trackStorageUnregisterCount}."
            );
        }

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
        RunCase(unity6000_4OrNewer: false);
        RunCase(unity6000_4OrNewer: true);

        static void RunCase(bool unity6000_4OrNewer)
        {
            var parseOptions = CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersion.Preview);

            parseOptions = unity6000_4OrNewer
                ? parseOptions.WithPreprocessorSymbols("MEDICINE_EXTENSIONS_LIB", "UNITY_6000_4_OR_NEWER")
                : parseOptions.WithPreprocessorSymbols("MEDICINE_EXTENSIONS_LIB");

            var compilation = RoslynHarness.CreateCompilation(
                parseOptions,
                Stubs.Core,
                """
                using Medicine;
                using UnityEngine;

                sealed class InterfacePayload { }

                [Track]
                partial interface ITrackSourceGeneratorInterfaceCustomStorage : ICustomStorage<InterfacePayload>,
                #if UNITY_6000_4_OR_NEWER
                    ITrackEntityIDs
                #else
                    ITrackInstanceIDs
                #endif
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
                parseOptions: parseOptions
            );

            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
            var run = driver.GetRunResult();

            RoslynHarness.AssertDoesNotContainDiagnostic(
                diagnostics: run.Diagnostics.ToArray(),
                id: "MED911",
                because: unity6000_4OrNewer
                    ? "interface custom-storage helper generation should not throw on Unity 6000.4+"
                    : "interface custom-storage helper generation should not throw before Unity 6000.4"
            );

            var interfaceName = unity6000_4OrNewer ? "ITrackEntityIDs" : "ITrackInstanceIDs";

            int helperPayloadPropertyCount = 0;
            int helperTrackIdsPropertyCount = 0;
            int helperPayloadRegisterCount = 0;
            int helperPayloadUnregisterCount = 0;
            int helperTrackIdsRegisterCount = 0;
            int helperTrackIdsUnregisterCount = 0;
            foreach (var result in run.Results)
            foreach (var generatedSource in result.GeneratedSources)
            {
                var text = generatedSource.SourceText.ToString();
                helperPayloadPropertyCount += CountOccurrences(text, "ref global::InterfacePayload InterfacePayloadStorage");
                helperTrackIdsPropertyCount += CountOccurrences(text, $"ref global::Medicine.{interfaceName}.Storage Storage");
                helperPayloadRegisterCount += CountOccurrences(text, "Storage.Custom<global::ITrackSourceGeneratorInterfaceCustomStorage, global::InterfacePayload>.Register(this)");
                helperPayloadUnregisterCount += CountOccurrences(text, "Storage.Custom<global::ITrackSourceGeneratorInterfaceCustomStorage, global::InterfacePayload>.Unregister(this,");
                helperTrackIdsRegisterCount += CountOccurrences(text, $"Storage.Custom<global::ITrackSourceGeneratorInterfaceCustomStorage, global::Medicine.{interfaceName}.Storage>.Register(this)");
                helperTrackIdsUnregisterCount += CountOccurrences(text, $"Storage.Custom<global::ITrackSourceGeneratorInterfaceCustomStorage, global::Medicine.{interfaceName}.Storage>.Unregister(this,");
            }

            if (
                helperPayloadPropertyCount >= 1 &&
                helperTrackIdsPropertyCount >= 1 &&
                helperPayloadRegisterCount is 1 &&
                helperPayloadUnregisterCount is 1 &&
                helperTrackIdsRegisterCount is 1 &&
                helperTrackIdsUnregisterCount is 1
            )
                return;

            throw new InvalidOperationException(
                "Expected tracked-interface helper custom storage generation to emit properties and lifecycle calls." + Environment.NewLine +
                $"Unity branch: {interfaceName}. " +
                $"Actual payload property: {helperPayloadPropertyCount}, {interfaceName} property: {helperTrackIdsPropertyCount}, " +
                $"payload register: {helperPayloadRegisterCount}, payload unregister: {helperPayloadUnregisterCount}, " +
                $"{interfaceName} register: {helperTrackIdsRegisterCount}, {interfaceName} unregister: {helperTrackIdsUnregisterCount}."
            );
        }

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

    static void RunNonTrackedInterfaceUnmanagedPropagation()
    {
        var compilation = RoslynHarness.CreateCompilation(
            Stubs.Core,
            """
            using Medicine;
            using UnityEngine;

            struct NonTrackedMarkerData
            {
                public int Value;
            }

            interface INonTrackedMarkerData : IUnmanagedData<NonTrackedMarkerData> { }

            [Track]
            partial class NonTrackedMarkerConsumer : MonoBehaviour, INonTrackedMarkerData { }
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
            because: "non-tracked inherited unmanaged-data marker should be handled without generator exceptions"
        );

        int classRegisterCount = 0;
        int classUnregisterCount = 0;
        foreach (var result in run.Results)
        foreach (var generatedSource in result.GeneratedSources)
        {
            var text = generatedSource.SourceText.ToString();
            classRegisterCount += CountOccurrences(text, "Storage.UnmanagedData<global::NonTrackedMarkerConsumer, global::NonTrackedMarkerData>.Register(this)");
            classUnregisterCount += CountOccurrences(text, "Storage.UnmanagedData<global::NonTrackedMarkerConsumer, global::NonTrackedMarkerData>.Unregister(this, index)");
        }

        if (classRegisterCount is 1 && classUnregisterCount is 1)
            return;

        throw new InvalidOperationException(
            "Expected non-tracked inherited unmanaged-data marker to emit class-level unmanaged registration lifecycle calls." + Environment.NewLine +
            $"Actual class register: {classRegisterCount}, class unregister: {classUnregisterCount}."
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

    static void RunTrackedInterfaceUnmanagedNoImplicitClassPropagation()
    {
        var compilation = RoslynHarness.CreateCompilation(
            Stubs.Core,
            """
            using Medicine;
            using UnityEngine;

            struct TrackedInterfaceData
            {
                public int Value;
            }

            [Track]
            partial interface ITrackedUnmanagedDataCarrier : IUnmanagedData<TrackedInterfaceData> { }

            [Track]
            partial class TrackedUnmanagedDataConsumer : MonoBehaviour, ITrackedUnmanagedDataCarrier { }
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
            because: "tracked-interface unmanaged-data should not rely on class-level unmanaged-data propagation"
        );

        int classRegisterCount = 0;
        int classUnregisterCount = 0;
        int interfaceRegisterCount = 0;
        int interfaceUnregisterCount = 0;
        int localAccessorCount = 0;
        foreach (var result in run.Results)
        foreach (var generatedSource in result.GeneratedSources)
        {
            var text = generatedSource.SourceText.ToString();
            classRegisterCount += CountOccurrences(text, "Storage.UnmanagedData<global::TrackedUnmanagedDataConsumer, global::TrackedInterfaceData>.Register(this)");
            classUnregisterCount += CountOccurrences(text, "Storage.UnmanagedData<global::TrackedUnmanagedDataConsumer, global::TrackedInterfaceData>.Unregister(this, index)");
            interfaceRegisterCount += CountOccurrences(text, "Storage.UnmanagedData<global::ITrackedUnmanagedDataCarrier, global::TrackedInterfaceData>.Register(this)");
            interfaceUnregisterCount += CountOccurrences(text, "Storage.UnmanagedData<global::ITrackedUnmanagedDataCarrier, global::TrackedInterfaceData>.Unregister(this, ");
            localAccessorCount += CountOccurrences(text, "LocalTrackedInterfaceData");
        }

        if (
            classRegisterCount is 0 &&
            classUnregisterCount is 0 &&
            interfaceRegisterCount is 1 &&
            interfaceUnregisterCount is 1 &&
            localAccessorCount is 0
        )
            return;

        throw new InvalidOperationException(
            "Expected tracked-interface unmanaged-data to stay interface-scoped and not be implicitly copied to class unmanaged-data APIs." + Environment.NewLine +
            $"Actual class register: {classRegisterCount}, class unregister: {classUnregisterCount}, interface register: {interfaceRegisterCount}, interface unregister: {interfaceUnregisterCount}, local accessor count: {localAccessorCount}."
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

    static void RunNonTrackedInterfaceUnmanagedNoDerivedClassInheritance()
    {
        var compilation = RoslynHarness.CreateCompilation(
            Stubs.Core,
            """
            using Medicine;
            using UnityEngine;

            struct InheritedMarkerData
            {
                public int Value;
            }

            interface INonTrackedInheritedMarker : IUnmanagedData<InheritedMarkerData> { }

            [Track]
            partial class BaseWithNonTrackedMarker : MonoBehaviour, INonTrackedInheritedMarker { }

            [Track]
            partial class DerivedWithoutMarker : BaseWithNonTrackedMarker { }
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
            because: "non-tracked unmanaged-data markers should be limited to interfaces implemented at each class inheritance level"
        );

        int baseRegisterCount = 0;
        int derivedRegisterCount = 0;
        foreach (var result in run.Results)
        foreach (var generatedSource in result.GeneratedSources)
        {
            var text = generatedSource.SourceText.ToString();
            baseRegisterCount += CountOccurrences(text, "Storage.UnmanagedData<global::BaseWithNonTrackedMarker, global::InheritedMarkerData>.Register(this)");
            derivedRegisterCount += CountOccurrences(text, "Storage.UnmanagedData<global::DerivedWithoutMarker, global::InheritedMarkerData>.Register(this)");
        }

        if (baseRegisterCount is 1 && derivedRegisterCount is 0)
            return;

        throw new InvalidOperationException(
            "Expected only the class that explicitly implements the non-tracked unmanaged-data marker interface to receive class-level unmanaged-data registration." + Environment.NewLine +
            $"Actual base register: {baseRegisterCount}, derived register: {derivedRegisterCount}."
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

    static void RunExplicitClassUnmanagedAlongTrackedInterface()
    {
        var compilation = RoslynHarness.CreateCompilation(
            Stubs.Core,
            """
            using Medicine;
            using UnityEngine;

            struct ExplicitClassData
            {
                public int Value;
            }

            [Track]
            partial interface ITrackedExplicitClassData : IUnmanagedData<ExplicitClassData> { }

            [Track]
            partial class ExplicitClassDataConsumer : MonoBehaviour, ITrackedExplicitClassData, IUnmanagedData<ExplicitClassData> { }
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
            because: "explicit class-level unmanaged-data implementation should remain active alongside tracked-interface unmanaged-data"
        );

        int classRegisterCount = 0;
        int classUnregisterCount = 0;
        int interfaceRegisterCount = 0;
        int interfaceUnregisterCount = 0;
        foreach (var result in run.Results)
        foreach (var generatedSource in result.GeneratedSources)
        {
            var text = generatedSource.SourceText.ToString();
            classRegisterCount += CountOccurrences(text, "Storage.UnmanagedData<global::ExplicitClassDataConsumer, global::ExplicitClassData>.Register(this)");
            classUnregisterCount += CountOccurrences(text, "Storage.UnmanagedData<global::ExplicitClassDataConsumer, global::ExplicitClassData>.Unregister(this, index)");
            interfaceRegisterCount += CountOccurrences(text, "Storage.UnmanagedData<global::ITrackedExplicitClassData, global::ExplicitClassData>.Register(this)");
            interfaceUnregisterCount += CountOccurrences(text, "Storage.UnmanagedData<global::ITrackedExplicitClassData, global::ExplicitClassData>.Unregister(this, ");
        }

        if (classRegisterCount is 1 && classUnregisterCount is 1 && interfaceRegisterCount is 1 && interfaceUnregisterCount is 1)
            return;

        throw new InvalidOperationException(
            "Expected explicit class-level unmanaged-data and tracked-interface unmanaged-data to both emit lifecycle calls exactly once." + Environment.NewLine +
            $"Actual class register: {classRegisterCount}, class unregister: {classUnregisterCount}, interface register: {interfaceRegisterCount}, interface unregister: {interfaceUnregisterCount}."
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
