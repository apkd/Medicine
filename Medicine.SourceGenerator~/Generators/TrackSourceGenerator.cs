using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static ActivePreprocessorSymbolNames;
using static Constants;

// ReSharper disable RedundantStringInterpolation

[Generator]
public sealed class TrackSourceGenerator : IIncrementalGenerator
{
    void IIncrementalGenerator.Initialize(IncrementalGeneratorInitializationContext context)
    {
        var medicineSettings = context.CompilationProvider
            .Combine(context.ParseOptionsProvider)
            .Select((x, ct) => new MedicineSettings(x, ct));

        foreach (var attributeName in new[] { SingletonAttributeMetadataName, TrackAttributeMetadataName })
        {
            context.RegisterImplementationSourceOutputEx(
                context.SyntaxProvider
                    .ForAttributeWithMetadataNameEx(
                        fullyQualifiedMetadataName: attributeName,
                        predicate: static (node, _) => node is ClassDeclarationSyntax,
                        transform: (attributeSyntaxContext, ct)
                            => TransformSyntaxContext(attributeSyntaxContext, ct, attributeName, $"global::{attributeName}")
                    )
                    .Where(x => x.Attribute is { Length: > 0 })
                    .Combine(medicineSettings)
                    .Select((x, ct) => x.Left with
                        {
                            HasIInstanceIndex = x.Left.HasIInstanceIndex || x.Right.AlwaysTrackInstanceIndices,
                            EmitIInstanceIndex = x.Left.EmitIInstanceIndex || x.Right.AlwaysTrackInstanceIndices,
                            Symbols = x.Right.PreprocessorSymbolNames,
                        }
                    ),
                GenerateSource
            );
        }
    }

    readonly record struct TrackAttributeSettings(
        bool TrackInstanceIDs,
        bool TrackTransforms,
        int InitialCapacity,
        int DesiredJobCount,
        bool CacheEnabledState,
        bool Manual
    );

    record struct GeneratorInput : IGeneratorTransformOutput
    {
        public string? SourceGeneratorOutputFilename { get; init; }
        public string? SourceGeneratorError { get; init; }
        public EquatableIgnore<Location?> SourceGeneratorErrorLocation { get; set; }

        public string? Attribute;
        public TrackAttributeSettings AttributeSettings;
        public EquatableArray<string> ContainingTypeDeclaration;
        public EquatableArray<string> InterfacesWithAttribute;
        public EquatableArray<string> UnmanagedDataFQNs;
        public EquatableArray<string> TrackingIdFQNs;
        public string? TypeFQN;
        public string? TypeDisplayName;
        public ActivePreprocessorSymbolNames Symbols;
        public bool HasIInstanceIndex;
        public bool EmitIInstanceIndex;
        public bool IsSealed;
        public bool HasBaseDeclarationsWithAttribute;
        public bool IsComponent;
        public bool IsGenericType;
    }

    static GeneratorInput TransformSyntaxContext(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct,
        string attributeName,
        string attributeFQN
    )
    {
        if (context.TargetNode is not TypeDeclarationSyntax typeDeclaration)
            return default;

        if (context.TargetSymbol is not INamedTypeSymbol { TypeKind: TypeKind.Class } classSymbol)
            return default;

        if (context.Attributes.FirstOrDefault() is not { AttributeConstructor: not null } attributeData)
            return default;

        var attributeArguments = attributeData
            .GetAttributeConstructorArguments(ct)
            .Select(x => new TrackAttributeSettings(
                    TrackInstanceIDs: x.Get("instanceIdArray", false),
                    TrackTransforms: x.Get("transformAccessArray", false) && classSymbol.InheritsFrom("global::UnityEngine.MonoBehaviour"),
                    InitialCapacity: x.Get("transformInitialCapacity", 64),
                    DesiredJobCount: x.Get("transformDesiredJobCount", -1),
                    CacheEnabledState: x.Get("cacheEnabledState", false),
                    Manual: x.Get("manual", false)
                )
            );

        return new()
        {
            SourceGeneratorOutputFilename = Utility.GetOutputFilename(
                filePath: typeDeclaration.SyntaxTree.FilePath,
                targetFQN: typeDeclaration.Identifier.ValueText,
                label: attributeName,
                includeFilename: false
            ),
            ContainingTypeDeclaration = Utility.DeconstructTypeDeclaration(typeDeclaration, context.SemanticModel, ct),
            Attribute = attributeName,
            AttributeSettings = attributeArguments,
            UnmanagedDataFQNs = classSymbol.Interfaces
                .Where(x => x.FQN?.StartsWith(UnmanagedDataInterfaceFQN) is true)
                .Select(x => x.TypeArguments.FirstOrDefault()?.FQN ?? "")
                .ToArray(),
            TrackingIdFQNs = classSymbol.Interfaces
                .Where(x => x.FQN?.StartsWith(TrackingIdInterfaceFQN) is true)
                .Select(x => x.TypeArguments.FirstOrDefault()?.FQN ?? "")
                .ToArray(),
            HasIInstanceIndex = classSymbol.HasInterface(IInstanceIndexInterfaceFQN),
            EmitIInstanceIndex = classSymbol.HasInterface(IInstanceIndexInterfaceFQN) && !classSymbol.HasInterface(IInstanceIndexInterfaceFQN, checkAllInterfaces: false),
            TypeFQN = classSymbol.FQN,
            TypeDisplayName = classSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat).HtmlEncode(),
            IsSealed = classSymbol.IsSealed,
            IsGenericType = classSymbol.IsGenericType,
            IsComponent = classSymbol.InheritsFrom("global::UnityEngine.Component"),
            InterfacesWithAttribute
                = classSymbol.Interfaces
                    .Where(x => x.HasAttribute(attributeFQN))
                    .Where(x => !classSymbol.GetBaseTypes().Any(y => y.Interfaces.Contains(x))) // skip interfaces already registered via base class
                    .Select(x => x.FQN)
                    .Where(x => x != null)
                    .ToArray(),
            HasBaseDeclarationsWithAttribute
                = classSymbol
                    .GetBaseTypes()
                    .Any(x => x.HasAttribute(attributeFQN)),
        };
    }

    static void GenerateSource(SourceProductionContext context, SourceWriter src, GeneratorInput input)
    {
        src.Line.Write("#pragma warning disable CS8321 // Local function is declared but never used");
        src.Line.Write("#pragma warning disable CS0618 // Type or member is obsolete");
        src.Line.Write(Alias.UsingStorage);
        src.Line.Write(Alias.UsingInline);
        src.Line.Write(Alias.UsingUtility);
        src.Line.Write(Alias.UsingNonSerialized);
        src.Linebreak();

        if (input.Attribute is TrackAttributeMetadataName)
        {
            input.HasIInstanceIndex |= input.EmitIInstanceIndex;
        }
        else
        {
            input.EmitIInstanceIndex = false;
            input.HasIInstanceIndex = false;
        }

        string @protected = input.IsSealed ? "" : "protected ";
        string @new = input.HasBaseDeclarationsWithAttribute ? "new " : "";

        var unmanagedDataInfo = input.UnmanagedDataFQNs.AsArray()
            .Select(x => (dataType: x, dataTypeShort: x.Split('.', ':').Last().HtmlEncode()))
            .ToArray();

        var declarations = input.ContainingTypeDeclaration.AsSpan();
        int lastDeclaration = declarations.Length - 1;
        for (int i = 0; i < declarations.Length; i++)
        {
            if (i == lastDeclaration && input.Symbols.Has(UNITY_EDITOR))
            {
                if (input.Attribute is TrackAttributeMetadataName)
                {
                    string active = input.IsComponent ? "(enabled) instances of this component" : "instances of this class";
                    src.Line.Write($"/// <remarks>");
                    src.Line.Write($"/// Active {active} are tracked, and contain additional generated static properties:");
                    src.Line.Write($"/// <list type=\"bullet\">");
                    WriteGeneratedArraysComment();
                    src.Line.Write($"/// </list>");
                    if (input.HasIInstanceIndex)
                    {
                        src.Line.Write($"/// And the following additional instance properties:");
                        src.Line.Write($"/// <list type=\"bullet\">");
                        src.Line.Write($"/// <item> The <see cref=\"{input.TypeDisplayName}.InstanceIndex\"/> property, which is the instance's index into the above arrays </item>");
                        foreach (var (_, dataTypeShort) in unmanagedDataInfo)
                            src.Line.Write($"/// <item> The <see cref=\"{input.TypeDisplayName}.Local{dataTypeShort}\"/> per-instance data accessor </item>");

                        src.Line.Write($"/// </list>");
                    }

                    src.Line.Write("/// </remarks>");
                }
                else if (input.Attribute is SingletonAttributeMetadataName)
                {
                    src.Line.Write($"/// <remarks>");
                    src.Line.Write($"/// This is a singleton class. The current instance of the singleton can be accessed via the");
                    src.Line.Write($"/// generated <see cref=\"{input.TypeDisplayName}.Instance\"/> static property.");
                    src.Line.Write($"/// </remarks>");
                }
            }

            src.Line.Write(declarations[i]);

            if (i == lastDeclaration)
            {
                if (input.HasIInstanceIndex)
                    src.Write($" : {IInstanceIndexInternalInterfaceFQN}<{input.TypeFQN}>");

                if (input.EmitIInstanceIndex)
                    src.Write($", {IInstanceIndexInterfaceFQN}");
            }

            src.Line.Write('{');
            src.IncreaseIndent();
        }

        void EmitRegistrationMethod(string methodName, params string?[] methodCalls)
        {
            if (!input.AttributeSettings.Manual)
            {
                src.Line.Write(Alias.Hidden);
                src.Line.Write(Alias.ObsoleteInternal);
            }
            else
            {
                bool register = methodName is "OnEnableINTERNAL";
                methodName = register
                    ? $"RegisterInstance"
                    : $"UnregisterInstance";

                if (input.Symbols.Has(UNITY_EDITOR))
                {
                    src.Line.Write($"/// <summary>");
                    src.Line.Write($"/// Manually {(register ? "registers" : "unregisters")} this instance {(register ? "in" : "from")} the <c>{input.Attribute}</c> storage.");
                    src.Line.Write($"/// </summary>");
                    src.Line.Write($"/// <remarks>");
                    src.Line.Write($"/// You <b>must ensure</b> that the instance always registers and unregisters itself symmetrically.");
                    src.Line.Write($"/// This is usually achieved by hooking into reliable object lifecycle methods, such as <c>OnEnable</c>+<c>OnDisable</c>,");
                    src.Line.Write($"/// or <c>Awake</c>+<c>OnDestroy</c>. Make sure the registration methods are never stopped by an earlier exception.");
                    src.Line.Write($"/// </remarks>");
                }
            }

            src.Line.Write($"{@protected}{@new}void {methodName}()");

            using (src.Braces)
            {
                if (input.HasBaseDeclarationsWithAttribute)
                    src.Line.Write($"base.{methodName}();");

                foreach (var methodCall in methodCalls)
                    if (methodCall is not null)
                        src.Line.Write(methodCall).Write(';');
            }

            src.Linebreak();
        }

        void WriteGeneratedArraysComment()
        {
            src.Line.Write($"/// <item> The <see cref=\"{input.TypeDisplayName}.Instances\"/> tracked instance list </item>");

            if (input.AttributeSettings.TrackInstanceIDs)
                src.Line.Write($"/// <item> The <see cref=\"{input.TypeDisplayName}.InstanceIDs\"/> instance ID array </item>");

            if (input.AttributeSettings.TrackTransforms)
                src.Line.Write($"/// <item> The <see cref=\"{input.TypeDisplayName}.TransformAccessArray\"/> transform array </item>");

            foreach (var (_, dataTypeShort) in unmanagedDataInfo)
                src.Line.Write($"/// <item> The <see cref=\"{input.TypeDisplayName}.Unmanaged.{dataTypeShort}Array\"/> data array </item>");
        }

        if (input.HasIInstanceIndex)
        {
            if (input.Attribute is SingletonAttributeMetadataName)
            {
                src.Line.Write($"#error The IInstanceIndex interface is invalid for singleton type {input.TypeDisplayName}.");
            }
            else
            {
                if (input.Symbols.Has(UNITY_EDITOR))
                {
                    src.Line.Write($"/// <summary>");
                    src.Line.Write($"/// Represents the index of this instance in the following static storage arrays:");
                    src.Line.Write($"/// <list type=\"bullet\">");
                    WriteGeneratedArraysComment();
                    src.Line.Write($"/// </list>");
                    src.Line.Write($"/// </summary>");
                    src.Line.Write($"/// <remarks>");
                    src.Line.Write($"/// This property is automatically updated.");
                    src.Line.Write($"/// Note that the instance index will change during the lifetime of the instance - never store it.");
                    src.Line.Write($"/// <br/><br/>");
                    src.Line.Write($"/// A value of -1 indicates that the instance is not currently active/registered.");
                    src.Line.Write($"/// </remarks>");
                }
            }

            src.Line.Write($"public int InstanceIndex => {m}MedicineInternalInstanceIndex;");
            src.Linebreak();

            src.Line.Write($"int {IInstanceIndexInterfaceFQN}.InstanceIndex");
            using (src.Braces)
            {
                src.Line.Write($"{Alias.Inline} get => {m}MedicineInternalInstanceIndex;");
                src.Line.Write($"{Alias.Inline} set => {m}MedicineInternalInstanceIndex = value;");
            }

            src.Line.Write($"int {IInstanceIndexInternalInterfaceFQN}<{input.TypeFQN}>.InstanceIndex");
            using (src.Braces)
            {
                src.Line.Write($"{Alias.Inline} get => {m}MedicineInternalInstanceIndex;");
                src.Line.Write($"{Alias.Inline} set => {m}MedicineInternalInstanceIndex = value;");
            }

            src.Linebreak();

            src.Line.Write(Alias.Hidden);
            src.Line.Write($"int {m}MedicineInternalInstanceIndex = -1;");
            src.Linebreak();
        }

        if (input.AttributeSettings.TrackTransforms)
        {
            if (input.Symbols.Has(UNITY_EDITOR))
            {
                src.Line.Write($"/// <summary>");
                src.Line.Write($"/// Allows job access to the transforms of the tracked {input.TypeDisplayName} instances.");
                src.Line.Write($"/// </summary>");
            }

            src.Line.Write($"{@new}public static global::UnityEngine.Jobs.TransformAccessArray TransformAccessArray");
            using (src.Braces)
            {
                src.Line.Write(Alias.Inline).Write(" get");
                using (src.Braces)
                {
                    src.Line.Write($"return {m}Storage.TransformAccess<{input.TypeFQN}>.Transforms;");
                    src.Linebreak();

                    if (!input.IsGenericType)
                    {
                        if (input.Symbols.Has(UNITY_EDITOR))
                            src.Line.Write("[global::UnityEditor.InitializeOnLoadMethod]");

                        src.Line.Write("[global::UnityEngine.RuntimeInitializeOnLoadMethod(global::UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]");
                        src.Line.Write($"static void {m}Init()");

                        using (src.Indent)
                            src.Line.Write($"=> {m}Storage.TransformAccess<{input.TypeFQN}>.Initialize")
                                .Write($"({input.AttributeSettings.InitialCapacity}, {input.AttributeSettings.DesiredJobCount});");
                    }
                }
            }

            src.Linebreak();
        }

        if (input is { IsGenericType: true, AttributeSettings.TrackTransforms: true })
        {
            src.Line.Write($"public static void InitializeTransformAccessArray()");

            using (src.Indent)
            {
                src.Line
                    .Write($"=> {m}Storage.TransformAccess<{input.TypeFQN}>.Initialize")
                    .Write($"({input.AttributeSettings.InitialCapacity}, {input.AttributeSettings.DesiredJobCount});");
            }

            src.Linebreak();
        }

        if (input.AttributeSettings.TrackInstanceIDs)
        {
            if (input.Symbols.Has(UNITY_EDITOR))
            {
                src.Line.Write($"/// <summary>");
                src.Line.Write($"/// Gets an array of instance IDs for the tracked type's enabled instances.");
                src.Line.Write($"/// </summary>");
                src.Line.Write($"/// <seealso href=\"https://docs.unity3d.com/ScriptReference/Resources.InstanceIDToObjectList.html\">Resources.InstanceIDToObjectList</seealso>");
                src.Line.Write($"/// <remarks>");
                src.Line.Write($"/// The instance IDs can be used to refer to tracked objects in unmanaged contexts, e.g.,");
                src.Line.Write($"/// jobs and Burst-compiled functions.");
                src.Line.Write($"/// <list type=\"bullet\">");
                src.Line.Write($"/// <item>Requires the <c>[Track(instanceIDs: true)]</c> attribute parameter to be set.</item>");
                src.Line.Write($"/// <item>The order of instance IDs will correspond to the order of tracked transforms.</item>");
                src.Line.Write($"/// </list>");
                src.Line.Write($"/// </remarks>");
            }

            src.Line.Write($"{@new}public static global::Unity.Collections.NativeArray<int> InstanceIDs");
            using (src.Braces)
                src.Line.Write(Alias.Inline).Write($" get => {m}Storage.InstanceIDs<{input.TypeFQN}>.List.AsArray();");

            src.Linebreak();
        }

        if (input.UnmanagedDataFQNs.Length > 0)
        {
            if (input.Symbols.Has(UNITY_EDITOR))
            {
                src.Line.Write($"/// <summary>");
                src.Line.Write($"/// Allows job access to the unmanaged data arrays of the tracked {input.TypeDisplayName} instances");
                src.Line.Write($"/// of this component type.");
                src.Line.Write($"/// </summary>");
                src.Line.Write($"/// <remarks>");
                src.Line.Write($"/// The unmanaged data is stored in a <see cref=\"global::Unity.Collections.NativeArray{{T}}\"/>, where each element");
                src.Line.Write($"/// corresponds to the tracked instance with the appropriate <see cref=\"Medicine.IInstanceIndex\">instance index</see>.");
                src.Line.Write($"/// </remarks>");
                string s = input.UnmanagedDataFQNs.Length > 1 ? "s" : "";
                string origin = unmanagedDataInfo.Select(x => $"<c>IUnmanagedData&lt;{x.dataTypeShort}&gt;</c>").Join(", ");
                src.Line.Write($"/// <codegen>Generated because of the implemented interface{s}: {origin}</codegen>");
            }

            src.Line.Write($"public static class Unmanaged");
            using (src.Braces)
            {
                foreach (var (dataType, dataTypeShort) in unmanagedDataInfo)
                {
                    src.Linebreak();
                    if (input.Symbols.Has(UNITY_EDITOR))
                    {
                        src.Line.Write($"/// <summary>");
                        src.Line.Write($"/// Gets an array of <see cref=\"{dataTypeShort}\"/> data for the tracked type's currently active instances.");
                        src.Line.Write($"/// You can use this array in jobs or Burst-compiled functions.");
                        src.Line.Write($"/// </summary>");
                        src.Line.Write($"/// <remarks>");
                        src.Line.Write($"/// Each element in the native array corresponds to the tracked instance with the appropriate instance index.");
                        src.Line.Write($"/// Implementing the <see cref=\"Medicine.IInstanceIndex\"/> interface will generate a property that lets each");
                        src.Line.Write($"/// instance access its own data. Otherwise, you can access the data statically via this array, and");
                        src.Line.Write($"/// initialize/dispose it by overriding the methods in the <see cref=\"Medicine.IUnmanagedData{{T}}\"/> interface.");
                        src.Line.Write($"/// </remarks>");
                        src.Line.Write($"/// <codegen>Generated because of the following implemented interface: <c>IUnmanagedData&lt;{dataTypeShort}&gt;</c></codegen>");
                    }

                    src.Line.Write($"public static ref global::Unity.Collections.NativeArray<{dataType}> {dataTypeShort}Array");

                    using (src.Braces)
                        src.Line.Write($"{Alias.Inline} get => ref {m}Storage.UnmanagedData<{input.TypeFQN}, {dataType}>.Array;");
                }
            }

            if (input.HasIInstanceIndex)
            {
                src.Linebreak();
                foreach (var (dataType, dataTypeShort) in unmanagedDataInfo)
                {
                    src.Linebreak();
                    if (input.Symbols.Has(UNITY_EDITOR))
                    {
                        src.Line.Write($"/// <summary>");
                        src.Line.Write($"/// Gets a reference to the <see cref=\"{dataTypeShort}\"/> data for the tracked type's currently active instance.");
                        src.Line.Write($"/// You can use this reference in jobs or Burst-compiled functions.");
                        src.Line.Write($"/// </summary>");
                        src.Line.Write($"/// <remarks>");
                        src.Line.Write($"/// The reference corresponds to the tracked instance with the appropriate instance index.");
                        src.Line.Write($"/// Implementing the <see cref=\"Medicine.IInstanceIndex\"/> interface will generate a property that lets each");
                        src.Line.Write($"/// instance access its own data. Otherwise, you can access the data statically via this array, and");
                        src.Line.Write($"/// initialize/dispose it by overriding the methods in the <see cref=\"Medicine.IUnmanagedData{{T}}\"/> interface.");
                    }

                    src.Line.Write($"public ref {dataType} Local{dataTypeShort}");

                    using (src.Braces)
                    {
                        src.Line.Write($"{Alias.Inline} get");
                        using (src.Indent)
                            src.Line.Write($"=> ref {m}Storage.UnmanagedData<{input.TypeFQN}, {dataType}>.ElementAtRefRW({m}MedicineInternalInstanceIndex);");
                    }
                }
            }

            src.Linebreak();
        }

        if (input.TrackingIdFQNs is { Length: > 0 })
        {
            foreach (var idType in input.TrackingIdFQNs)
            {
                src.Line.Write(Alias.Inline);
                src.Line.Write($"public static {input.TypeFQN}? FindByID({idType} id)");

                using (src.Indent)
                    src.Line.Write($"=> {m}Storage.LookupByID<{input.TypeFQN}, {idType}>.Map.TryGetValue(id, out var result) ? result : null;");

                src.Linebreak();

                src.Line.Write(Alias.Inline);
                src.Line.Write($"public static bool TryFindByID({idType} id, out {input.TypeFQN} result)");

                using (src.Indent)
                    src.Line.Write($"=> {m}Storage.LookupByID<{input.TypeFQN}, {idType}>.Map.TryGetValue(id, out result);");

                src.Linebreak();
            }
        }

        if (input.Attribute is SingletonAttributeMetadataName)
        {
            if (input.Symbols.Has(UNITY_EDITOR))
            {
                src.Line.Write($"/// <summary>");
                src.Line.Write($"/// Retrieves the active <see cref=\"{input.TypeDisplayName}\"/> singleton instance.");
                src.Line.Write($"/// </summary>");
                src.Line.Write($"/// <remarks>");
                src.Line.Write($"/// <list type=\"bullet\">");
                src.Line.Write($"/// <item> This property <b>might return null</b> if the singleton instance has not been registered yet");
                src.Line.Write($"/// (or has been disabled/destroyed). </item>");
                src.Line.Write($"/// <item> MonoBehaviours and ScriptableObjects marked with the <see cref=\"SingletonAttribute\"/> will");
                src.Line.Write($"/// automatically register/unregister themselves as the active singleton instance in OnEnable/OnDisable. </item>");
                src.Line.Write($"/// <item> In edit mode, to provide better compatibility with editor tooling, <c>FindObjectsOfType</c>");
                src.Line.Write($"/// is used internally to attempt to locate the object (cached for one editor update). </item>");
                src.Line.Write($"/// </list>");
                src.Line.Write($"/// </remarks>");
            }

            src.Line.Write($"public static {input.TypeFQN}? Instance");

            using (src.Braces)
            {
                src.Line.Write(Alias.Inline);
                src.Line.Write($"get => {m}Storage.Singleton<{input.TypeFQN}>.Instance;");
            }

            src.Linebreak();
            EmitRegistrationMethod("OnEnableINTERNAL", $"{m}Storage.Singleton<{input.TypeFQN}>.Register(this)");
            EmitRegistrationMethod("OnDisableINTERNAL", $"{m}Storage.Singleton<{input.TypeFQN}>.Unregister(this)");

            if (input.Symbols.Has(UNITY_EDITOR))
            {
                src.Linebreak();
                src.Line.Write(Alias.Hidden);
                src.Line.Write(Alias.ObsoleteInternal);
                src.Line.Write($"[{m}NS] int {m}MedicineInvalidateInstanceToken = {m}Storage.Singleton<{input.TypeFQN}>.EditMode.Invalidate();");
            }
        }
        else if (input.Attribute is TrackAttributeMetadataName)
        {
            if (input.Symbols.Has(UNITY_EDITOR))
            {
                src.Line.Write($"/// <summary>");
                src.Line.Write($"/// Allows enumeration of all enabled instances of <see cref=\"{input.TypeDisplayName?.HtmlEncode()}\"/>.");
                src.Line.Write($"/// </summary>");
                src.Line.Write($"/// <remarks>");
                src.Line.Write($"/// <list type=\"bullet\">");
                src.Line.Write($"/// <item> MonoBehaviours and ScriptableObjects marked with the <see cref=\"TrackAttribute\"/> will automatically register/unregister themselves");
                src.Line.Write($"/// in the active instance list in OnEnable/OnDisable. </item>");
                src.Line.Write($"/// <item> This property can return null if the singleton instance doesn't exist or hasn't executed OnEnabled yet. </item>");
                src.Line.Write($"/// <item> In edit mode, to provide better compatibility with editor tooling, <see cref=\"Object.FindObjectsByType(System.Type,UnityEngine.FindObjectsSortMode)\"/>");
                src.Line.Write($"/// is used internally to find object instances (cached for one editor update). </item>");
                src.Line.Write($"/// <item> You can use <c>foreach</c> to iterate over the instances. </item>");
                src.Line.Write($"/// <item> If you’re enabling/disabling instances while enumerating, you need to use <c>{input.TypeDisplayName}.Instances.Copied()</c>. </item>");
                src.Line.Write($"/// <item> The returned struct is compatible with <a href=\"https://github.com/Cysharp/ZLinq\">ZLINQ</a>. </item>");
                src.Line.Write($"/// </list>");
                src.Line.Write($"/// </remarks>");
            }

            src.Line.Write($"public static global::Medicine.Internal.TrackedInstances<{input.TypeFQN}> Instances");
            using (src.Braces)
            {
                src.Line.Write($"{Alias.Inline} get => default;");
            }

            src.Linebreak();
            string withId = input.AttributeSettings.TrackInstanceIDs ? "WithInstanceID" : "";

            EmitRegistrationMethod(
                methodName: "OnEnableINTERNAL",
                methodCalls:
                [
                    // keep first
                    input.AttributeSettings.CacheEnabledState ? $"{m}MedicineInternalCachedEnabledState = true" : null,
                    $"{m}Storage.Instances<{input.TypeFQN}>.Register{withId}(this)",

                    input.AttributeSettings.TrackTransforms ? $"{m}Storage.TransformAccess<{input.TypeFQN}>.Register(transform)" : null,
                    ..input.InterfacesWithAttribute.AsArray().Select(x => $"{m}Storage.Instances<{x}>.Register(this)"),
                    ..input.UnmanagedDataFQNs.AsArray().Select(x => $"{m}Storage.UnmanagedData<{input.TypeFQN}, {x}>.Register(this)"),
                    ..input.TrackingIdFQNs.AsArray().Select(x => $"{m}Storage.LookupByID<{input.TypeFQN}, {x}>.Register(this)"),
                ]
            );

            EmitRegistrationMethod(
                methodName: "OnDisableINTERNAL",
                methodCalls:
                [
                    // keep first
                    input.AttributeSettings.CacheEnabledState ? $"{m}MedicineInternalCachedEnabledState = false" : null,
                    $"int index = {m}Storage.Instances<{input.TypeFQN}>.Unregister{withId}(this)",
                    // rest in reverse
                    ..input.TrackingIdFQNs.AsArray().Select(x => $"{m}Storage.LookupByID<{input.TypeFQN}, {x}>.Unregister(this)").Reverse(),
                    ..input.UnmanagedDataFQNs.AsArray().Select(x => $"{m}Storage.UnmanagedData<{input.TypeFQN}, {x}>.Unregister(this, index)").Reverse(),
                    ..input.InterfacesWithAttribute.AsArray().Select(x => $"{m}Storage.Instances<{x}>.Unregister(this)").Reverse(),
                    input.AttributeSettings.TrackTransforms ? $"{m}Storage.TransformAccess<{input.TypeFQN}>.Unregister(index)" : null,
                ]
            );

            if (input.Symbols.Has(UNITY_EDITOR))
            {
                src.Linebreak();
                src.Line.Write(Alias.Hidden);
                src.Line.Write(Alias.ObsoleteInternal);
                src.Line.Write($"[{m}NS] int {m}MedicineInvalidateInstanceToken = {m}Storage.Instances<{input.TypeFQN}>.EditMode.Invalidate();");
            }

            if (input.AttributeSettings.CacheEnabledState)
            {
                src.Linebreak();
                src.Line.Write(Alias.Hidden);
                src.Line.Write($"bool {m}MedicineInternalCachedEnabledState;");

                src.Linebreak();
                src.Line.Write($"/// <summary>");
                src.Line.Write($"/// Enabled components are updated, disabled components are not.");
                src.Line.Write($"/// </summary>");
                src.Line.Write($"/// <remarks>");
                src.Line.Write($"/// This property returns the cached value of <see cref=\"UnityEngine.Behaviour.enabled\"/>.");
                src.Line.Write($"/// Setting this property normally updates the component's enabled state.");
                src.Line.Write($"/// The cached value is automatically updated when the component is enabled/disabled.<br/><br/>");
                src.Line.Write($"/// This generated property effectively hides the built-in <c>enabled</c>");
                src.Line.Write($"/// property, and tries to exactly replicate its behaviour.");
                src.Line.Write($"/// </remarks>");
                src.Line.Write($"/// <codegen>Generated because of the <c>TrackAttribute</c> parameter: <c>cacheEnabledState</c></codegen>");
                src.Line.Write($"public new bool enabled");
                using (src.Braces)
                {
                    src.Line.Write($"{Alias.Inline}");
                    src.Line.Write($"get");
                    using (src.Braces)
                    {
                        src.Line.Write($"if ({m}Utility.EditMode)");
                        src.Line.Write($"return base.enabled;");

                        src.Line.Write($"return {m}MedicineInternalCachedEnabledState;");
                    }

                    src.Line.Write($"{Alias.Inline} set => base.enabled = value;");
                }
            }
        }

        src.TrimEndWhitespace();
        foreach (var _ in declarations)
        {
            src.DecreaseIndent();
            src.Line.Write('}');
        }

        src.Linebreak();
    }
}