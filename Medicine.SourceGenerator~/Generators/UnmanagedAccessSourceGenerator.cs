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

        // public EquatableArray<string> NamespaceImports;
        public EquatableArray<string> ContainingTypeDeclaration;

        public string ClassFQN;

        // public string ClassName;
        public bool IsUnityObject;
        public bool IsTracked;
        public EquatableArray<FieldInfo> Fields;
    }

    record struct FieldInfo
    {
        public string Name;
        public string MetadataName;
        public string TypeFQN;
        public string Visibility;
        public bool IsReadOnly;
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
        var syntaxProvider = context.SyntaxProvider
            .ForAttributeWithMetadataNameEx(
                fullyQualifiedMetadataName: UnmanagedAccessAttributeMetadataName,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: TransformSyntaxContext
            );

        context.RegisterPostInitializationOutput(x => x.AddSource("UnmanagedAccessExtensions.g.cs", extensionsSrc));

        context.RegisterSourceOutputEx(
            source: syntaxProvider,
            action: GenerateSource
        );
    }

    static GeneratorInput TransformSyntaxContext(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetNode is not TypeDeclarationSyntax typeDecl)
            return default;

        if (context.TargetSymbol is not ITypeSymbol typeSymbol)
            return default;

        var trackAttribute = context.TargetSymbol.GetAttribute(TrackAttributeFQN);

        bool hasCachedEnable = trackAttribute?
            .GetAttributeConstructorArguments(ct)
            .Select(x => x.Get("cacheEnabledState", false)) ?? false;

        var output = new GeneratorInput
        {
            SourceGeneratorOutputFilename = Utility.GetOutputFilename(
                filePath: typeDecl.SyntaxTree.FilePath,
                targetFQN: typeSymbol.ToDisplayString(CSharpShortErrorMessageFormat),
                label: "UnmanagedAccess",
                includeFilename: false
            ),
            ContainingTypeDeclaration = Utility.DeconstructTypeDeclaration(typeDecl, context.SemanticModel, ct),
            ClassFQN = typeSymbol.FQN,
            // ClassName = typeSymbol.Name,
            IsUnityObject = typeSymbol.InheritsFrom("global::UnityEngine.Object"),
            IsTracked = trackAttribute is not null,
        };

        // if (typeDecl.SyntaxTree.GetRoot(ct) is CompilationUnitSyntax compilationUnit)
        //     output.NamespaceImports = compilationUnit.Usings.Select(x => x.ToString()).ToArray();

        var members = typeSymbol.GetMembers().AsArray();
        var fields = new List<FieldInfo>(capacity: members.Length);

        foreach (var member in members)
        {
            if (member is not IFieldSymbol { IsStatic: false, Type.IsUnmanagedType: true } field)
                continue;

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
                }
            );
        }

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
                }
            );
        }

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
    }

    static void GenerateSource(SourceProductionContext context, SourceWriter src, GeneratorInput input)
    {
        // foreach (var x in input.NamespaceImports.AsArray())
        //     src.Line.Write(x);

        src.Line.Write(Alias.UsingStorage);
        src.Line.Write(Alias.UsingInline);
        src.Line.Write(Alias.UsingBindingFlags);
        src.Line.Write(Alias.UsingUnsafeUtility);
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
                        src.Line.Write($"arr.UpdateBuffer(ᵐStorage.Instances<{input.ClassFQN}>.AsUnmanaged());");
                        src.Line.Write($"return arr;");
                    }
                }

                src.Linebreak();

                src.Line.Write($"static class InstanceArrayStorage");
                using (src.Braces)
                {
                    src.Line.Write($"internal static AccessArray InstancesArrayInternalStorage");
                    using (src.Indent)
                        src.Line.Write($"= new(ᵐStorage.Instances<{input.ClassFQN}>.AsUnmanaged());");

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
                    src.Line.Write($"public int {x.Name} {{ get; init; }}");

                src.Linebreak();

                src.Line.Write("[global::UnityEngine.RuntimeInitializeOnLoadMethod(global::UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]");
                src.Line.Write("static void InitializeUnmanagedLayout()");
                using (src.Indent)
                    src.Line.Write("=> unmanagedLayoutStorage.Data = new()");

                using (src.Indent)
                using (src.Braces)
                    foreach (var x in input.Fields.AsArray())
                        src.Line.Write($"{x.Name} = ᵐUU.GetFieldOffset(typeof({input.ClassFQN}).GetField(\"{x.MetadataName}\", ᵐBF.{x.Visibility} | ᵐBF.Instance)),");

                src.Write(';');
            }

            src.Linebreak();

            src.Line.Write("public readonly partial struct AccessArray");
            using (src.Braces)
            {
                src.Line.Write($"readonly global::Medicine.Internal.UnmanagedAccessArray<{input.ClassFQN}, Layout, Access, AccessRO> impl;");
                src.Linebreak();

                src.Line.Write($"public AccessArray(global::Unity.Collections.LowLevel.Unsafe.UnsafeList<global::Medicine.UnmanagedRef<{input.ClassFQN}>> classRefArray)");
                using (src.Indent)
                    src.Line.Write("=> impl = new(classRefArray);");

                src.Linebreak();

                src.Line.Write($"public void UpdateBuffer(global::Unity.Collections.LowLevel.Unsafe.UnsafeList<global::Medicine.UnmanagedRef<{input.ClassFQN}>> classRefArray)");
                using (src.Indent)
                    src.Line.Write("=> impl.UpdateBuffer(classRefArray);");

                src.Linebreak();

                src.Line.Write($"public int Length");
                using (src.Indent)
                    src.Line.Write("=> impl.Length;");

                src.Linebreak();

                src.Line.Write("public Access this[int index]");
                using (src.Braces)
                    src.Line.Write($"{Alias.Inline} get => impl[index];");

                src.Linebreak();

                src.Line.Write(Alias.Inline);
                src.Line.Write("public ReadOnly AsReadOnly()");
                using (src.Indent)
                    src.Line.Write($"=> new(impl);");

                src.Linebreak();

                src.Line.Write(Alias.Inline);
                src.Line.Write($"public global::Medicine.Internal.UnmanagedAccessArray<{input.ClassFQN}, Layout, Access, AccessRO>.Enumerator GetEnumerator()");

                src.Linebreak();

                using (src.Indent)
                    src.Line.Write("=> impl.GetEnumerator();");

                src.Linebreak();

                src.Line.Write("public readonly partial struct ReadOnly");
                using (src.Braces)
                {
                    src.Line.Write($"readonly global::Medicine.Internal.UnmanagedAccessArray<{input.ClassFQN}, Layout, Access, AccessRO>.ReadOnly impl;");
                    src.Linebreak();

                    src.Line.Write($"public ReadOnly(global::Medicine.Internal.UnmanagedAccessArray<{input.ClassFQN}, Layout, Access, AccessRO> accessArray)");
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
                    src.Line.Write($"public global::Medicine.Internal.UnmanagedAccessArray<{input.ClassFQN}, Layout, Access, AccessRO>.ReadOnly.Enumerator GetEnumerator()");
                    using (src.Indent)
                        src.Line.Write("=> impl.GetEnumerator();");
                }
            }

            src.Linebreak();

            src.Line.Write("public readonly unsafe partial struct Access");
            using (src.Braces)
            {
                src.Line.Write($"public readonly global::Medicine.UnmanagedRef<{input.ClassFQN}> Ref;");
                src.Line.Write("readonly Layout* layoutInfo;");
                src.Linebreak();

                src.Line.Write("public ref readonly Layout Layout");
                using (src.Indent)
                    src.Line.Write($"=> ref *layoutInfo;");

                src.Linebreak();

                src.Line.Write($"public Access(global::Medicine.UnmanagedRef<{input.ClassFQN}> Ref)");
                using (src.Braces)
                {
                    src.Line.Write("this.Ref = Ref;");
                    src.Line.Write("layoutInfo = (Layout*)unmanagedLayoutStorage.UnsafeDataPointer;");
                }

                src.Linebreak();

                foreach (var x in input.Fields.AsArray())
                {
                    src.Line.Write($"/// <inheritdoc cref=\"{input.ClassFQN}.{x.Name}\" />");
                    src.Line.Write($"public ref{(x.IsReadOnly ? " readonly" : "")} {x.TypeFQN} {x.Name}");
                    using (src.Indent)
                        src.Line.Write($"=> ref Ref.Read<{x.TypeFQN}>(layoutInfo->{x.Name});");

                    src.Linebreak();
                }

                if (input.IsUnityObject)
                {
                    src.Line.Write($"/// <inheritdoc cref=\"global::Medicine.UnmanagedRefExtensions.IsDestroyed\" />");
                    src.Line.Write($"public bool IsDestroyed");
                    using (src.Indent)
                        src.Line.Write($"=> global::Medicine.UnmanagedRefExtensions.IsDestroyed(Ref);");

                    src.Linebreak();

                    src.Line.Write($"/// <inheritdoc cref=\"global::Medicine.UnmanagedRefExtensions.GetInstanceID\" />");
                    src.Line.Write($"public int InstanceID");
                    using (src.Indent)
                        src.Line.Write($"=> global::Medicine.UnmanagedRefExtensions.GetInstanceID(Ref);");

                    src.Linebreak();
                }
            }

            src.Line.Write("public readonly unsafe partial struct AccessRO");
            using (src.Braces)
            {
                src.Line.Write($"public readonly global::Medicine.UnmanagedRef<{input.ClassFQN}> Ref;");
                src.Line.Write("readonly Layout* layoutInfo;");
                src.Linebreak();

                src.Line.Write("public ref readonly Layout Layout");
                using (src.Indent)
                    src.Line.Write($"=> ref *layoutInfo;");

                src.Linebreak();

                src.Line.Write($"public AccessRO(global::Medicine.UnmanagedRef<{input.ClassFQN}> Ref)");
                using (src.Braces)
                {
                    src.Line.Write("this.Ref = Ref;");
                    src.Line.Write("layoutInfo = (Layout*)unmanagedLayoutStorage.UnsafeDataPointer;");
                }

                src.Linebreak();

                foreach (var x in input.Fields.AsArray())
                {
                    src.Line.Write($"/// <inheritdoc cref=\"{input.ClassFQN}.{x.Name}\" />");
                    src.Line.Write($"public ref readonly {x.TypeFQN} {x.Name}");
                    using (src.Indent)
                        src.Line.Write($"=> ref Ref.Read<{x.TypeFQN}>(layoutInfo->{x.Name});");

                    src.Linebreak();
                }

                if (input.IsUnityObject)
                {
                    src.Line.Write($"/// <inheritdoc cref=\"global::Medicine.UnmanagedRefExtensions.IsDestroyed\" />");
                    src.Line.Write($"public bool IsDestroyed");
                    using (src.Indent)
                        src.Line.Write($"=> global::Medicine.UnmanagedRefExtensions.IsDestroyed(Ref);");

                    src.Linebreak();

                    src.Line.Write($"/// <inheritdoc cref=\"global::Medicine.UnmanagedRefExtensions.GetInstanceID\" />");
                    src.Line.Write($"public int InstanceID");
                    using (src.Indent)
                        src.Line.Write($"=> global::Medicine.UnmanagedRefExtensions.GetInstanceID(Ref);");

                    src.Linebreak();
                }
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
                src.Line.Write("/// <summary>");
                src.Line.Write("/// Returns an <see cref=\"Access\"/> struct that can be used to read and write fields of the given class.");
                src.Line.Write($"/// This is less efficient than using a <see cref=\"{input.ClassFQN}.Unmanaged.AccessArray\"/>.");
                src.Line.Write("/// </summary>");
                src.Line.Write(Alias.Inline);
                src.Line.Write($"public static {input.ClassFQN}.Unmanaged.Access GetAccess(this UnmanagedRef<{input.ClassFQN}> classRef)");
                using (src.Indent)
                    src.Line.Write("=> new(classRef);");
            }
        }
    }
}