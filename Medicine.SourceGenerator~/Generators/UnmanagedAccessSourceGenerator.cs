using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using static Constants;
using static Microsoft.CodeAnalysis.SymbolDisplayFormat;

[Generator]
public sealed class UnmanagedAccessSourceGenerator : IIncrementalGenerator
{
    record struct GeneratorInput : IGeneratorTransformOutput
    {
        public string? SourceGeneratorOutputFilename { get; init; }
        public string? SourceGeneratorError { get; init; }
        public EquatableIgnore<Location?> SourceGeneratorErrorLocation { get; set; }

        public EquatableArray<string> ContainingTypeDeclaration;
        public string ClassName;
        public string ClassFQN;
        public bool SafetyChecks;
        public bool IsUnityObject;
        public bool IsTracked;
        public MedicineSettings MedicineSettings;
        public EquatableArray<FieldInfo> Fields;
    }

    record struct FieldInfo
    {
        public string Name;
        public string MetadataName;
        public string TypeFQN;
        public string Visibility;
        public bool IsReadOnly;
        public bool IsUnmanagedType;
        public bool IsReferenceType;
    }

    static readonly SourceText extensionsSrc
        = SourceText.From(
            $$"""
              namespace Medicine
              {
                  {{Alias.Hidden}}
                  public static partial class UnmanagedAccessExtensions { }
              }
              """,
            Encoding.UTF8
        );

    void IIncrementalGenerator.Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(x => x.AddSource("UnmanagedAccessExtensions.g.cs", extensionsSrc));

        var settingsProvider = context.CompilationProvider
            .Combine(context.ParseOptionsProvider)
            .Select((x, ct) => new MedicineSettings(x, ct));

        var inputProvider = context
            .SyntaxProvider
            .ForAttributeWithMetadataNameEx(
                fullyQualifiedMetadataName: UnmanagedAccessAttributeMetadataName,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: TransformSyntaxContext
            )
            .Combine(settingsProvider)
            .Select((x, ct) => x.Left with { MedicineSettings = x.Right });

        context.RegisterSourceOutputEx(
            source: inputProvider,
            action: GenerateSource
        );
    }

    static GeneratorInput TransformSyntaxContext(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetNode is not TypeDeclarationSyntax typeDecl)
            return default;

        if (context.TargetSymbol is not ITypeSymbol typeSymbol)
            return default;

        var unmanagedAccessAttribute = context.Attributes.First(x => x.AttributeClass.Is(UnmanagedAccessAttributeFQN));

        bool safetyChecks = unmanagedAccessAttribute
            .GetAttributeConstructorArguments(ct)
            .Get("safetyChecks", true) || true;

        var trackAttribute = context.TargetSymbol.GetAttribute(TrackAttributeFQN);

        bool hasCachedEnable = trackAttribute?
            .GetAttributeConstructorArguments(ct)
            .Get("cacheEnabledState", false) ?? false;

        bool hasIInstanceIndex
            = typeSymbol.HasInterface(IInstanceIndexInterfaceFQN, checkAllInterfaces: false);

        var output = new GeneratorInput
        {
            SourceGeneratorOutputFilename = Utility.GetOutputFilename(
                filePath: typeDecl.SyntaxTree.FilePath,
                targetFQN: typeSymbol.ToDisplayString(CSharpShortErrorMessageFormat),
                label: "UnmanagedAccess",
                includeFilename: false
            ),
            ContainingTypeDeclaration = Utility.DeconstructTypeDeclaration(typeDecl, context.SemanticModel, ct),
            ClassName = typeSymbol.Name,
            ClassFQN = typeSymbol.FQN,
            IsUnityObject = typeSymbol.InheritsFrom("global::UnityEngine.Object"),
            IsTracked = trackAttribute is not null,
            SafetyChecks = safetyChecks,
        };

        var fields = new List<FieldInfo>(capacity: 16);

        if (hasCachedEnable)
        {
            fields.Add(
                new()
                {
                    Name = "enabled",
                    MetadataName = $"{m}MedicineInternalCachedEnabledState",
                    TypeFQN = "global::System.Boolean",
                    Visibility = "NonPublic",
                    IsReadOnly = true,
                    IsReferenceType = false,
                    IsUnmanagedType = true,
                }
            );
        }

        if (hasIInstanceIndex)
        {
            fields.Add(
                new()
                {
                    Name = "InstanceIndex",
                    MetadataName = $"{m}MedicineInternalInstanceIndex",
                    TypeFQN = "global::System.Int32",
                    Visibility = "NonPublic",
                    IsReadOnly = true,
                    IsReferenceType = false,
                    IsUnmanagedType = true,
                }
            );
        }

        foreach (var member in typeSymbol.GetMembers().AsArray())
            CollectField(member);

        foreach (var member in typeSymbol.GetBaseTypes().SelectMany(x => x.GetMembers().AsArray()))
            CollectField(member);

        output.Fields = fields.ToArray();

        if (output.Fields.Length is 0)
        {
            return output with
            {
                SourceGeneratorError = "Class marked with [UnmanagedAccess] has no instance fields or properties with backing fields.",
                SourceGeneratorErrorLocation = typeDecl.Identifier.GetLocation(),
            };
        }

        return output;

        void CollectField(ISymbol member)
        {
            if (member is not IFieldSymbol { IsStatic: false } field)
                return;

            fields.Add(
                new()
                {
                    MetadataName = field.Name,
                    Name = field.IsImplicitlyDeclared
                        ? field.Name[1..^16]
                        : field.Name,
                    TypeFQN = field.Type.FQN,
                    Visibility = field.DeclaredAccessibility is Accessibility.Public ? "Public" : "NonPublic",
                    IsReadOnly = field.IsReadOnly,
                    IsUnmanagedType = field.Type.IsUnmanagedType,
                    IsReferenceType = field.Type.IsReferenceType,
                }
            );
        }
    }

    static void GenerateSource(SourceProductionContext context, SourceWriter src, GeneratorInput input)
    {
        if (!input.MedicineSettings.PreprocessorSymbolNames.Has(ActivePreprocessorSymbolNames.DEBUG))
            input.SafetyChecks = false;

        src.Line.Write(Alias.UsingStorage);
        src.Line.Write(Alias.UsingInline);
        src.Line.Write(Alias.UsingUtility);
        src.Line.Write(Alias.UsingBindingFlags);
        src.Line.Write(Alias.UsingUnsafeUtility);
        src.Line.Write($"using {m}Self = {input.ClassFQN};");
        src.Linebreak();

        foreach (var x in input.ContainingTypeDeclaration.AsArray())
        {
            src.Line.Write(x);
            src.OpenBrace();
        }

        src.Line.Write("public static partial class Unmanaged");
        using (src.Braces)
        {
            src.Line.Write($"static readonly global::Unity.Burst.SharedStatic<Layout> unmanagedLayoutStorage");
            using (src.Indent)
                src.Line.Write($"= global::Unity.Burst.SharedStatic<Layout>.GetOrCreate<Layout>();");

            src.Linebreak();

            src.Line.Write("public static ref Layout ClassLayout");
            using (src.Braces)
                src.Line.Write($"{Alias.Inline} get => ref unmanagedLayoutStorage.Data;");

            src.Linebreak();

            if (input.IsTracked)
            {
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
            src.Line.Write("public readonly struct Layout");
            using (src.Braces)
            {
                foreach (var x in input.Fields.AsArray())
                    src.Line.Write($"public ushort {x.Name} {{ get; init; }}");

                src.Linebreak();

                src.Line.Write("[global::UnityEngine.RuntimeInitializeOnLoadMethod(global::UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]");
                src.Line.Write("static void InitializeUnmanagedLayout()");
                using (src.Indent)
                    src.Line.Write("=> unmanagedLayoutStorage.Data = new()");

                using (src.Indent)
                using (src.Braces)
                    foreach (var x in input.Fields.AsArray())
                        src.Line.Write($"{x.Name} = ᵐUtility.GetFieldOffset(typeof({m}Self), \"{x.MetadataName}\", ᵐBF.{x.Visibility} | ᵐBF.Instance),");

                src.Write(';');
            }

            src.Linebreak();

            src.Line.Write("public partial struct AccessArray");
            using (src.Braces)
            {
                src.Line.Write($"Medicine.Internal.UnmanagedAccessArray<{m}Self, Layout, AccessRW, AccessRO> impl;");
                src.Linebreak();

                src.Line.Write($"public AccessArray(global::Unity.Collections.LowLevel.Unsafe.UnsafeList<Medicine.UnmanagedRef<{m}Self>> classRefArray)");
                using (src.Indent)
                    src.Line.Write("=> impl = new(classRefArray);");

                src.Linebreak();

                src.Line.Write($"public void UpdateBuffer(global::Unity.Collections.LowLevel.Unsafe.UnsafeList<Medicine.UnmanagedRef<{m}Self>> classRefArray)");
                using (src.Indent)
                    src.Line.Write("=> impl.UpdateBuffer(classRefArray);");

                src.Linebreak();

                src.Line.Write($"public int Length");
                using (src.Indent)
                    src.Line.Write("=> impl.Length;");

                src.Linebreak();

                src.Line.Write("public AccessRW this[int index]");
                using (src.Braces)
                    src.Line.Write($"{Alias.Inline} get => impl[index];");

                src.Linebreak();

                src.Line.Write(Alias.Inline);
                src.Line.Write("public ReadOnly AsReadOnly()");
                using (src.Indent)
                    src.Line.Write($"=> new(impl);");

                src.Linebreak();

                src.Line.Write(Alias.Inline);
                src.Line.Write($"public Medicine.Internal.UnmanagedAccessArray<{m}Self, Layout, AccessRW, AccessRO>.Enumerator GetEnumerator()");

                src.Linebreak();

                using (src.Indent)
                    src.Line.Write("=> impl.GetEnumerator();");

                src.Linebreak();

                src.Line.Write("public readonly partial struct ReadOnly");
                using (src.Braces)
                {
                    src.Line.Write($"readonly Medicine.Internal.UnmanagedAccessArray<{m}Self, Layout, AccessRW, AccessRO>.ReadOnly impl;");
                    src.Linebreak();

                    src.Line.Write($"public ReadOnly(Medicine.Internal.UnmanagedAccessArray<{m}Self, Layout, AccessRW, AccessRO> accessArray)");
                    using (src.Indent)
                        src.Line.Write("=> impl = accessArray.AsReadOnly();");

                    src.Linebreak();

                    src.Line.Write($"public int Length");
                    using (src.Indent)
                        src.Line.Write("=> impl.Length;");

                    src.Linebreak();

                    src.Line.Write("public AccessRO this[int index]");
                    using (src.Braces)
                        src.Line.Write($"{Alias.Inline} get => impl[index];");

                    src.Linebreak();

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
                src.Line.Write($"public readonly unsafe partial struct {accessStructName}");
                using (src.Braces)
                {
                    src.Line.Write($"public readonly Medicine.UnmanagedRef<{m}Self> Ref;");
                    src.Line.Write("readonly Layout* layoutInfo;");
                    src.Linebreak();

                    src.Line.Write("public ref readonly Layout Layout");
                    using (src.Indent)
                        src.Line.Write($"=> ref *layoutInfo;");

                    src.Linebreak();

                    src.Line.Write(Alias.Inline);
                    src.Line.Write($"public {accessStructName}(Medicine.UnmanagedRef<{m}Self> Ref)");
                    using (src.Braces)
                    {
                        src.Line.Write("this.Ref = Ref;");
                        src.Line.Write("layoutInfo = (Layout*)unmanagedLayoutStorage.UnsafeDataPointer;");
                    }

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
                                if (input.SafetyChecks)
                                {
                                    using (src.Braces)
                                    {
                                        src.Line.Write($"CheckDestroyed();");
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
                        src.Line.Write($"/// <inheritdoc cref=\"Medicine.UnmanagedRefExtensions.IsDestroyed\" />");
                        src.Line.Write($"public bool IsDestroyed");
                        PropertyWithSafetyChecks("Medicine.UnmanagedRefExtensions.IsDestroyed(Ref);");

                        src.Line.Write($"/// <inheritdoc cref=\"Medicine.UnmanagedRefExtensions.GetInstanceID\" />");
                        src.Line.Write($"public int InstanceID");
                        PropertyWithSafetyChecks($"Medicine.UnmanagedRefExtensions.GetInstanceID(Ref);");
                    }

                    foreach (var x in input.Fields.AsArray())
                    {
                        string ro = isReadOnly || x.IsReadOnly ? " readonly" : "";
                        if (x.IsUnmanagedType)
                        {
                            src.Line.Write($"/// <inheritdoc cref=\"{m}Self.{x.Name}\" />");
                            src.Line.Write($"public ref{ro} {x.TypeFQN} {x.Name}");
                            PropertyWithSafetyChecks($"ref Ref.Read<{x.TypeFQN}>(layoutInfo->{x.Name});");
                        }
                        else if (x.IsReferenceType)
                        {
                            src.Line.Write($"/// <inheritdoc cref=\"{m}Self.{x.Name}\" />");
                            src.Line.Write($"public ref{ro} Medicine.UnmanagedRef<{x.TypeFQN}> {x.Name}");
                            PropertyWithSafetyChecks($"ref Ref.Read<Medicine.UnmanagedRef<{x.TypeFQN}>>(layoutInfo->{x.Name});");

                            src.Linebreak();
                        }
                    }

                    if (input.SafetyChecks)
                    {
                        src.Line.Write(Alias.Inline);
                        src.Line.Write($"void CheckDestroyed()");
                        using (src.Braces)
                        {
                            src.Line.Write($"if (Medicine.UnmanagedRefExtensions.IsDestroyed(Ref))");
                            using (src.Braces)
                            {
                                src.Line.Write($"if (Ref.Ptr is 0)");
                                using (src.Indent)
                                    src.Line.Write($"ThrowNullException();");

                                src.Line.Write("else");
                                using (src.Indent)
                                    src.Line.Write($"ThrowDestroyedException();");
                            }

                            src.Linebreak();

                            src.Line.Write($"{Alias.NoInline}");
                            src.Line.Write($"static void ThrowNullException()");
                            using (src.Indent)
                                src.Line.Write($"=> throw new System.InvalidOperationException(\"Attempted to access a null {input.ClassName} instance.\");");

                            src.Linebreak();

                            src.Line.Write($"{Alias.NoInline}");
                            src.Line.Write($"static void ThrowDestroyedException()");
                            using (src.Indent)
                                src.Line.Write($"=> throw new System.InvalidOperationException(\"Attempted to access a destroyed {input.ClassName} instance.\");");
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
            src.Line.Write("public static partial class UnmanagedAccessExtensions");
            using (src.Braces)
            {
                src.Line.Write($"/// <summary>");
                src.Line.Write($"/// Returns an <see cref=\"AccessRW\"/> struct that can be used to read and write fields of the given class.");
                src.Line.Write($"/// </summary>");
                src.Line.Write(Alias.Inline);
                src.Line.Write($"public static {m}Self.Unmanaged.AccessRW AccessRW(this ref UnmanagedRef<{m}Self> classRef)");
                using (src.Indent)
                    src.Line.Write("=> new(classRef);");

                src.Linebreak();

                src.Line.Write($"/// <inheritdoc cref=\"AccessRW(ref UnmanagedRef{{T}})\" />");
                src.Line.Write(Alias.Inline);
                src.Line.Write($"public static {m}Self.Unmanaged.AccessRW AccessRW(this ref UnmanagedRef<{m}Self> classRef, ref {m}Self.Unmanaged.Layout layout)");
                using (src.Indent)
                    src.Line.Write("=> new(classRef, ref layout);");

                src.Linebreak();

                src.Line.Write($"/// <summary>");
                src.Line.Write($"/// Returns an <see cref=\"AccessRO\"/> struct that can be used to read fields of the given class.");
                src.Line.Write($"/// </summary>");
                src.Line.Write(Alias.Inline);
                src.Line.Write($"public static {m}Self.Unmanaged.AccessRO AccessRO(this UnmanagedRef<{m}Self> classRef)");
                using (src.Indent)
                    src.Line.Write("=> new(classRef);");

                src.Linebreak();

                src.Linebreak();
                src.Line.Write($"/// <inheritdoc cref=\"AccessRO(UnmanagedRef{{T}})\" />");
                src.Line.Write(Alias.Inline);
                src.Line.Write($"public static {m}Self.Unmanaged.AccessRO AccessRO(this UnmanagedRef<{m}Self> classRef, ref {m}Self.Unmanaged.Layout layout)");
                using (src.Indent)
                    src.Line.Write("=> new(classRef, ref layout);");

                src.Linebreak();
            }
        }
    }
}