using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Constants;

[Generator]
public sealed class TrackingSourceGenerator : BaseSourceGenerator, IIncrementalGenerator
{
    void IIncrementalGenerator.Initialize(IncrementalGeneratorInitializationContext context)
    {
        foreach (var attributeName in new[] { SingletonAttributeName, TrackAttributeName })
        {
            context.RegisterImplementationSourceOutput(
                context.SyntaxProvider
                    .ForAttributeWithMetadataName(
                        fullyQualifiedMetadataName: $"Medicine.{attributeName}",
                        predicate: static (node, _) => node is ClassDeclarationSyntax,
                        transform: WrapTransform((attributeSyntaxContext, ct)
                            => TransformSyntaxContext(attributeSyntaxContext, ct, attributeName, $"global::Medicine.{attributeName}")
                        )
                    )
                    .Where(x => x.Attribute is { Length: > 0 }),
                WrapGenerateSource<GeneratorInput>(GenerateSource)
            );
        }
    }

    record struct GeneratorInput() : IGeneratorTransformOutput
    {
        public string? SourceGeneratorOutputFilename { get; init; }
        public string? SourceGeneratorError { get; set; }
        public EquatableIgnoreList<string>? SourceGeneratorDiagnostics { get; set; } = [];
        public string? Attribute;
        public (bool TrackInstanceIDs, bool TrackTransforms, int InitialCapacity, int DesiredJobCount, bool Manual) AttributeArguments;
        public EquatableArray<string> ContainingTypeDeclaration;
        public EquatableArray<string> InterfacesWithAttribute;
        public EquatableArray<string> InstanceDataFQNs;
        public string? TypeFQN;
        public string? TypeDisplayName;
        public bool HasIInstanceIndex;
        public bool IsSealed;
        public bool IsUnityEditorCompile;
        public bool IsDebugCompile;
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

        (bool TrackInstanceIDs, bool TrackTransforms, int InitialCapacity, int DesiredJobCount, bool Manual) GetAttributeArguments()
        {
            var constructorArguments = attributeData.ConstructorArguments;
            var arguments = new Dictionary<string, object>(StringComparer.Ordinal);

            for (int i = 0; i < constructorArguments.Length; i++)
                if (constructorArguments[i].Value is { } argument)
                    arguments[attributeCtorParameters[i].Name] = argument;

            foreach (var namedArg in attributeData.NamedArguments)
                if (namedArg.Value.Value is { } value)
                    arguments[namedArg.Key] = value;

            T? GetValue<T>(string name, T? defaultValue)
                => arguments.TryGetValue(name, out var value) && value is T typedValue
                    ? typedValue
                    : defaultValue;

            return (
                GetValue("instanceIdArray", false),
                GetValue("transformAccessArray", false),
                GetValue("transformInitialCapacity", 64),
                GetValue("transformDesiredJobCount", -1),
                GetValue("manual", false)
            );
        }

        var attributeArguments = GetAttributeArguments();

        if (!classSymbol.InheritsFrom("global::UnityEngine.MonoBehaviour"))
            attributeArguments.TrackTransforms = false;

        var preprocessorSymbolNames = context.SemanticModel
            .SyntaxTree.Options.PreprocessorSymbolNames
            .ToArray();

        return new()
        {
            SourceGeneratorOutputFilename = GetOutputFilename(typeDeclaration.SyntaxTree.FilePath, typeDeclaration.Identifier.ValueText, attributeName),
            ContainingTypeDeclaration = Utility.DeconstructTypeDeclaration(typeDeclaration),
            Attribute = attributeName,
            AttributeArguments = attributeArguments,
            InstanceDataFQNs = classSymbol.Interfaces
                .Where(x => x.GetFQN()?.StartsWith(UnmanagedDataInterfaceFQN) is true)
                .Select(x => x.TypeArguments.FirstOrDefault().GetFQN()!)
                .ToArray(),
            HasIInstanceIndex = classSymbol.HasInterface(IInstanceIndexInterfaceFQN),
            TypeFQN = classSymbol.GetFQN()!,
            TypeDisplayName = classSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            IsSealed = classSymbol.IsSealed,
            IsUnityEditorCompile = preprocessorSymbolNames.Contains("UNITY_EDITOR"),
            IsDebugCompile = preprocessorSymbolNames.Contains("DEBUG"),
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
        Line.Append("#pragma warning disable CS8321 // Local function is declared but never used");
        Line.Append("#pragma warning disable CS0618 // Type or member is obsolete");
        Line.Append(Alias.UsingStorage);
        Line.Append(Alias.UsingInline);
        Linebreak();

        string @protected = input.IsSealed ? "" : "protected ";
        string @new = input.HasBaseDeclarationsWithAttribute ? "new " : "";

        var declarations = input.ContainingTypeDeclaration.AsSpan();
        int lastDeclaration = declarations.Length - 1;
        for (int i = 0; i < declarations.Length; i++)
        {
            if (i == lastDeclaration && input.IsUnityEditorCompile)
            {
                if (input.Attribute is TrackAttributeName)
                {
                    Line.Append("/// <remarks>");
                    Line.Append($"/// The instances of this class are tracked, and contains additional generated properties:");
                    Line.Append($"/// <list type=\"bullet\">");
                    EmitGeneratedPropertiesListComment();
                    if (input.HasIInstanceIndex)
                        Line.Append($"/// <item> The <see cref=\"{input.TypeDisplayName}.InstanceIndex\"/> property, which is the instance's index into the above </item>");

                    Line.Append($"/// </list>");
                    Line.Append("/// </remarks>");
                }
                else if (input.Attribute is SingletonAttributeName)
                {
                    Line.Append("/// <remarks>");
                    Line.Append($"/// This is a singleton class. The current instance of the singleton can be accessed via the");
                    Line.Append($"/// generated <see cref=\"{input.TypeDisplayName}.Instance\"/> static property.");
                    Line.Append("/// </remarks>");
                }
            }

            Line.Append(declarations[i]);
            Line.Append('{');
            IncreaseIndent();
        }

        void EmitRegistrationMethod(string methodName, string trackingMethod, bool storeIndex = false, params string?[] extraTrackingMethods)
        {
            if (!input.AttributeArguments.Manual)
            {
                Line.Append(Alias.Hidden);
                Line.Append(Alias.ObsoleteInternal);
                Line.Append($"{@protected}{@new}void {methodName}()");
            }
            else
            {
                bool register = methodName is "OnEnableINTERNAL";
                methodName = register
                    ? $"RegisterInstance"
                    : $"UnregisterInstance";

                if (input.IsUnityEditorCompile)
                {
                    Line.Append($"/// <summary>");
                    Line.Append($"/// Manually {(register ? "registers" : "unregisters")} this instance {(register ? "in" : "from")} the <c>{input.Attribute}</c> storage.");
                    Line.Append($"/// </summary>");
                    Line.Append($"/// <remarks>");
                    Line.Append($"/// You <b>must ensure</b> that the instance always registers and unregisters itself symmetrically.");
                    Line.Append($"/// This is usually achieved by hooking into reliable object lifecycle methods, such as <c>OnEnable</c>+<c>OnDisable</c>,");
                    Line.Append($"/// or <c>Awake</c>+<c>OnDestroy</c>. Make sure the registration methods are never stopped by an earlier exception.");
                    Line.Append($"/// </remarks>");
                }

                Line.Append($"void {methodName}()");
            }

            using (Braces)
            {
                if (input.HasBaseDeclarationsWithAttribute)
                    Line.Append($"base.{methodName}();");

                Line.Append($"{(storeIndex ? "int index = " : "")}{trackingMethod}(this);");

                foreach (var extraTrackingMethod in extraTrackingMethods)
                    if (extraTrackingMethod is not null)
                        Line.Append(extraTrackingMethod).Append(';');
            }

            Linebreak();
        }

        void EmitGeneratedPropertiesListComment()
        {
            Line.Append($"/// <item> The <see cref=\"{input.TypeDisplayName}.Instances\"/> tracked instance list </item>");
            if (input.AttributeArguments.TrackInstanceIDs)
                Line.Append($"/// <item> The <see cref=\"{input.TypeDisplayName}.InstanceIDs\"/> instance ID array </item>");

            if (input.AttributeArguments.TrackTransforms)
                Line.Append($"/// <item> The <see cref=\"{input.TypeDisplayName}.TransformAccessArray\"/> transform array </item>");

            foreach (var dataType in input.InstanceDataFQNs)
                Line.Append($"/// <item> The <see cref=\"{input.TypeDisplayName}.Unmanaged.{dataType.Split('.', ':').Last()}Array\"/> data array </item>");
        }

        if (input.HasIInstanceIndex)
        {
            if (input.Attribute is SingletonAttributeName)
            {
                Line.Append($"#error The IInstanceIndex interface is invalid for singleton type {input.TypeDisplayName}.");
            }
            else
            {
                if (input.IsUnityEditorCompile)
                {
                    Line.Append($"/// <summary>");
                    Line.Append($"/// Represents the index of this instance in the following static storage arrays:");
                    Line.Append($"/// <list type=\"bullet\">");
                    EmitGeneratedPropertiesListComment();
                    Line.Append($"/// </list>");
                    Line.Append($"/// </summary>");
                    Line.Append($"/// <remarks>");
                    Line.Append($"/// This property is automatically updated.");
                    Line.Append($"/// Note that the instance index will change during the lifetime of the instance - never store it.");
                    Line.Append($"/// <br/><br/>");
                    Line.Append($"/// A value of -1 indicates that the instance is not currently active/registered.");
                    Line.Append($"/// </remarks>");
                }
            }

            Line.Append($"public int InstanceIndex => {m}MedicineInternalInstanceIndex;");
            Linebreak();

            Line.Append($"int {IInstanceIndexInterfaceFQN}.InstanceIndex");
            using (Braces)
            {
                Line.Append(Alias.Inline).Append($" get => {m}MedicineInternalInstanceIndex;");
                Line.Append(Alias.Inline).Append($" set => {m}MedicineInternalInstanceIndex = value;");
            }
            Linebreak();

            Line.Append(Alias.Hidden);
            Line.Append($"int {m}MedicineInternalInstanceIndex = -1;");
            Linebreak();
        }

        if (input.AttributeArguments.TrackTransforms)
        {
            if (input.IsUnityEditorCompile)
            {
                Line.Append($"/// <summary>");
                Line.Append($"/// Allows job access to the transforms of the tracked {input.TypeDisplayName} instances.");
                Line.Append($"/// </summary>");
            }

            Line.Append($"{@new}public static global::UnityEngine.Jobs.TransformAccessArray TransformAccessArray");
            using (Braces)
            {
                Line.Append(Alias.Inline).Append(" get");
                using (Braces)
                {
                    Line.Append($"return {m}Storage.TransformAccess<{input.TypeFQN}>.Transforms;");
                    Linebreak();
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
                Line.Append($"/// <summary>");
                Line.Append($"/// Gets an array of instance IDs for the tracked type's enabled instances.");
                Line.Append($"/// </summary>");
                Line.Append($"/// <seealso href=\"https://docs.unity3d.com/ScriptReference/Resources.InstanceIDToObjectList.html\">Resources.InstanceIDToObjectList</seealso>");
                Line.Append($"/// <remarks>");
                Line.Append($"/// The instance IDs can be used to refer to tracked objects in unmanaged contexts, e.g.,");
                Line.Append($"/// jobs and Burst-compiled functions.");
                Line.Append($"/// <list type=\"bullet\">");
                Line.Append($"/// <item>Requires the <c>[Track(instanceIDs: true)]</c> attribute parameter to be set.</item>");
                Line.Append($"/// <item>The order of instance IDs will correspond to the order of tracked transforms.</item>");
                Line.Append($"/// </list>");
                Line.Append($"/// </remarks>");
            }

            Line.Append($"{@new}public static global::Unity.Collections.NativeArray<int> InstanceIDs");
            using (Braces)
            {
                Line.Append(Alias.Inline).Append(" get");
                using (Braces)
                {
                    Line.Append($"return {m}Storage.InstanceIDs<{input.TypeFQN}>.List.AsArray();");
                    Linebreak();
                    Line.Append("[global::UnityEngine.RuntimeInitializeOnLoadMethod(global::UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]");
                    Line.Append($"static void {m}Init()");

                    using (Indent)
                        Line.Append($"=> {m}Storage.InstanceIDs<{input.TypeFQN}>.Initialize();");
                }
            }

            Linebreak();
        }

        if (input.InstanceDataFQNs.Length > 0)
        {
            Line.Append($"public static class Unmanaged");
            using (Braces)
            {
                foreach (var dataType in input.InstanceDataFQNs)
                {
                    Linebreak();
                    Line.Append($"public static global::Unity.Collections.NativeArray<{dataType}> {dataType.Split('.', ':').Last()}Array");

                    using (Braces)
                    {
                        Line.Append($"{Alias.Inline} get");
                        using (Braces)
                        {
                            Line.Append($"return {m}Storage.UnmanagedData<{input.TypeFQN}, {dataType}>.List.AsArray();");
                            Linebreak();
                            Line.Append("[global::UnityEngine.RuntimeInitializeOnLoadMethod(global::UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]");
                            Line.Append($"static void {m}Init()");

                            using (Indent)
                                Line.Append($"=> {m}Storage.UnmanagedData<{input.TypeFQN}, {dataType}>.Initialize();");
                        }
                    }
                }
            }

            Linebreak();
        }

        if (input.Attribute is SingletonAttributeName)
        {
            if (input.IsUnityEditorCompile)
            {
                Line.Append($"/// <summary>");
                Line.Append($"/// Retrieves the active <see cref=\"{input.TypeDisplayName}\"/> singleton instance.");
                Line.Append($"/// </summary>");
                Line.Append($"/// <remarks>");
                Line.Append($"/// <list type=\"bullet\">");
                Line.Append($"/// <item> This property <b>might return null</b> if the singleton instance has not been registered yet");
                Line.Append($"/// (or has been disabled/destroyed). </item>");
                Line.Append($"/// <item> MonoBehaviours and ScriptableObjects marked with the <see cref=\"SingletonAttribute\"/> will");
                Line.Append($"/// automatically register/unregister themselves as the active singleton instance in OnEnable/OnDisable. </item>");
                Line.Append($"/// <item> In edit mode, to provide better compatibility with editor tooling, <c>FindObjectsOfType</c>");
                Line.Append($"/// is used internally to attempt to locate the object (cached for one editor update). </item>");
                Line.Append($"/// </list>");
                Line.Append($"/// </remarks>");
            }

            Line.Append($"public static {input.TypeFQN}? Instance");

            using (Braces)
            {
                Line.Append(Alias.Inline);
                Line.Append($"get => {m}Storage.Singleton<{input.TypeFQN}>.Instance;");
            }

            Linebreak();
            EmitRegistrationMethod("OnEnableINTERNAL", $"{m}Storage.Singleton<{input.TypeFQN}>.Register");
            EmitRegistrationMethod("OnDisableINTERNAL", $"{m}Storage.Singleton<{input.TypeFQN}>.Unregister");

            if (input.IsUnityEditorCompile)
            {
                Linebreak();
                Line.Append(Alias.Hidden);
                Line.Append(Alias.ObsoleteInternal);
                Line.Append($"global::Medicine.Internal.InvalidateSingletonToken<{input.TypeFQN}> {m}MedicineInternalInstanceToken = new(meaningOfLife: 42);");
            }
        }
        else if (input.Attribute is TrackAttributeName)
        {
            if (input.IsUnityEditorCompile)
            {
                Line.Append($"/// <summary>");
                Line.Append($"/// Allows enumeration of all enabled instances of <see cref=\"{input.TypeDisplayName?.HtmlEncode()}\"/>.");
                Line.Append($"/// </summary>");
                Line.Append($"/// <remarks>");
                Line.Append($"/// <list type=\"bullet\">");
                Line.Append($"/// <item> MonoBehaviours and ScriptableObjects marked with the <see cref=\"TrackAttribute\"/> will automatically register/unregister themselves");
                Line.Append($"/// in the active instance list in OnEnable/OnDisable. </item>");
                Line.Append($"/// <item> This property can return null if the singleton instance doesn't exist or hasn't executed OnEnabled yet. </item>");
                Line.Append($"/// <item> In edit mode, to provide better compatibility with editor tooling, <see cref=\"Object.FindObjectsByType(System.Type,UnityEngine.FindObjectsSortMode)\"/>");
                Line.Append($"/// is used internally to find object instances (cached for one editor update). </item>");
                Line.Append($"/// <item> You can use <c>foreach</c> to iterate over the instances. </item>");
                Line.Append($"/// <item> If you’re enabling/disabling instances while enumerating, you need to use <c>{input.TypeDisplayName}.Instances.Copied()</c>. </item>");
                Line.Append($"/// <item> The returned struct is compatible with <a href=\"https://github.com/Cysharp/ZLinq\">ZLINQ</a>. </item>");
                Line.Append($"/// </list>");
                Line.Append($"/// </remarks>");
            }

            Line.Append($"public static global::Medicine.Internal.TrackedInstances<{input.TypeFQN}> Instances");
            using (Braces)
            {
                Line.Append(Alias.Inline);
                Line.Append($"get => default;");
            }

            Linebreak();
            string withId = input.AttributeArguments.TrackInstanceIDs ? "WithInstanceID" : "";

            EmitRegistrationMethod(
                methodName: "OnEnableINTERNAL",
                trackingMethod: $"{m}Storage.Instances<{input.TypeFQN}>.Register{withId}",
                storeIndex: false,
                extraTrackingMethods:
                [
                    input.AttributeArguments.TrackTransforms ? $"{m}Storage.TransformAccess<{input.TypeFQN}>.Register(transform)" : null,
                    ..input.InterfacesWithAttribute.AsArray().Select(x => $"{m}Storage.Instances<{input.TypeFQN}, {x}>.Register(this)"),
                    ..input.InstanceDataFQNs.AsArray().Select(x => $"{m}Storage.UnmanagedData<{input.TypeFQN}, {x}>.Register(this)"),
                ]
            );

            EmitRegistrationMethod(
                methodName: "OnDisableINTERNAL",
                trackingMethod: $"{m}Storage.Instances<{input.TypeFQN}>.Unregister{withId}",
                storeIndex: true,
                extraTrackingMethods:
                [
                    input.AttributeArguments.TrackTransforms ? $"{m}Storage.TransformAccess<{input.TypeFQN}>.Unregister(index)" : null,
                    ..input.InterfacesWithAttribute.AsArray().Select(x => $"{m}Storage.Instances<{input.TypeFQN}, {x}>.Unregister(this)"),
                    ..input.InstanceDataFQNs.AsArray().Select(x => $"{m}Storage.UnmanagedData<{input.TypeFQN}, {x}>.Unregister(index)"),
                ]
            );

            if (input.IsUnityEditorCompile)
            {
                Linebreak();
                Line.Append(Alias.Hidden);
                Line.Append(Alias.ObsoleteInternal);
                Line.Append($"global::Medicine.Internal.InvalidateInstanceToken<{input.TypeFQN}> {m}MedicineInternalInstanceToken = new(meaningOfLife: 42);");
            }
        }

        TrimEndWhitespace();
        foreach (var _ in declarations)
        {
            DecreaseIndent();
            Line.Append('}');
        }
    }
}