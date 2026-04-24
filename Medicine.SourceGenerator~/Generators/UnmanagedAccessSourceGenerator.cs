using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using static ActivePreprocessorSymbolNames;
using static Constants;

[Generator]
public sealed class UnmanagedAccessSourceGenerator : IIncrementalGenerator
{
    record struct GeneratorInput : ISourceGeneratorPassData
    {
        public string? SourceGeneratorOutputFilename { get; init; }
        public string? SourceGeneratorError { get; init; }
        public LocationInfo? SourceGeneratorLocation { get; set; }
        public EquatableArray<string> ContainingTypeDeclaration;
        public string ClassName;
        public string ClassFQN;
        public bool IsUnityObject;
        public bool UsesEntityId;
        public bool IsTracked;
        public GeneratorEnvironment GeneratorEnvironment;
        public AttributeSettings AttributeSettings;
        public Defer<bool>? HasCachedEnableBuilderDeferred;
        public Defer<bool>? HasIInstanceIndexBuilderDeferred;
        public EquatableArray<FieldInfo> Fields;
    }

    record struct FieldInfo
    {
        public string Name;
        public string MetadataName;
        public string TypeFQN;
        public string ArrayElementTypeFQN;
        public FieldFlags Flags;

        public readonly bool EmitsDirectAccess
            => Flags.Has(FieldFlags.TypeHasUnmanagedAccess) &&
               !Flags.Has(FieldFlags.IsManagedArrayType);

        public readonly bool EmitsUnmanagedArray
            => Flags.Has(FieldFlags.IsManagedArrayType) &&
               Flags.Has(FieldFlags.ArrayElementIsUnmanagedType);

        public readonly bool EmitsAccessArray
            => Flags.Has(FieldFlags.IsManagedArrayType) &&
               Flags.Has(FieldFlags.ArrayElementHasUnmanagedAccess);
    }

    [Flags]
    enum FieldFlags : uint
    {
        IsPublic = 1 << 00,
        IsReadOnly = 1 << 01,
        IsUnmanagedType = 1 << 02,
        IsReferenceType = 1 << 03,
        IsManagedArrayType = 1 << 04,
        TypeHasUnmanagedAccess = 1 << 05,
        ArrayElementIsUnmanagedType = 1 << 06,
        ArrayElementHasUnmanagedAccess = 1 << 07,
        IsPrivateInBaseType = 1 << 08,
        IsManagedValueType = 1 << 09,
    }

    record struct AttributeSettings(
        bool SafetyChecks,
        bool IncludePublic,
        bool IncludePrivate,
        EquatableArray<string> MemberNames
    );

    static readonly SourceText extensionsSrc
        = SourceText.From(
            $$"""
              namespace Medicine
              {
              #if UNITY_EDITOR
                  /// <summary>
                  /// Contains generated unmanaged access extension methods.
                  /// </summary>
              #endif
                  {{Alias.Hidden}}
                  public static partial class UnmanagedAccessExtensions { }
              }
              """,
            Encoding.UTF8
        );

    void IIncrementalGenerator.Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(x => x.AddSource("UnmanagedAccessExtensions.g.cs", extensionsSrc));

        var generatorEnvironment = context.GetGeneratorEnvironment();

        var inputProvider = context
            .SyntaxProvider
            .ForAttributeWithMetadataNameEx(
                fullyQualifiedMetadataName: UnmanagedAccessAttributeMetadataName,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: TransformForCache
            )
            .Combine(generatorEnvironment)
            .SelectEx((x, ct) => TransformSyntaxContext(x.Left.Context.Value, x.Right, ct)
        );

        context.RegisterSourceOutputEx(
            source: inputProvider,
            action: GenerateSource
        );
    }

    static ContextWithCacheGeneratorInput TransformForCache(GeneratorAttributeSyntaxContext context, CancellationToken ct)
        => new()
        {
            Context = context,
            SourceGeneratorOutputFilename = GetOutputFilename(context),
            SourceGeneratorLocation = context.TargetNode.GetLocation(),
            Checksum64ForCache = (context.TargetNode.Parent ?? context.TargetNode).GetNodeChecksum(ct),
        };

    static GeneratorInput TransformSyntaxContext(GeneratorAttributeSyntaxContext context, GeneratorEnvironment generatorEnvironment, CancellationToken ct)
    {
        if (context.TargetNode is not TypeDeclarationSyntax typeDecl)
            return default;

        if (context.TargetSymbol is not ITypeSymbol typeSymbol)
            return default;

        var knownSymbols = context.SemanticModel.Compilation.GetKnownSymbols();

        var settings = context.Attributes
            .First(x => x.AttributeClass.Is(knownSymbols.UnmanagedAccessAttribute))
            .GetAttributeConstructorArguments(ct)
            .Select(x => new AttributeSettings(
                    SafetyChecks: x.Get("safetyChecks", true),
                    IncludePublic: x.Get("includePublic", true),
                    IncludePrivate: x.Get("includePrivate", true),
                    MemberNames: x.Get<string[]>("memberNames", []) ?? []
                )
            );

        var trackAttribute = context.TargetSymbol.GetAttribute(knownSymbols.TrackAttribute);

        var output = new GeneratorInput
        {
            SourceGeneratorOutputFilename = GetOutputFilename(context),
            AttributeSettings = settings,
            GeneratorEnvironment = generatorEnvironment,
            ContainingTypeDeclaration = Utility.DeconstructTypeDeclaration(typeDecl),
            ClassName = typeSymbol.Name,
            ClassFQN = typeSymbol.FQN,
            IsUnityObject = typeSymbol.InheritsFrom(knownSymbols.UnityObject),
            UsesEntityId = generatorEnvironment.IsUnity64OrNewer,
            IsTracked = trackAttribute is not null,
            SourceGeneratorLocation = new(typeDecl.Identifier.GetLocation()),
            HasCachedEnableBuilderDeferred = new(() =>
                {
                    if (trackAttribute is null)
                        return false;

                    bool hasBaseCachedEnable = typeSymbol
                        .GetBaseTypes()
                        .Select(x => x.GetAttribute(knownSymbols.TrackAttribute))
                        .OfType<AttributeData>()
                        .Any(x => x.GetAttributeConstructorArguments().Get("cacheEnabledState", false));

                    return trackAttribute
                               .GetAttributeConstructorArguments()
                               .Get("cacheEnabledState", false)
                           && !hasBaseCachedEnable;
                }
            ),
            HasIInstanceIndexBuilderDeferred = new(() =>
                trackAttribute is not null
            ),
        };

        using (Scratch.RentA<List<FieldInfo>>(out var fields))
        {
            foreach (var member in typeSymbol.GetMembers().AsArray())
                CollectField(member, isFromBaseType: false);

            foreach (var member in typeSymbol.GetBaseTypes().SelectMany(x => x.GetMembers().AsArray()))
                CollectField(member, isFromBaseType: true);

            void CollectField(ISymbol member, bool isFromBaseType)
            {
                if (member is not IFieldSymbol { IsStatic: false } field)
                    return;

                // this is special-case handling for source-generated wrapper structs generated for [Union] structs
                // a bit of a hack, but seems harmless enough? usually shouldn't match any real type
                static bool IsWrapperErrorType(ITypeSymbol type)
                    => type is IErrorTypeSymbol
                    {
                        Name: "Wrapper",
                        Arity: 0,
                        ContainingType.TypeKind: TypeKind.Struct,
                    };

                bool treatAsUnmanagedWrapper = IsWrapperErrorType(field.Type);
                var arrayType = field.Type as IArrayTypeSymbol;
                bool isManagedArrayType = arrayType is { Rank: 1, IsSZArray: true };
                var arrayElementType = isManagedArrayType ? arrayType!.ElementType : null;
                bool arrayElementTreatAsUnmanagedWrapper = arrayElementType is not null && IsWrapperErrorType(arrayElementType);
                bool isManagedValueType = field.Type is { IsValueType: true, IsUnmanagedType: false } && !treatAsUnmanagedWrapper;

                var fieldFlags
                    = 0
                      | (field.DeclaredAccessibility is Accessibility.Public ? FieldFlags.IsPublic : 0)
                      | (field.IsReadOnly ? FieldFlags.IsReadOnly : 0)
                      | (field.Type.IsUnmanagedType || treatAsUnmanagedWrapper ? FieldFlags.IsUnmanagedType : 0)
                      | (field.Type.IsReferenceType && !treatAsUnmanagedWrapper ? FieldFlags.IsReferenceType : 0)
                      | (isManagedValueType ? FieldFlags.IsManagedValueType : 0)
                      | (isManagedArrayType ? FieldFlags.IsManagedArrayType : 0)
                      | (field.Type.GetAttribute(knownSymbols.UnmanagedAccessAttribute) is not null ? FieldFlags.TypeHasUnmanagedAccess : 0)
                      | (arrayElementType?.IsUnmanagedType is true || arrayElementTreatAsUnmanagedWrapper ? FieldFlags.ArrayElementIsUnmanagedType : 0)
                      | (arrayElementType?.GetAttribute(knownSymbols.UnmanagedAccessAttribute) is not null ? FieldFlags.ArrayElementHasUnmanagedAccess : 0)
                      | (isFromBaseType && field.DeclaredAccessibility is Accessibility.Private ? FieldFlags.IsPrivateInBaseType : 0);

                fields.Add(
                    new()
                    {
                        MetadataName = field.Name,
                        Name = field.IsImplicitlyDeclared
                            ? field.Name[1..^16] // trim generated backing field name
                            : field.Name,
                        TypeFQN = field.Type.FQN,
                        ArrayElementTypeFQN = arrayElementType?.FQN ?? string.Empty,
                        Flags = fieldFlags,
                    }
                );
            }

            output.Fields = fields.ToArray();
        }

        return output;
    }

    static string? GetOutputFilename(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetNode is not TypeDeclarationSyntax
            {
                ShortName: { Length: > 0 } name,
                FullName: { Length: > 0 } fullName,
                SyntaxTree.FilePath: { Length: > 0 } filePath,
            })
            return null;

        return Utility.GetOutputFilename(
            filePath: filePath,
            targetNodeName: name,
            additionalNameForHash: fullName,
            label: $"[{UnmanagedAccessAttributeNameShort}]",
            includeFilename: false
        );
    }

    static string ToBindingFlagsVisibility(bool isPublic)
        => isPublic ? "Public" : "NonPublic";

    static FieldInfo CachedEnabledField()
        => new()
        {
            Name = "enabled",
            MetadataName = $"{m}MedicineInternalCachedEnabledState",
            TypeFQN = "global::System.Boolean",
            Flags = FieldFlags.IsReadOnly | FieldFlags.IsUnmanagedType,
        };

    static FieldInfo InstanceIndexField()
        => new()
        {
            Name = "InstanceIndex",
            MetadataName = $"{m}MedicineInternalInstanceIndex",
            TypeFQN = "global::System.Int32",
            Flags = FieldFlags.IsReadOnly | FieldFlags.IsUnmanagedType,
        };

    static void GenerateSource(SourceProductionContext context, SourceWriter src, GeneratorInput input)
    {
        src.ShouldEmitDocs = input.GeneratorEnvironment.ShouldEmitDocs;

        var symbols = input.GeneratorEnvironment.PreprocessorSymbols;
        var fields = input.Fields.AsArray();

        void EmitDoc(params string[] lines)
        {
            foreach (var line in lines)
                src.Doc?.Write(line);
        }

        bool hasCachedEnable = input.HasCachedEnableBuilderDeferred?.Value ?? false;
        bool hasIInstanceIndex = input.HasIInstanceIndexBuilderDeferred?.Value ?? false;

        if ((hasCachedEnable ? 1 : 0) + (hasIInstanceIndex ? 1 : 0) is > 0 and var generatedFieldCount)
        {
            var fieldsWithGenerated = new FieldInfo[fields.Length + generatedFieldCount];
            int generatedIndex = 0;

            if (hasCachedEnable)
                fieldsWithGenerated[generatedIndex++] = CachedEnabledField();

            if (hasIInstanceIndex)
                fieldsWithGenerated[generatedIndex++] = InstanceIndexField();

            Array.Copy(fields, sourceIndex: 0, fieldsWithGenerated, destinationIndex: generatedIndex, length: fields.Length);
            fields = fieldsWithGenerated;
        }

        if (!symbols.Has(DEBUG))
            input.AttributeSettings = input.AttributeSettings with { SafetyChecks = false };

        if (input.AttributeSettings.MemberNames is { Length: > 0 })
            fields = fields.Where(x => input.AttributeSettings.MemberNames.AsArray().Contains(x.Name)).ToArray();

        if (input.AttributeSettings is { IncludePublic: false, IncludePrivate: false })
            throw new InvalidOperationException("Must include at least one visibility modifier.");

        if (!input.AttributeSettings.IncludePublic)
            fields = fields.Where(x => !x.Flags.Has(FieldFlags.IsPublic)).ToArray();

        if (!input.AttributeSettings.IncludePrivate)
            fields = fields.Where(x => x.Flags.Has(FieldFlags.IsPublic)).ToArray();

        if (fields.Length is 0)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    descriptor: Utility.ExceptionDiagnosticDescriptor,
                    location: input.SourceGeneratorLocation?.ToLocation() ?? Location.None,
                    messageArgs: "Class marked with [UnmanagedAccess] has no instance fields or properties with backing fields."
                )
            );

            return;
        }

        src.Line.Write($"#pragma warning disable CS0108");
        src.Line.Write(Alias.UsingStorage);
        src.Line.Write(Alias.UsingInline);
        src.Line.Write(Alias.UsingUtility);
        src.Line.Write(Alias.UsingBindingFlags);
        src.Line.Write(Alias.UsingUnsafeUtility);
        src.Line.Write(Alias.UsingDeclaredAt);
        src.Line.Write($"using {m}Self = {input.ClassFQN};");
        src.Linebreak();

        foreach (var x in input.ContainingTypeDeclaration.AsArray())
        {
            src.Line.Write(x);
            src.OpenBrace();
        }

        EmitDoc(
            "/// <summary>",
            $"/// Provides generated unmanaged field access APIs for <see cref=\"{input.ClassFQN}\"/>.",
            "/// </summary>"
        );

        src.Line.Write("public static partial class Unmanaged");
        using (src.Braces)
        {
            src.Line.Write($"static readonly global::Unity.Burst.SharedStatic<Layout> unmanagedLayoutStorage");
            using (src.Indent)
                src.Line.Write($"= global::Unity.Burst.SharedStatic<Layout>.GetOrCreate<Layout>();");

            src.Linebreak();

            EmitDoc(
                "/// <summary>",
                "/// Returns the cached unmanaged layout metadata for the generated access API.",
                "/// </summary>"
            );

            src.Line.Write("public static ref Layout ClassLayout");
            using (src.Braces)
                src.Line.Write($"{Alias.Inline} get => ref unmanagedLayoutStorage.Data;");

            src.Linebreak();

            if (input.IsTracked)
            {
                EmitDoc(
                    "/// <summary>",
                    "/// Returns unmanaged access wrappers aligned with the currently tracked instances.",
                    "/// </summary>"
                );

                src.Line.Write($"public static AccessArray Instances");
                using (src.Braces)
                {
                    src.Line.Write($"{Alias.Inline} get");
                    using (src.Braces)
                    {
                        src.Line.Write($"ref var arr = ref InstanceArrayStorage.InstancesArrayInternalStorage;");
                        src.Line.Write($"arr.UpdateBuffer(ᵐStorage.Instances<{m}Self>.AsUnmanaged());");
                        src.Line.Write($"return arr;");
                    }
                }

                src.Linebreak();

                src.Line.Write($"static class InstanceArrayStorage");
                using (src.Braces)
                {
                    src.Line.Write($"internal static AccessArray InstancesArrayInternalStorage");
                    using (src.Indent)
                        src.Line.Write($"= new(ᵐStorage.Instances<{m}Self>.AsUnmanaged());");

                    src.Linebreak();
                }

                src.Linebreak();
            }

            src.Write("\n#if UNITY_EDITOR");
            src.Line.Write("[global::System.Runtime.InteropServices.StructLayout((short)0, Size = 128)]");
            src.Write("\n#endif");
            EmitDoc(
                "/// <summary>",
                "/// Stores byte offsets for generated field accessors.",
                "/// </summary>"
            );

            src.Line.Write("public readonly struct Layout");
            using (src.Braces)
            {
                foreach (var x in fields)
                {
                    EmitDoc(
                        "/// <summary>",
                        $"/// Byte offset metadata for field <c>{x.Name}</c>.",
                        "/// </summary>"
                    );

                    src.Line.Write($"public ushort {x.Name} {{ get; init; }}");
                }

                src.Linebreak();

                if (symbols.Has(UNITY_EDITOR))
                    src.Write(Alias.EditorInit);

                src.Line.Write(Alias.RuntimeInit);
                src.Line.Write("static void InitializeUnmanagedLayout()");
                using (src.Indent)
                    src.Line.Write("=> unmanagedLayoutStorage.Data = new()");

                using (src.Indent)
                using (src.Braces)
                    foreach (var x in fields)
                        src.Line.Write($"{x.Name} = ᵐUtility.GetFieldOffset(typeof({m}Self), \"{x.MetadataName}\", ᵐBF.{ToBindingFlagsVisibility(x.Flags.Has(FieldFlags.IsPublic))} | ᵐBF.Instance),");

                src.Write(';');
            }

            src.Linebreak();

            EmitDoc(
                "/// <summary>",
                "/// Mutable unmanaged access collection wrapper.",
                "/// </summary>"
            );

            src.Line.Write("public partial struct AccessArray");
            using (src.Braces)
            {
                src.Line.Write($"Medicine.Internal.UnmanagedAccessArray<{m}Self, Layout, AccessRW, AccessRO> impl;");
                src.Linebreak();

                EmitDoc(
                    "/// <summary>",
                    "/// Creates an access wrapper from an unmanaged reference list.",
                    "/// </summary>"
                );

                src.Line.Write($"public AccessArray(global::Unity.Collections.LowLevel.Unsafe.UnsafeList<Medicine.UnmanagedRef<{m}Self>> classRefArray)");
                using (src.Indent)
                    src.Line.Write("=> impl = new(classRefArray);");

                src.Linebreak();

                EmitDoc(
                    "/// <summary>",
                    "/// Creates an access wrapper from an array of managed instances.",
                    "/// </summary>"
                );

                src.Line.Write($"public AccessArray({m}Self[]? classArray)");
                using (src.Indent)
                    src.Line.Write($"=> impl = new(ᵐUtility.AsUnsafeList<{m}Self, Medicine.UnmanagedRef<{m}Self>>(classArray));");

                src.Linebreak();

                EmitDoc(
                    "/// <summary>",
                    "/// Rebinds this wrapper to a different unmanaged reference list.",
                    "/// </summary>"
                );

                src.Line.Write($"public void UpdateBuffer(global::Unity.Collections.LowLevel.Unsafe.UnsafeList<Medicine.UnmanagedRef<{m}Self>> classRefArray)");
                using (src.Indent)
                    src.Line.Write("=> impl.UpdateBuffer(classRefArray);");

                src.Linebreak();

                EmitDoc(
                    "/// <summary>",
                    "/// Rebinds this wrapper to a different managed instance array.",
                    "/// </summary>"
                );

                src.Line.Write($"public void UpdateBuffer({m}Self[]? classArray)");
                using (src.Indent)
                    src.Line.Write($"=> impl.UpdateBuffer(ᵐUtility.AsUnsafeList<{m}Self, Medicine.UnmanagedRef<{m}Self>>(classArray));");

                src.Linebreak();

                EmitDoc(
                    "/// <summary>",
                    "/// Returns the number of available elements.",
                    "/// </summary>"
                );

                src.Line.Write($"public int Length");
                using (src.Indent)
                    src.Line.Write("=> impl.Length;");

                src.Linebreak();

                EmitDoc(
                    "/// <summary>",
                    "/// Returns mutable unmanaged field access for the specified element index.",
                    "/// </summary>"
                );

                src.Line.Write("public AccessRW this[int index]");
                using (src.Braces)
                    src.Line.Write($"{Alias.Inline} get => impl[index];");

                src.Linebreak();

                EmitDoc(
                    "/// <summary>",
                    "/// Returns a sliced view of this access collection.",
                    "/// </summary>"
                );

                src.Line.Write("public AccessArray this[global::System.Range range]");
                using (src.Braces)
                {
                    src.Line.Write("get");
                    using (src.Braces)
                    {
                        src.Line.Write("AccessArray accessArray = new();");
                        src.Line.Write("accessArray.impl = impl[range];");
                        src.Line.Write("return accessArray;");
                    }
                }

                src.Linebreak();

                EmitDoc(
                    "/// <summary>",
                    "/// Converts this wrapper to a read-only view.",
                    "/// </summary>"
                );

                src.Line.Write(Alias.Inline);
                src.Line.Write("public ReadOnly AsReadOnly()");
                using (src.Indent)
                    src.Line.Write($"=> new(impl);");

                src.Linebreak();

                EmitDoc(
                    "/// <summary>",
                    "/// Returns an enumerator over mutable access elements.",
                    "/// </summary>"
                );

                src.Line.Write(Alias.Inline);
                src.Line.Write($"public Medicine.Internal.UnmanagedAccessArray<{m}Self, Layout, AccessRW, AccessRO>.Enumerator GetEnumerator()");

                src.Linebreak();

                using (src.Indent)
                    src.Line.Write("=> impl.GetEnumerator();");

                src.Linebreak();

                EmitDoc(
                    "/// <summary>",
                    "/// Read-only view over an <see cref=\"AccessArray\"/>.",
                    "/// </summary>"
                );

                src.Line.Write("public readonly partial struct ReadOnly");
                using (src.Braces)
                {
                    src.Line.Write($"readonly Medicine.Internal.UnmanagedAccessArray<{m}Self, Layout, AccessRW, AccessRO>.ReadOnly impl;");
                    src.Linebreak();

                    EmitDoc(
                        "/// <summary>",
                        "/// Creates a read-only view from a mutable access wrapper.",
                        "/// </summary>"
                    );

                    src.Line.Write($"public ReadOnly(Medicine.Internal.UnmanagedAccessArray<{m}Self, Layout, AccessRW, AccessRO> accessArray)");
                    using (src.Indent)
                        src.Line.Write("=> impl = accessArray.AsReadOnly();");

                    src.Linebreak();

                    EmitDoc(
                        "/// <summary>",
                        "/// Creates a read-only view from an existing read-only access wrapper.",
                        "/// </summary>"
                    );

                    src.Line.Write($"public ReadOnly(Medicine.Internal.UnmanagedAccessArray<{m}Self, Layout, AccessRW, AccessRO>.ReadOnly accessArray)");
                    using (src.Indent)
                        src.Line.Write("=> impl = accessArray;");

                    src.Linebreak();

                    EmitDoc(
                        "/// <summary>",
                        "/// Returns the number of available elements.",
                        "/// </summary>"
                    );

                    src.Line.Write($"public int Length");
                    using (src.Indent)
                        src.Line.Write("=> impl.Length;");

                    src.Linebreak();

                    EmitDoc(
                        "/// <summary>",
                        "/// Returns read-only unmanaged field access for the specified element index.",
                        "/// </summary>"
                    );

                    src.Line.Write("public AccessRO this[int index]");
                    using (src.Braces)
                        src.Line.Write($"{Alias.Inline} get => impl[index];");

                    src.Linebreak();

                    EmitDoc(
                        "/// <summary>",
                        "/// Returns a sliced read-only view of this access collection.",
                        "/// </summary>"
                    );

                    src.Line.Write("public ReadOnly this[global::System.Range range]");
                    using (src.Indent)
                        src.Line.Write("=> new(impl[range]);");

                    src.Linebreak();

                    EmitDoc(
                        "/// <summary>",
                        "/// Returns an enumerator over read-only access elements.",
                        "/// </summary>"
                    );

                    src.Line.Write(Alias.Inline);
                    src.Line.Write($"public Medicine.Internal.UnmanagedAccessArray<{m}Self, Layout, AccessRW, AccessRO>.ReadOnly.Enumerator GetEnumerator()");
                    using (src.Indent)
                        src.Line.Write("=> impl.GetEnumerator();");
                }
            }

            src.Linebreak();

            EmitAccessStruct("AccessRW", false);
            EmitAccessStruct("AccessRO", true);

            void EmitAccessStruct(string accessStructName, bool isReadOnly)
            {
                string safetyCheckMethodName = input.IsUnityObject
                    ? "CheckNullOrDestroyed"
                    : "CheckNull";

                EmitDoc(
                    "/// <summary>",
                    isReadOnly
                        ? "/// Read-only unmanaged field accessor for a single instance."
                        : "/// Read-write unmanaged field accessor for a single instance.",
                    "/// </summary>"
                );

                src.Line.Write($"public readonly unsafe partial struct {accessStructName}");
                using (src.Braces)
                {
                    EmitDoc(
                        "/// <summary>",
                        "/// Underlying unmanaged reference.",
                        "/// </summary>"
                    );

                    src.Line.Write($"public readonly Medicine.UnmanagedRef<{m}Self> Ref;");
                    src.Line.Write("readonly Layout* layoutInfo;");
                    src.Linebreak();

                    EmitDoc(
                        "/// <summary>",
                        "/// Layout metadata used for field offset lookup.",
                        "/// </summary>"
                    );

                    src.Line.Write("public ref readonly Layout Layout");
                    using (src.Indent)
                        src.Line.Write($"=> ref *layoutInfo;");

                    src.Linebreak();

                    EmitDoc(
                        "/// <summary>",
                        "/// Initializes the accessor using the globally cached class layout.",
                        "/// </summary>"
                    );

                    src.Line.Write(Alias.Inline);
                    src.Line.Write($"public {accessStructName}(Medicine.UnmanagedRef<{m}Self> Ref)");
                    using (src.Braces)
                    {
                        src.Line.Write("this.Ref = Ref;");
                        src.Line.Write("layoutInfo = (Layout*)unmanagedLayoutStorage.UnsafeDataPointer;");
                    }

                    EmitDoc(
                        "/// <summary>",
                        "/// Initializes the accessor using a caller-provided layout reference.",
                        "/// </summary>"
                    );

                    src.Line.Write(Alias.Inline);
                    src.Line.Write($"public {accessStructName}(Medicine.UnmanagedRef<{m}Self> Ref, ref Layout layout)");
                    using (src.Braces)
                    {
                        src.Line.Write("this.Ref = Ref;");
                        src.Line.Write($"layoutInfo = (Layout*){m}UU.AddressOf(ref layout);");
                    }

                    src.Linebreak();

                    void PropertyWithSafetyChecks(string call)
                    {
                        using (src.Braces)
                        {
                            src.Line.Write($"{Alias.Inline} get");
                            {
                                if (input.AttributeSettings.SafetyChecks)
                                {
                                    using (src.Braces)
                                    {
                                        src.Line.Write($"{safetyCheckMethodName}();");
                                        src.Line.Write($"return {call}");
                                    }
                                }
                                else
                                {
                                    using (src.Indent)
                                        src.Line.Write($"=> {call}");
                                }
                            }
                        }

                        src.Linebreak();
                    }

                    if (input.IsUnityObject)
                    {
                        EmitDoc($"/// <inheritdoc cref=\"Medicine.UnmanagedRefExtensions.IsValid\" />");
                        src.Line.Write($"public bool IsValid");
                        using (src.Indent)
                            src.Line.Write($"=> Medicine.UnmanagedRefExtensions.IsValid(Ref);");

                        EmitDoc($"/// <inheritdoc cref=\"Medicine.UnmanagedRefExtensions.IsInvalid\" />");
                        src.Line.Write($"public bool IsInvalid");
                        using (src.Indent)
                            src.Line.Write($"=> Medicine.UnmanagedRefExtensions.IsInvalid(Ref);");

                        if (input.UsesEntityId)
                        {
                            EmitDoc($"/// <inheritdoc cref=\"Medicine.UnmanagedRefExtensions.GetEntityID\" />");
                            src.Line.Write($"public global::UnityEngine.EntityId EntityID");
                            PropertyWithSafetyChecks($"Medicine.UnmanagedRefExtensions.GetEntityID(Ref);");

                            EmitDoc("/// <summary>Legacy Unity object identity API. Use <see cref=\"EntityID\"/> on Unity 6000.4 or newer.</summary>");
                            src.Line.Write($"[global::System.Obsolete(\"{InstanceIdMigrationMessage}\", true)]");
                            src.Line.Write("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
                            src.Line.Write($"public int InstanceID");
                            ObsoleteInstanceIdProperty();
                        }
                        else
                        {
                            EmitDoc($"/// <inheritdoc cref=\"Medicine.UnmanagedRefExtensions.GetInstanceID\" />");
                            src.Line.Write($"public int InstanceID");
                            PropertyWithSafetyChecks($"Medicine.UnmanagedRefExtensions.GetInstanceID(Ref);");
                        }
                    }

                    void ObsoleteInstanceIdProperty()
                    {
                        using (src.Braces)
                        {
                            src.Line.Write($"{Alias.Inline} get");
                            using (src.Braces)
                                src.Line.Write($"throw new global::System.NotSupportedException(\"{InstanceIdMigrationMessage}\");");
                        }

                        src.Linebreak();
                    }

                    string GetProjectedType(in FieldInfo field)
                    {
                        if (field.Flags.Has(FieldFlags.IsUnmanagedType) || field.Flags.Has(FieldFlags.IsManagedValueType))
                            return $"{(isReadOnly || field.Flags.Has(FieldFlags.IsReadOnly) ? "ref readonly" : "ref")} {field.TypeFQN}";

                        if (field.EmitsDirectAccess)
                            return $"{field.TypeFQN}.Unmanaged.{(isReadOnly ? "AccessRO" : "AccessRW")}";

                        if (field.EmitsUnmanagedArray)
                            return isReadOnly
                                ? $"global::Unity.Collections.NativeArray<{field.ArrayElementTypeFQN}>.ReadOnly"
                                : $"global::Unity.Collections.NativeArray<{field.ArrayElementTypeFQN}>";

                        if (field.EmitsAccessArray)
                            return isReadOnly
                                ? $"{field.ArrayElementTypeFQN}.Unmanaged.AccessArray.ReadOnly"
                                : $"{field.ArrayElementTypeFQN}.Unmanaged.AccessArray";

                        return $"{(isReadOnly || field.Flags.Has(FieldFlags.IsReadOnly) ? "ref readonly" : "ref")} Medicine.UnmanagedRef<{field.TypeFQN}>";
                    }

                    string GetProjectedAccess(in FieldInfo field)
                    {
                        if (field.Flags.Has(FieldFlags.IsUnmanagedType))
                            return $"ref Ref.Read<{field.TypeFQN}>(layoutInfo->{field.Name});";

                        if (field.Flags.Has(FieldFlags.IsManagedValueType))
                            return $"ref {m}UU.AsRef<{field.TypeFQN}>((void*)(Ref.Ptr + layoutInfo->{field.Name}));";

                        if (field.EmitsDirectAccess)
                            return $"new {field.TypeFQN}.Unmanaged.{(isReadOnly ? "AccessRO" : "AccessRW")}(Ref.Read<Medicine.UnmanagedRef<{field.TypeFQN}>>(layoutInfo->{field.Name}));";

                        if (field.EmitsUnmanagedArray)
                            return $"ᵐUtility.AsNativeArray{(isReadOnly ? "RO" : string.Empty)}(Ref.Read<Medicine.UnmanagedRef<{field.TypeFQN}>>(layoutInfo->{field.Name}).Resolve());";

                        if (field.EmitsAccessArray)
                        {
                            var accessArray = $"new {field.ArrayElementTypeFQN}.Unmanaged.AccessArray(Ref.Read<Medicine.UnmanagedRef<{field.TypeFQN}>>(layoutInfo->{field.Name}).Resolve())";
                            return isReadOnly
                                ? $"{accessArray}.AsReadOnly();"
                                : $"{accessArray};";
                        }

                        return $"ref Ref.Read<Medicine.UnmanagedRef<{field.TypeFQN}>>(layoutInfo->{field.Name});";
                    }

                    foreach (var x in fields)
                    {
                        if (x.Flags.Has(FieldFlags.IsUnmanagedType)
                            || x.Flags.Has(FieldFlags.IsReferenceType)
                            || x.Flags.Has(FieldFlags.IsManagedValueType))
                        {
                            EmitDoc($"/// <inheritdoc cref=\"{m}Self.{x.Name}\" />");

                            // base-private fields aren't accessible from here, so we omit `DeclaredAt`
                            if (!x.Flags.Has(FieldFlags.IsPrivateInBaseType))
                                src.Line.Write($"[{m}DeclaredAt(nameof({m}Self.{x.Name}))]");
                        }

                        src.Line.Write($"public {GetProjectedType(x)} {x.Name}");
                        PropertyWithSafetyChecks(GetProjectedAccess(x));
                    }

                    if (input.AttributeSettings.SafetyChecks)
                    {
                        src.Line.Write(Alias.Inline);
                        src.Line.Write($"void {safetyCheckMethodName}()");
                        using (src.Braces)
                        {
                            if (input.IsUnityObject)
                            {
                                src.Line.Write($"if (Medicine.UnmanagedRefExtensions.IsInvalid(Ref))");
                                using (src.Braces)
                                {
                                    src.Line.Write($"if (Ref.Ptr is 0)");
                                    using (src.Indent)
                                        src.Line.Write($"ThrowNullException();");

                                    src.Line.Write("else");
                                    using (src.Indent)
                                        src.Line.Write($"ThrowDestroyedException();");
                                }
                            }
                            else
                            {
                                src.Line.Write($"if (Ref.Ptr is 0)");
                                using (src.Indent)
                                    src.Line.Write($"ThrowNullException();");
                            }

                            src.Linebreak();

                            src.Line.Write($"{Alias.NoInline}");
                            src.Line.Write($"static void ThrowNullException()");
                            using (src.Indent)
                                src.Line.Write($"=> throw new System.InvalidOperationException(\"Attempted to access a null {input.ClassName} instance.\");");

                            if (input.IsUnityObject)
                            {
                                src.Linebreak();

                                src.Line.Write($"{Alias.NoInline}");
                                src.Line.Write($"static void ThrowDestroyedException()");
                                using (src.Indent)
                                    src.Line.Write($"=> throw new System.InvalidOperationException(\"Attempted to access a destroyed {input.ClassName} instance.\");");
                            }
                        }
                    }

                    src.Linebreak();
                }

                src.Linebreak();
            }
        }

        foreach (var _ in input.ContainingTypeDeclaration.AsArray())
            src.CloseBrace();

        src.Linebreak();

        src.Line.Write("namespace Medicine");
        using (src.Braces)
        {
            EmitDoc(
                "/// <summary>",
                $"/// Extension methods for <see cref=\"UnmanagedRef{{T}}\"/> when <c>T</c> is <see cref=\"{input.ClassFQN}\"/>.",
                "/// </summary>"
            );

            src.Line.Write("public static partial class UnmanagedAccessExtensions");
            using (src.Braces)
            {
                EmitDoc(
                    "/// <summary>",
                    "/// Returns a generated read-write unmanaged accessor.",
                    "/// </summary>"
                );

                src.Line.Write(Alias.Inline);
                src.Line.Write($"public static {m}Self.Unmanaged.AccessRW AccessRW(this ref UnmanagedRef<{m}Self> classRef)");
                using (src.Indent)
                    src.Line.Write("=> new(classRef);");

                src.Linebreak();

                EmitDoc($"/// <inheritdoc cref=\"AccessRW(ref UnmanagedRef{{T}})\" />");
                src.Line.Write(Alias.Inline);
                src.Line.Write($"public static {m}Self.Unmanaged.AccessRW AccessRW(this ref UnmanagedRef<{m}Self> classRef, ref {m}Self.Unmanaged.Layout layout)");
                using (src.Indent)
                    src.Line.Write("=> new(classRef, ref layout);");

                src.Linebreak();

                EmitDoc(
                    "/// <summary>",
                    "/// Returns a generated read-only unmanaged accessor.",
                    "/// </summary>"
                );

                src.Line.Write(Alias.Inline);
                src.Line.Write($"public static {m}Self.Unmanaged.AccessRO AccessRO(this UnmanagedRef<{m}Self> classRef)");
                using (src.Indent)
                    src.Line.Write("=> new(classRef);");

                src.Linebreak();

                src.Linebreak();
                EmitDoc($"/// <inheritdoc cref=\"AccessRO(UnmanagedRef{{T}})\" />");
                src.Line.Write(Alias.Inline);
                src.Line.Write($"public static {m}Self.Unmanaged.AccessRO AccessRO(this UnmanagedRef<{m}Self> classRef, ref {m}Self.Unmanaged.Layout layout)");
                using (src.Indent)
                    src.Line.Write("=> new(classRef, ref layout);");

                src.Linebreak();
            }
        }
    }
}
