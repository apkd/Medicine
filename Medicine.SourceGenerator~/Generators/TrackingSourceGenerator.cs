using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Constants;

[Generator]
public sealed class TrackingSourceGenerator : BaseSourceGenerator, IIncrementalGenerator
{
    void IIncrementalGenerator.Initialize(IncrementalGeneratorInitializationContext context)
    {
        var medicineSettings = context.CompilationProvider
            .Select((x, ct) => new MedicineSettings(x));

        foreach (var attributeName in new[] { SingletonAttributeMetadataName, TrackAttributeMetadataName })
        {
            context.RegisterImplementationSourceOutput(
                context.SyntaxProvider
                    .ForAttributeWithMetadataName(
                        fullyQualifiedMetadataName: attributeName,
                        predicate: static (node, _) => node is ClassDeclarationSyntax,
                        transform: WrapTransform((attributeSyntaxContext, ct)
                            => TransformSyntaxContext(attributeSyntaxContext, ct, attributeName, $"global::Medicine.{attributeName}")
                        )
                    )
                    .Where(x => x.Attribute is { Length: > 0 })
                    .Combine(medicineSettings)
                    .Select((x, ct) => x.Left with { EmitIInstanceIndex = x.Right.AlwaysTrackInstanceIndices }),
                WrapGenerateSource<GeneratorInput>(GenerateSource)
            );
        }
    }

    record struct GeneratorInput : IGeneratorTransformOutput
    {
        public string? SourceGeneratorOutputFilename { get; init; }
        public string? SourceGeneratorError { get; set; }
        public EquatableIgnore<Location> SourceGeneratorErrorLocation { get; set; }

        public string? Attribute;
        public (bool TrackInstanceIDs, bool TrackTransforms, int InitialCapacity, int DesiredJobCount, bool Manual) AttributeArguments;
        public EquatableArray<string> ContainingTypeDeclaration;
        public EquatableArray<string> InterfacesWithAttribute;
        public EquatableArray<string> UnmanagedDataFQNs;
        public string? TypeFQN;
        public string? TypeDisplayName;
        public bool HasIInstanceIndex;
        public bool EmitIInstanceIndex;
        public bool IsSealed;
        public bool IsUnityEditorCompile;
        public bool HasBaseDeclarationsWithAttribute;
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

        if (context.Attributes.FirstOrDefault() is not { AttributeConstructor.Parameters: var attributeCtorParameters } attributeData)
            return default;

        var attributeArguments = attributeData
            .GetAttributeConstructorArguments()
            .Select(x => (
                    TrackInstanceIDs: x.Get("instanceIdArray", false),
                    TrackTransforms: x.Get("transformAccessArray", false) && classSymbol.InheritsFrom("global::UnityEngine.MonoBehaviour"),
                    InitialCapacity: x.Get("transformInitialCapacity", 64),
                    DesiredJobCount: x.Get("transformDesiredJobCount", -1),
                    Manual: x.Get("manual", false)
                )
            );

        var preprocessorSymbolNames = context.SemanticModel
            .SyntaxTree.Options.PreprocessorSymbolNames
            .ToArray();

        return new()
        {
            SourceGeneratorOutputFilename = GetOutputFilename(typeDeclaration.SyntaxTree.FilePath, typeDeclaration.Identifier.ValueText, attributeName),
            ContainingTypeDeclaration = Utility.DeconstructTypeDeclaration(typeDeclaration),
            Attribute = attributeName,
            AttributeArguments = attributeArguments,
            UnmanagedDataFQNs = classSymbol.Interfaces
                .Where(x => x.GetFQN()?.StartsWith(UnmanagedDataInterfaceFQN) is true)
                .Select(x => x.TypeArguments.FirstOrDefault().GetFQN()!)
                .ToArray(),
            HasIInstanceIndex = classSymbol.HasInterface(IInstanceIndexInterfaceFQN),
            TypeFQN = classSymbol.GetFQN()!,
            TypeDisplayName = classSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            IsSealed = classSymbol.IsSealed,
            IsUnityEditorCompile = preprocessorSymbolNames.Contains("UNITY_EDITOR"),
            InterfacesWithAttribute
                = classSymbol.Interfaces
                    .Where(x => x.HasAttribute(attributeFQN))
                    .Where(x => !classSymbol.GetBaseTypes().Any(y => y.Interfaces.Contains(x))) // skip interfaces already registered via base class
                    .Select(x => x.GetFQN()!)
                    .Where(x => x != null)
                    .ToArray(),
            HasBaseDeclarationsWithAttribute
                = classSymbol
                    .GetBaseTypes()
                    .Any(x => x.HasAttribute(attributeFQN)),
        };
    }

    void GenerateSource(SourceProductionContext context, GeneratorInput input)
    {
        Line.Write("#pragma warning disable CS8321 // Local function is declared but never used");
        Line.Write("#pragma warning disable CS0618 // Type or member is obsolete");
        Line.Write(Alias.UsingStorage);
        Line.Write(Alias.UsingInline);
        Linebreak();

        input.EmitIInstanceIndex &= !input.HasIInstanceIndex;
        input.HasIInstanceIndex |= input.EmitIInstanceIndex;

        string @protected = input.IsSealed ? "" : "protected ";
        string @new = input.HasBaseDeclarationsWithAttribute ? "new " : "";

        var unmanagedDataInfo = input.UnmanagedDataFQNs.AsArray()
            .Select(x => (dataType: x, dataTypeShort: x.Split('.', ':').Last()))
            .ToArray();

        var declarations = input.ContainingTypeDeclaration.AsSpan();
        int lastDeclaration = declarations.Length - 1;
        for (int i = 0; i < declarations.Length; i++)
        {
            if (i == lastDeclaration && input.IsUnityEditorCompile)
            {
                if (input.Attribute is TrackAttributeMetadataName)
                {
                    Line.Write("/// <remarks>");
                    Line.Write($"/// The instances of this class are tracked, and contains additional generated properties:");
                    Line.Write($"/// <list type=\"bullet\">");
                    EmitGeneratedPropertiesListComment();
                    if (input.HasIInstanceIndex)
                        Line.Write($"/// <item> The <see cref=\"{input.TypeDisplayName}.InstanceIndex\"/> property, which is the instance's index into the above </item>");

                    Line.Write($"/// </list>");
                    Line.Write("/// </remarks>");
                }
                else if (input.Attribute is SingletonAttributeMetadataName)
                {
                    Line.Write("/// <remarks>");
                    Line.Write($"/// This is a singleton class. The current instance of the singleton can be accessed via the");
                    Line.Write($"/// generated <see cref=\"{input.TypeDisplayName}.Instance\"/> static property.");
                    Line.Write("/// </remarks>");
                }
            }

            Line.Write(declarations[i]);
            if (i == lastDeclaration && input.EmitIInstanceIndex)
                Text.Append($" : {IInstanceIndexInterfaceFQN}");

            Line.Write('{');
            IncreaseIndent();
        }

        void EmitRegistrationMethod(string methodName, params string?[] methodCalls)
        {
            if (!input.AttributeArguments.Manual)
            {
                Line.Write(Alias.Hidden);
                Line.Write(Alias.ObsoleteInternal);
                Line.Write($"{@protected}{@new}void {methodName}()");
            }
            else
            {
                bool register = methodName is "OnEnableINTERNAL";
                methodName = register
                    ? $"RegisterInstance"
                    : $"UnregisterInstance";

                if (input.IsUnityEditorCompile)
                {
                    Line.Write($"/// <summary>");
                    Line.Write($"/// Manually {(register ? "registers" : "unregisters")} this instance {(register ? "in" : "from")} the <c>{input.Attribute}</c> storage.");
                    Line.Write($"/// </summary>");
                    Line.Write($"/// <remarks>");
                    Line.Write($"/// You <b>must ensure</b> that the instance always registers and unregisters itself symmetrically.");
                    Line.Write($"/// This is usually achieved by hooking into reliable object lifecycle methods, such as <c>OnEnable</c>+<c>OnDisable</c>,");
                    Line.Write($"/// or <c>Awake</c>+<c>OnDestroy</c>. Make sure the registration methods are never stopped by an earlier exception.");
                    Line.Write($"/// </remarks>");
                }

                Line.Write($"{@protected}{@new}void {methodName}()");
            }

            using (Braces)
            {
                if (input.HasBaseDeclarationsWithAttribute)
                    Line.Write($"base.{methodName}();");

                foreach (var methodCall in methodCalls)
                    if (methodCall is not null)
                        Line.Write(methodCall).Write(';');
            }

            Linebreak();
        }

        void EmitGeneratedPropertiesListComment()
        {
            Line.Write($"/// <item> The <see cref=\"{input.TypeDisplayName}.Instances\"/> tracked instance list </item>");
            if (input.AttributeArguments.TrackInstanceIDs)
                Line.Write($"/// <item> The <see cref=\"{input.TypeDisplayName}.InstanceIDs\"/> instance ID array </item>");

            if (input.AttributeArguments.TrackTransforms)
                Line.Write($"/// <item> The <see cref=\"{input.TypeDisplayName}.TransformAccessArray\"/> transform array </item>");

            foreach (var (dataType, dataTypeShort) in unmanagedDataInfo)
                Line.Write($"/// <item> The <see cref=\"{input.TypeDisplayName}.Unmanaged.{dataTypeShort}Array\"/> data array </item>");
        }

        if (input.HasIInstanceIndex)
        {
            if (input.Attribute is SingletonAttributeMetadataName)
            {
                Line.Write($"#error The IInstanceIndex interface is invalid for singleton type {input.TypeDisplayName}.");
            }
            else
            {
                if (input.IsUnityEditorCompile)
                {
                    Line.Write($"/// <summary>");
                    Line.Write($"/// Represents the index of this instance in the following static storage arrays:");
                    Line.Write($"/// <list type=\"bullet\">");
                    EmitGeneratedPropertiesListComment();
                    Line.Write($"/// </list>");
                    Line.Write($"/// </summary>");
                    Line.Write($"/// <remarks>");
                    Line.Write($"/// This property is automatically updated.");
                    Line.Write($"/// Note that the instance index will change during the lifetime of the instance - never store it.");
                    Line.Write($"/// <br/><br/>");
                    Line.Write($"/// A value of -1 indicates that the instance is not currently active/registered.");
                    Line.Write($"/// </remarks>");
                }
            }

            Line.Write($"public int InstanceIndex => {m}MedicineInternalInstanceIndex;");
            Linebreak();

            Line.Write($"int {IInstanceIndexInterfaceFQN}.InstanceIndex");
            using (Braces)
            {
                Line.Write($"{Alias.Inline} get => {m}MedicineInternalInstanceIndex;");
                Line.Write($"{Alias.Inline} set => {m}MedicineInternalInstanceIndex = value;");
            }

            Linebreak();

            Line.Write(Alias.Hidden);
            Line.Write($"int {m}MedicineInternalInstanceIndex = -1;");
            Linebreak();
        }

        if (input.AttributeArguments.TrackTransforms)
        {
            if (input.IsUnityEditorCompile)
            {
                Line.Write($"/// <summary>");
                Line.Write($"/// Allows job access to the transforms of the tracked {input.TypeDisplayName} instances.");
                Line.Write($"/// </summary>");
            }

            Line.Write($"{@new}public static global::UnityEngine.Jobs.TransformAccessArray TransformAccessArray");
            using (Braces)
            {
                Line.Append(Alias.Inline).Append(" get");
                using (Braces)
                {
                    Line.Append($"return {m}Storage.TransformAccess<{input.TypeFQN}>.Transforms;");
                    Linebreak();
                    if (input.IsUnityEditorCompile)
                        Line.Append("[global::UnityEditor.InitializeOnLoadMethod]");

                    Line.Append("[global::UnityEngine.RuntimeInitializeOnLoadMethod(global::UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]");
                    Line.Append($"static void {m}Init()");

                    using (Indent)
                        Line.Append($"=> {m}Storage.TransformAccess<{input.TypeFQN}>.Initialize")
                            .Append($"({input.AttributeArguments.InitialCapacity}, {input.AttributeArguments.DesiredJobCount});");
                }
            }

            Linebreak();
        }

        if (input.AttributeArguments.TrackInstanceIDs)
        {
            if (input.IsUnityEditorCompile)
            {
                Line.Write($"/// <summary>");
                Line.Write($"/// Gets an array of instance IDs for the tracked type's enabled instances.");
                Line.Write($"/// </summary>");
                Line.Write($"/// <seealso href=\"https://docs.unity3d.com/ScriptReference/Resources.InstanceIDToObjectList.html\">Resources.InstanceIDToObjectList</seealso>");
                Line.Write($"/// <remarks>");
                Line.Write($"/// The instance IDs can be used to refer to tracked objects in unmanaged contexts, e.g.,");
                Line.Write($"/// jobs and Burst-compiled functions.");
                Line.Write($"/// <list type=\"bullet\">");
                Line.Write($"/// <item>Requires the <c>[Track(instanceIDs: true)]</c> attribute parameter to be set.</item>");
                Line.Write($"/// <item>The order of instance IDs will correspond to the order of tracked transforms.</item>");
                Line.Write($"/// </list>");
                Line.Write($"/// </remarks>");
            }

            Line.Write($"{@new}public static global::Unity.Collections.NativeArray<int> InstanceIDs");
            using (Braces)
                Line.Write(Alias.Inline).Write($" get => {m}Storage.InstanceIDs<{input.TypeFQN}>.List.AsArray();");

            Linebreak();
        }

        if (input.UnmanagedDataFQNs.Length > 0)
        {
            Line.Write($"public static class Unmanaged");
            using (Braces)
            {
                foreach (var (dataType, dataTypeShort) in unmanagedDataInfo)
                {
                    Linebreak();
                    Line.Write($"public static ref global::Unity.Collections.NativeArray<{dataType}> {dataTypeShort}Array");

                    using (Braces)
                        Line.Write($"{Alias.Inline} get => ref {m}Storage.UnmanagedData<{input.TypeFQN}, {dataType}>.Array;");
                }
            }

            if (input.HasIInstanceIndex)
            {
                Linebreak();
                foreach (var (dataType, dataTypeShort) in unmanagedDataInfo)
                {
                    Linebreak();
                    Line.Write($"public ref {dataType} {dataTypeShort}");

                    using (Indent)
                        Line.Write($"=> ref {m}Storage.UnmanagedData<{input.TypeFQN}, {dataType}>.ElementAtRefRW({m}MedicineInternalInstanceIndex);");
                }
            }

            Linebreak();
        }

        if (input.Attribute is SingletonAttributeMetadataName)
        {
            if (input.IsUnityEditorCompile)
            {
                Line.Write($"/// <summary>");
                Line.Write($"/// Retrieves the active <see cref=\"{input.TypeDisplayName}\"/> singleton instance.");
                Line.Write($"/// </summary>");
                Line.Write($"/// <remarks>");
                Line.Write($"/// <list type=\"bullet\">");
                Line.Write($"/// <item> This property <b>might return null</b> if the singleton instance has not been registered yet");
                Line.Write($"/// (or has been disabled/destroyed). </item>");
                Line.Write($"/// <item> MonoBehaviours and ScriptableObjects marked with the <see cref=\"SingletonAttribute\"/> will");
                Line.Write($"/// automatically register/unregister themselves as the active singleton instance in OnEnable/OnDisable. </item>");
                Line.Write($"/// <item> In edit mode, to provide better compatibility with editor tooling, <c>FindObjectsOfType</c>");
                Line.Write($"/// is used internally to attempt to locate the object (cached for one editor update). </item>");
                Line.Write($"/// </list>");
                Line.Write($"/// </remarks>");
            }

            Line.Write($"public static {input.TypeFQN}? Instance");

            using (Braces)
            {
                Line.Write(Alias.Inline);
                Line.Write($"get => {m}Storage.Singleton<{input.TypeFQN}>.Instance;");
            }

            Linebreak();
            EmitRegistrationMethod("OnEnableINTERNAL", $"{m}Storage.Singleton<{input.TypeFQN}>.Register(this)");
            EmitRegistrationMethod("OnDisableINTERNAL", $"{m}Storage.Singleton<{input.TypeFQN}>.Unregister(this)");

            if (input.IsUnityEditorCompile)
            {
                Linebreak();
                Line.Write(Alias.Hidden);
                Line.Write(Alias.ObsoleteInternal);
                Line.Write($"int {m}MedicineInvalidateInstanceToken = {m}Storage.Singleton<{input.TypeFQN}>.EditMode.Invalidate();");
            }
        }
        else if (input.Attribute is TrackAttributeMetadataName)
        {
            if (input.IsUnityEditorCompile)
            {
                Line.Write($"/// <summary>");
                Line.Write($"/// Allows enumeration of all enabled instances of <see cref=\"{input.TypeDisplayName?.HtmlEncode()}\"/>.");
                Line.Write($"/// </summary>");
                Line.Write($"/// <remarks>");
                Line.Write($"/// <list type=\"bullet\">");
                Line.Write($"/// <item> MonoBehaviours and ScriptableObjects marked with the <see cref=\"TrackAttribute\"/> will automatically register/unregister themselves");
                Line.Write($"/// in the active instance list in OnEnable/OnDisable. </item>");
                Line.Write($"/// <item> This property can return null if the singleton instance doesn't exist or hasn't executed OnEnabled yet. </item>");
                Line.Write($"/// <item> In edit mode, to provide better compatibility with editor tooling, <see cref=\"Object.FindObjectsByType(System.Type,UnityEngine.FindObjectsSortMode)\"/>");
                Line.Write($"/// is used internally to find object instances (cached for one editor update). </item>");
                Line.Write($"/// <item> You can use <c>foreach</c> to iterate over the instances. </item>");
                Line.Write($"/// <item> If you’re enabling/disabling instances while enumerating, you need to use <c>{input.TypeDisplayName}.Instances.Copied()</c>. </item>");
                Line.Write($"/// <item> The returned struct is compatible with <a href=\"https://github.com/Cysharp/ZLinq\">ZLINQ</a>. </item>");
                Line.Write($"/// </list>");
                Line.Write($"/// </remarks>");
            }

            Line.Write($"public static global::Medicine.Internal.TrackedInstances<{input.TypeFQN}> Instances");
            using (Braces)
            {
                Line.Write($"{Alias.Inline} get => default;");
            }

            Linebreak();
            string withId = input.AttributeArguments.TrackInstanceIDs ? "WithInstanceID" : "";

            EmitRegistrationMethod(
                methodName: "OnEnableINTERNAL",
                methodCalls:
                [
                    $"{m}Storage.Instances<{input.TypeFQN}>.Register{withId}(this)",
                    input.AttributeArguments.TrackTransforms ? $"{m}Storage.TransformAccess<{input.TypeFQN}>.Register(transform)" : null,
                    ..input.InterfacesWithAttribute.AsArray().Select(x => $"{m}Storage.Instances<{x}>.Register(this)"),
                    ..input.UnmanagedDataFQNs.AsArray().Select(x => $"{m}Storage.UnmanagedData<{input.TypeFQN}, {x}>.Register(this)"),
                ]
            );

            EmitRegistrationMethod(
                methodName: "OnDisableINTERNAL",
                methodCalls:
                [
                    $"int index = {m}Storage.Instances<{input.TypeFQN}>.Unregister{withId}(this)",
                    ..input.UnmanagedDataFQNs.AsArray().Select(x => $"{m}Storage.UnmanagedData<{input.TypeFQN}, {x}>.Unregister(this, index)").Reverse(),
                    ..input.InterfacesWithAttribute.AsArray().Select(x => $"{m}Storage.Instances<{x}>.Unregister(this)").Reverse(),
                    input.AttributeArguments.TrackTransforms ? $"{m}Storage.TransformAccess<{input.TypeFQN}>.Unregister(index)" : null,
                ]
            );

            if (input.IsUnityEditorCompile)
            {
                Linebreak();
                Line.Write(Alias.Hidden);
                Line.Write(Alias.ObsoleteInternal);
                Line.Write($"int {m}MedicineInvalidateInstanceToken = {m}Storage.Instances<{input.TypeFQN}>.EditMode.Invalidate();");
            }
        }

        TrimEndWhitespace();
        foreach (var _ in declarations)
        {
            DecreaseIndent();
            Line.Write('}');
        }
    }
}