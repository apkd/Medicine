using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using static Constants;

[Generator]
public sealed class UnmanagedInvokeSourceGenerator : IIncrementalGenerator
{
    static readonly DiagnosticDescriptor MED038 = new(
        id: nameof(MED038),
        title: "Invalid [UnmanagedInvoke] method",
        messageFormat: "{0}",
        category: "Medicine",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    static readonly DiagnosticDescriptor MED039 = new(
        id: nameof(MED039),
        title: "Conflicting [UnmanagedInvoke] helper signature",
        messageFormat: "{0}",
        category: "Medicine",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    enum DiagnosticKind
    {
        InvalidTarget,
        HelperCollision,
    }

    readonly record struct GeneratorDiagnostic(
        DiagnosticKind Kind,
        string Message,
        LocationInfo? Location
    );

    readonly record struct TypeProjection(
        string SourceTypeFQN,
        string ProjectedTypeFQN,
        string ScaffoldTypeFQN,
        bool IsVoid,
        bool IsManagedReference,
        bool IsUnmanagedRef
    );

    readonly record struct ParameterInfo(
        string Name,
        RefKind RefKind,
        string SourceTypeFQN,
        string ProjectedTypeFQN,
        string ScaffoldTypeFQN,
        bool IsManagedReference,
        bool IsUnmanagedRef
    );

    record struct AccessClassInfo : ISourceGeneratorPassData
    {
        public string? SourceGeneratorOutputFilename { get; init; }
        public string? SourceGeneratorError { get; init; }
        public LocationInfo? SourceGeneratorLocation { get; set; }
        public string SourcePath;
        public string TypeName;
        public string TypeFQN;
        public bool IsGenericType;
        public EquatableArray<string> ContainingTypeDeclaration;
        public EquatableArray<string> BaseTypeFQNs;
    }

    readonly record struct InheritedForwarderInfo(
        string BaseTypeName,
        string BaseTypeFQN,
        string HelperName,
        TypeProjection ReturnType,
        EquatableArray<ParameterInfo> Parameters
    );

    record struct GeneratorInput : ISourceGeneratorPassData
    {
        public string? SourceGeneratorOutputFilename { get; init; }
        public string? SourceGeneratorError { get; init; }
        public LocationInfo? SourceGeneratorLocation { get; set; }
        public GeneratorEnvironment GeneratorEnvironment;
        public EquatableArray<string> ContainingTypeDeclaration;
        public EquatableArray<GeneratorDiagnostic> Diagnostics;
        public EquatableArray<ParameterInfo> Parameters;
        public TypeProjection ReturnType;
        public string ContainingTypeFQN;
        public string MethodName;
        public string HelperName;
        public string ScaffoldName;
        public bool IsStatic;
    }

    record struct InheritedForwarderInput : ISourceGeneratorPassData
    {
        public string? SourceGeneratorOutputFilename { get; init; }
        public string? SourceGeneratorError { get; init; }
        public LocationInfo? SourceGeneratorLocation { get; set; }
        public EquatableArray<string> ContainingTypeDeclaration;
        public string TargetTypeFQN;
        public EquatableArray<InheritedForwarderInfo> Forwarders;
    }

    void IIncrementalGenerator.Initialize(IncrementalGeneratorInitializationContext context)
    {
        var generatorEnvironment = context.GetGeneratorEnvironment();

        var inputProvider = context
            .SyntaxProvider
            .ForAttributeWithMetadataNameEx(
                fullyQualifiedMetadataName: UnmanagedInvokeAttributeMetadataName,
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: TransformForCache
            )
            .Combine(generatorEnvironment)
            .SelectEx((x, ct) => Transform(x.Left, x.Right, ct));

        context.RegisterSourceOutputEx(inputProvider, GenerateSource);

        var accessClassProvider = context
            .SyntaxProvider
            .ForAttributeWithMetadataNameEx(
                fullyQualifiedMetadataName: UnmanagedAccessAttributeMetadataName,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (context, ct) => TransformAccessClass(context, ct)
            );

        var inheritedForwarderProvider = inputProvider
            .Collect()
            .Combine(accessClassProvider.Collect())
            .SelectManyEx(static (x, _) => BuildInheritedForwarderInputs(x.Left, x.Right));

        context.RegisterSourceOutputEx(inheritedForwarderProvider, GenerateInheritedForwardersSource);
    }

    static ContextWithCacheGeneratorInput TransformForCache(GeneratorAttributeSyntaxContext context, CancellationToken ct)
        => new()
        {
            Context = context,
            SourceGeneratorOutputFilename = GetOutputFilename(context),
            SourceGeneratorLocation = context.TargetNode.GetLocation(),
            Checksum64ForCache = (context.TargetNode.Parent ?? context.TargetNode).GetNodeChecksum(ct)
                                 ^ context.TargetSymbol.GetDeclarationHierarchyChecksum(ct),
        };

    static GeneratorInput Transform(ContextWithCacheGeneratorInput cacheInput, GeneratorEnvironment generatorEnvironment, CancellationToken ct)
    {
        var context = cacheInput.Context.Value;

        if (context is not { TargetNode: MethodDeclarationSyntax methodDecl, TargetSymbol: IMethodSymbol method })
        {
            return new()
            {
                SourceGeneratorOutputFilename = cacheInput.SourceGeneratorOutputFilename,
                SourceGeneratorLocation = cacheInput.SourceGeneratorLocation,
                SourceGeneratorError = "Unexpected target shape for [UnmanagedInvoke].",
            };
        }

        var knownSymbols = context.SemanticModel.Compilation.GetKnownSymbols();

        using var r1 = Scratch.RentA<List<GeneratorDiagnostic>>(out var diagnostics);
        using var r2 = Scratch.RentA<List<ParameterInfo>>(out var parameters);

        var output = new GeneratorInput
        {
            SourceGeneratorOutputFilename = GetOutputFilename(context),
            SourceGeneratorLocation = new(methodDecl.Identifier.GetLocation()),
            GeneratorEnvironment = generatorEnvironment,
            ContainingTypeDeclaration = DeconstructContainingTypeDeclaration(methodDecl),
            ContainingTypeFQN = method.ContainingType?.FQN ?? "",
            MethodName = method.Name,
            HelperName = $"{method.Name}Unmanaged",
            ScaffoldName = $"{method.Name}UnmanagedCallScaffold_{GetStableSignatureHash(method):X16}",
            IsStatic = method.IsStatic,
        };

        void AddInvalid(string message, Location? location = null)
            => diagnostics.Add(new(
                    DiagnosticKind.InvalidTarget,
                    message,
                    new(location ?? methodDecl.Identifier.GetLocation())
                )
            );

        if (method.ContainingType is not { TypeKind: TypeKind.Class } containingType)
            AddInvalid("[UnmanagedInvoke] can only be used on methods declared in classes.");

        if (method.MethodKind is not MethodKind.Ordinary)
            AddInvalid("[UnmanagedInvoke] can only be used on ordinary methods.");

        if (method.IsGenericMethod || HasGenericContainingType(method.ContainingType))
            AddInvalid("[UnmanagedInvoke] does not support generic methods or methods declared in generic types.");

        if (method.ContainingType?.TypeKind is TypeKind.Interface)
            AddInvalid("[UnmanagedInvoke] does not support interface methods.");

        if (method.IsExtern)
            AddInvalid("[UnmanagedInvoke] does not support extern methods.");

        if (method.IsAsync || methodDecl.Modifiers.Any(SyntaxKind.AsyncKeyword))
            AddInvalid("[UnmanagedInvoke] does not support async methods.");

        if (methodDecl.DescendantNodes().Any(static x => x is YieldStatementSyntax))
            AddInvalid("[UnmanagedInvoke] does not support iterator methods.");

        if (method.ReturnsByRef || method.ReturnsByRefReadonly)
            AddInvalid("[UnmanagedInvoke] does not support ref returns.");

        if (!method.IsStatic && method.ContainingType?.HasAttribute(knownSymbols.UnmanagedAccessAttribute) is not true)
            AddInvalid("Instance [UnmanagedInvoke] methods must be declared in a class marked with [UnmanagedAccess].");

        if (TryGetFirstNonPartialContainingType(methodDecl, out var nonPartialType))
            AddInvalid(
                $"Containing type '{nonPartialType.Identifier.ValueText}' must be partial because it uses [UnmanagedInvoke].",
                GetTypeDeclarationHeaderLocation(nonPartialType)
            );

        if (!TryProjectType(method.ReturnType, knownSymbols, allowVoid: true, out var returnType, out var returnError))
            AddInvalid($"Return type '{method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}' is not supported by [UnmanagedInvoke]: {returnError}");

        output.ReturnType = returnType;

        foreach (var parameter in method.Parameters)
        {
            if (!TryProjectType(parameter.Type, knownSymbols, allowVoid: false, out var projection, out var parameterError))
            {
                AddInvalid(
                    $"Parameter '{parameter.Name}' type '{parameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}' is not supported by [UnmanagedInvoke]: {parameterError}",
                    parameter.Locations.FirstOrDefault()
                );
                continue;
            }

            parameters.Add(new(
                    parameter.Name,
                    parameter.RefKind,
                    projection.SourceTypeFQN,
                    projection.ProjectedTypeFQN,
                    projection.ScaffoldTypeFQN,
                    projection.IsManagedReference,
                    projection.IsUnmanagedRef
                )
            );
        }

        output.Parameters = parameters.ToArray();

        if (diagnostics.Count is 0)
        {
            string helperKey = BuildHelperKey(method.IsStatic, output.HelperName, output.Parameters.AsArray());
            if (HasProjectedHelperCollision(method, knownSymbols, helperKey, ct))
            {
                diagnostics.Add(new(
                        DiagnosticKind.HelperCollision,
                        $"[UnmanagedInvoke] generated helper signature '{output.HelperName}' conflicts with another [UnmanagedInvoke] method after managed reference projection.",
                        new(methodDecl.Identifier.GetLocation())
                    )
                );
            }
        }

        output.Diagnostics = diagnostics.ToArray();
        return output;
    }

    static AccessClassInfo TransformAccessClass(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context is not { TargetNode: ClassDeclarationSyntax typeDecl, TargetSymbol: INamedTypeSymbol typeSymbol })
            return default;

        return new()
        {
            SourcePath = typeDecl.SyntaxTree.FilePath,
            TypeName = typeSymbol.Name,
            TypeFQN = typeSymbol.FQN,
            IsGenericType = typeSymbol.IsGenericType,
            ContainingTypeDeclaration = Utility.DeconstructTypeDeclaration(typeDecl),
            BaseTypeFQNs = typeSymbol.GetBaseTypes().Select(static x => x.FQN).ToArray(),
            SourceGeneratorLocation = new(typeDecl.Identifier.GetLocation()),
        };
    }

    static IEnumerable<InheritedForwarderInput> BuildInheritedForwarderInputs(
        ImmutableArray<GeneratorInput> invokeInputs,
        ImmutableArray<AccessClassInfo> accessClasses
    )
    {
        var validInstanceInvokes = invokeInputs
            .Where(static x => x.Diagnostics.Length is 0 && !x.IsStatic && x.ContainingTypeFQN is { Length: > 0 })
            .ToArray();

        var accessTypes = accessClasses
            .Where(static x => !x.IsGenericType && x.TypeFQN is { Length: > 0 })
            .OrderBy(static x => x.TypeFQN, StringComparer.Ordinal)
            .ToArray();

        foreach (var target in accessTypes)
        {
            using var r1 = Scratch.RentA<List<InheritedForwarderInfo>>(out var forwarders);
            using var r2 = Scratch.RentB<HashSet<string>>(out var localKeys);
            using var r3 = Scratch.RentC<List<(string Key, int Distance, GeneratorInput Invoke)>>(out var inheritedByKey);

            foreach (var invoke in validInstanceInvokes)
            {
                string key = BuildHelperKey(isStatic: false, invoke.HelperName, invoke.Parameters.AsArray());

                if (invoke.ContainingTypeFQN == target.TypeFQN)
                {
                    localKeys.Add(key);
                    continue;
                }

                int distance = Array.IndexOf(target.BaseTypeFQNs.AsArray(), invoke.ContainingTypeFQN);
                if (distance < 0)
                    continue;

                int existingIndex = inheritedByKey.FindIndex(x => x.Key == key);
                if (existingIndex >= 0)
                {
                    if (inheritedByKey[existingIndex].Distance <= distance)
                        continue;

                    inheritedByKey[existingIndex] = (key, distance, invoke);
                    continue;
                }

                inheritedByKey.Add((key, distance, invoke));
            }

            inheritedByKey.Sort(static (left, right) => string.Compare(left.Key, right.Key, StringComparison.Ordinal));

            foreach (var pair in inheritedByKey)
            {
                if (localKeys.Contains(pair.Key))
                    continue;

                var invoke = pair.Invoke;
                forwarders.Add(new(
                        BaseTypeName: GetTypeName(invoke.ContainingTypeFQN),
                        BaseTypeFQN: invoke.ContainingTypeFQN,
                        HelperName: invoke.HelperName,
                        ReturnType: invoke.ReturnType,
                        Parameters: invoke.Parameters
                    )
                );
            }

            if (forwarders.Count is 0)
                continue;

            yield return new()
            {
                SourceGeneratorOutputFilename = Utility.GetOutputFilename(
                    filePath: target.SourcePath,
                    targetNodeName: target.TypeName,
                    additionalNameForHash: $"{target.TypeFQN}|InheritedUnmanagedInvoke",
                    label: $"[{UnmanagedInvokeAttributeNameShort}]",
                    includeFilename: false
                ),
                SourceGeneratorLocation = target.SourceGeneratorLocation,
                ContainingTypeDeclaration = target.ContainingTypeDeclaration,
                TargetTypeFQN = target.TypeFQN,
                Forwarders = forwarders.ToArray(),
            };
        }

        static string GetTypeName(string typeFqn)
        {
            int index = typeFqn.LastIndexOf('.');
            return index < 0 ? typeFqn.Replace("global::", "") : typeFqn[(index + 1)..];
        }
    }

    static void GenerateInheritedForwardersSource(SourceProductionContext context, SourceWriter src, InheritedForwarderInput input)
    {
        if (input.Forwarders.Length is 0)
            return;

        src.Line.Write(Alias.UsingInline);
        src.Line.Write("using Medicine;");
        src.Linebreak();

        foreach (var declaration in input.ContainingTypeDeclaration.AsArray())
        {
            src.Line.Write(declaration);
            src.OpenBrace();
        }

        src.Line.Write("public static partial class Unmanaged");
        using (src.Braces)
        {
            EmitAccessForwarders("AccessRW");
            src.Linebreak();
            EmitAccessForwarders("AccessRO");
        }

        foreach (var _ in input.ContainingTypeDeclaration.AsArray())
            src.CloseBrace();

        void EmitAccessForwarders(string accessStructName)
        {
            src.Line.Write($"public readonly unsafe partial struct {accessStructName}");
            using (src.Braces)
            {
                foreach (var forwarder in input.Forwarders.AsArray())
                {
                    src.Line.Write(Alias.Inline);
                    src.Line.Write($"public {forwarder.ReturnType.ProjectedTypeFQN} {forwarder.HelperName}(");
                    using (src.Indent)
                        EmitForwarderParameters(src, forwarder.Parameters);

                    src.Line.Write(")");
                    using (src.Braces)
                    {
                        string call = $"this.As{forwarder.BaseTypeName.Sanitize()}().{forwarder.HelperName}({BuildForwarderArguments(forwarder.Parameters)})";
                        if (forwarder.ReturnType.IsVoid)
                            src.Line.Write($"{call};");
                        else
                            src.Line.Write($"return {call};");
                    }

                    src.Linebreak();
                }
            }
        }
    }

    static void GenerateSource(SourceProductionContext context, SourceWriter src, GeneratorInput input)
    {
        src.ShouldEmitDocs = input.GeneratorEnvironment.ShouldEmitDocs;

        foreach (var diagnostic in input.Diagnostics.AsArray())
            context.ReportDiagnostic(Diagnostic.Create(
                    descriptor: diagnostic.Kind is DiagnosticKind.HelperCollision ? MED039 : MED038,
                    location: diagnostic.Location?.ToLocation() ?? input.SourceGeneratorLocation?.ToLocation() ?? Location.None,
                    messageArgs: diagnostic.Message
                )
            );

        if (input.Diagnostics.Length > 0)
            return;

        src.Line.Write(Alias.UsingInline);
        src.Linebreak();

        foreach (var declaration in input.ContainingTypeDeclaration.AsArray())
        {
            src.Line.Write(declaration);
            src.OpenBrace();
        }

        EmitScaffold(src, input);

        src.Linebreak();

        if (input.IsStatic)
            EmitStaticHelper(src, input);
        else
            EmitAccessHelpers(src, input);

        foreach (var _ in input.ContainingTypeDeclaration.AsArray())
            src.CloseBrace();
    }

    static void EmitScaffold(SourceWriter src, GeneratorInput input)
    {
        var parameters = input.Parameters.AsArray();

        src.Line.Write("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        src.Line.Write($"static class {input.ScaffoldName}");
        using (src.Braces)
        {
            src.Line.Write("readonly struct SharedStaticKey { }");
            src.Linebreak();

            src.Line.Write($"delegate {input.ReturnType.ScaffoldTypeFQN} UnmanagedDelegate(");
            using (src.Indent)
            {
                if (!input.IsStatic)
                    src.Line.Write($"nint self{(parameters.Length > 0 ? "," : "")}");

                for (int i = 0; i < parameters.Length; i++)
                {
                    var parameter = parameters[i];
                    src.Line.Write($"{GetParameterPrefix(parameter.RefKind)}{parameter.ScaffoldTypeFQN} {parameter.Name}{(i < parameters.Length - 1 ? "," : "")}");
                }
            }

            src.Line.Write(");");
            src.Linebreak();

            src.Line.Write("static readonly global::Unity.Burst.SharedStatic<global::Unity.Burst.FunctionPointer<UnmanagedDelegate>> SharedStaticFunctionPointer");
            using (src.Indent)
                src.Line.Write("= global::Unity.Burst.SharedStatic<global::Unity.Burst.FunctionPointer<UnmanagedDelegate>>.GetOrCreate<SharedStaticKey>();");

            src.Linebreak();

            src.Line.Write("static readonly UnmanagedDelegate ManagedDelegate = Managed;");
            src.Linebreak();

            src.Write("\n#if UNITY_EDITOR");
            src.Line.Write("[global::UnityEditor.InitializeOnLoadMethodAttribute]");
            src.Write("\n#endif");
            src.Line.Write("[global::UnityEngine.RuntimeInitializeOnLoadMethod(global::UnityEngine.RuntimeInitializeLoadType.AfterAssembliesLoaded)]");
            src.Line.Write("static void Initialize()");
            using (src.Indent)
                src.Line.Write("=> SharedStaticFunctionPointer.Data = new global::Unity.Burst.FunctionPointer<UnmanagedDelegate>(global::System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(ManagedDelegate));");

            src.Linebreak();

            src.Line.Write("[global::AOT.MonoPInvokeCallbackAttribute(typeof(UnmanagedDelegate))]");
            src.Line.Write($"static {input.ReturnType.ScaffoldTypeFQN} Managed(");
            using (src.Indent)
            {
                if (!input.IsStatic)
                    src.Line.Write($"nint self{(parameters.Length > 0 ? "," : "")}");

                for (int i = 0; i < parameters.Length; i++)
                {
                    var parameter = parameters[i];
                    src.Line.Write($"{GetParameterPrefix(parameter.RefKind)}{parameter.ScaffoldTypeFQN} {parameter.Name}{(i < parameters.Length - 1 ? "," : "")}");
                }
            }

            src.Line.Write(")");
            using (src.Braces)
            {
                EmitManagedLocals(src, input);
                EmitManagedInvoke(src, input);
            }

            src.Linebreak();

            src.Line.Write($"public static {input.ReturnType.ScaffoldTypeFQN} Invoke(");
            using (src.Indent)
            {
                if (!input.IsStatic)
                    src.Line.Write($"nint self{(parameters.Length > 0 ? "," : "")}");

                for (int i = 0; i < parameters.Length; i++)
                {
                    var parameter = parameters[i];
                    src.Line.Write($"{GetParameterPrefix(parameter.RefKind)}{parameter.ScaffoldTypeFQN} {parameter.Name}{(i < parameters.Length - 1 ? "," : "")}");
                }
            }

            src.Line.Write(")");
            using (src.Indent)
                src.Line.Write($"=> SharedStaticFunctionPointer.Data.Invoke({BuildScaffoldForwardArguments(input, includeSelf: !input.IsStatic)});");
        }
    }

    static void EmitManagedLocals(SourceWriter src, GeneratorInput input)
    {
        foreach (var parameter in input.Parameters.AsArray())
        {
            if (!UsesPointerScaffold(parameter))
                continue;

            switch (parameter.RefKind)
            {
                case RefKind.None:
                {
                    break;
                }
                case RefKind.Out:
                {
                    src.Line.Write($"{GetManagedLocalType(parameter)} {ManagedLocalName(parameter)};");
                    break;
                }
                default:
                {
                    src.Line.Write($"var {ManagedLocalName(parameter)} = {BuildManagedLocalInitializer(parameter)};");
                    break;
                }
            }
        }

        if (input.Parameters.AsArray().Any(static x => UsesPointerScaffold(x) && x.RefKind is not RefKind.None))
            src.Linebreak();
    }

    static void EmitManagedInvoke(SourceWriter src, GeneratorInput input)
    {
        string call = input.IsStatic
            ? $"{input.ContainingTypeFQN}.{input.MethodName}({BuildManagedCallArguments(input)})"
            : $"new global::Medicine.UnmanagedRef<{input.ContainingTypeFQN}>(self).Resolve().{input.MethodName}({BuildManagedCallArguments(input)})";

        if (input.ReturnType.IsVoid)
        {
            src.Line.Write($"{call};");
            EmitManagedCopyBack(src, input);
            return;
        }

        src.Line.Write($"var result = {call};");
        EmitManagedCopyBack(src, input);
        src.Line.Write($"return {BuildScaffoldReturnExpression(input.ReturnType, "result")};");
    }

    static void EmitManagedCopyBack(SourceWriter src, GeneratorInput input)
    {
        foreach (var parameter in input.Parameters.AsArray())
            if (UsesPointerScaffold(parameter))
                if (parameter.RefKind is RefKind.Ref or RefKind.Out)
                    src.Line.Write($"{parameter.Name} = {BuildManagedCopyBackExpression(parameter)};");
    }

    static void EmitStaticHelper(SourceWriter src, GeneratorInput input)
    {
        src.Line.Write(Alias.Inline);
        src.Line.Write($"public static {input.ReturnType.ProjectedTypeFQN} {input.HelperName}(");
        using (src.Indent)
            EmitHelperParameters(src, input);

        src.Line.Write(")");
        using (src.Braces)
            EmitHelperInvokeBody(src, input, selfExpression: null);
    }

    static void EmitAccessHelpers(SourceWriter src, GeneratorInput input)
    {
        src.Line.Write("public static partial class Unmanaged");
        using (src.Braces)
        {
            EmitAccessHelper("AccessRW");
            src.Linebreak();
            EmitAccessHelper("AccessRO");
        }

        void EmitAccessHelper(string accessStructName)
        {
            src.Line.Write($"public readonly unsafe partial struct {accessStructName}");
            using (src.Braces)
            {
                src.Line.Write(Alias.Inline);
                src.Line.Write($"public {input.ReturnType.ProjectedTypeFQN} {input.HelperName}(");
                using (src.Indent)
                    EmitHelperParameters(src, input);

                src.Line.Write(")");
                using (src.Braces)
                    EmitHelperInvokeBody(src, input, selfExpression: "Ref.Ptr");
            }
        }
    }

    static void EmitHelperParameters(SourceWriter src, GeneratorInput input)
    {
        var parameters = input.Parameters.AsArray();
        for (int i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            src.Line.Write($"{GetParameterPrefix(parameter.RefKind)}{parameter.ProjectedTypeFQN} {parameter.Name}{(i < parameters.Length - 1 ? "," : "")}");
        }
    }

    static void EmitForwarderParameters(SourceWriter src, EquatableArray<ParameterInfo> parametersArray)
    {
        var parameters = parametersArray.AsArray();
        for (int i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            src.Line.Write($"{GetParameterPrefix(parameter.RefKind)}{parameter.ProjectedTypeFQN} {parameter.Name}{(i < parameters.Length - 1 ? "," : "")}");
        }
    }

    static string BuildForwarderArguments(EquatableArray<ParameterInfo> parameters)
        => string.Join(
            ", ",
            parameters.AsArray().Select(static x => $"{GetArgumentPrefix(x.RefKind)}{x.Name}")
        );

    static void EmitHelperInvokeBody(SourceWriter src, GeneratorInput input, string? selfExpression)
    {
        foreach (var parameter in input.Parameters.AsArray())
        {
            if (!UsesPointerScaffold(parameter) || parameter.RefKind is RefKind.None)
                continue;

            if (parameter.RefKind is RefKind.Out)
                src.Line.Write($"nint {HelperPointerLocalName(parameter)};");
            else
                src.Line.Write($"var {HelperPointerLocalName(parameter)} = {parameter.Name}.Ptr;");
        }

        if (input.Parameters.AsArray().Any(static x => UsesPointerScaffold(x) && x.RefKind is not RefKind.None))
            src.Linebreak();

        string invoke = $"{input.ScaffoldName}.Invoke({BuildHelperInvokeArguments(input, selfExpression)})";

        if (input.ReturnType.IsVoid)
        {
            src.Line.Write($"{invoke};");
            EmitHelperCopyBack(src, input);
            return;
        }

        bool hasCopyBack = HasHelperCopyBack(input);
        if (hasCopyBack)
            src.Line.Write($"var result = {invoke};");

        EmitHelperCopyBack(src, input);

        if (UsesPointerScaffold(input.ReturnType))
            src.Line.Write(hasCopyBack
                ? $"return new {input.ReturnType.ProjectedTypeFQN}(result);"
                : $"return new {input.ReturnType.ProjectedTypeFQN}({invoke});");
        else
            src.Line.Write(hasCopyBack
                ? "return result;"
                : $"return {invoke};");
    }

    static void EmitHelperCopyBack(SourceWriter src, GeneratorInput input)
    {
        foreach (var parameter in input.Parameters.AsArray())
        {
            if (!UsesPointerScaffold(parameter) || parameter.RefKind is not (RefKind.Ref or RefKind.Out))
                continue;

            src.Line.Write($"{parameter.Name} = new {parameter.ProjectedTypeFQN}({HelperPointerLocalName(parameter)});");
        }
    }

    static string BuildManagedCallArguments(GeneratorInput input)
        => string.Join(", ", input.Parameters.AsArray().Select(BuildManagedCallArgument));

    static string BuildManagedCallArgument(ParameterInfo parameter)
    {
        if (UsesPointerScaffold(parameter))
            return parameter.RefKind switch
            {
                RefKind.None => BuildManagedLocalInitializer(parameter),
                RefKind.Out  => $"out {ManagedLocalName(parameter)}",
                RefKind.Ref  => $"ref {ManagedLocalName(parameter)}",
                RefKind.In   => $"in {ManagedLocalName(parameter)}",
                _            => $"{GetArgumentPrefix(parameter.RefKind)}{ManagedLocalName(parameter)}",
            };

        return $"{GetArgumentPrefix(parameter.RefKind)}{parameter.Name}";
    }

    static string BuildScaffoldForwardArguments(GeneratorInput input, bool includeSelf)
        => BuildJoinedArguments(
            input,
            selfExpression: includeSelf ? "self" : null,
            static parameter => parameter.Name
        );

    static string BuildHelperInvokeArguments(GeneratorInput input, string? selfExpression)
        => BuildJoinedArguments(
            input,
            selfExpression,
            static parameter => UsesPointerScaffold(parameter)
                ? parameter.RefKind is RefKind.None
                    ? $"{parameter.Name}.Ptr"
                    : HelperPointerLocalName(parameter)
                : parameter.Name
        );

    static string BuildJoinedArguments(
        GeneratorInput input,
        string? selfExpression,
        Func<ParameterInfo, string> buildParameterExpression
    )
    {
        var parameters = input.Parameters.AsArray();
        var count = parameters.Length + (selfExpression is null ? 0 : 1);
        var arguments = new string[count];
        var index = 0;

        if (selfExpression is not null)
            arguments[index++] = selfExpression;

        foreach (var parameter in parameters)
            arguments[index++] = $"{GetArgumentPrefix(parameter.RefKind)}{buildParameterExpression(parameter)}";

        return string.Join(", ", arguments);
    }

    static string ManagedLocalName(ParameterInfo parameter)
        => $"{m}{parameter.Name}Managed";

    static string HelperPointerLocalName(ParameterInfo parameter)
        => $"{m}{parameter.Name}Ptr";

    static string GetManagedLocalType(ParameterInfo parameter)
        => parameter.IsManagedReference || parameter.IsUnmanagedRef
            ? parameter.SourceTypeFQN
            : parameter.ProjectedTypeFQN;

    static string BuildManagedLocalInitializer(ParameterInfo parameter)
        => parameter.IsManagedReference
            ? $"new global::Medicine.UnmanagedRef<{parameter.SourceTypeFQN}>({parameter.Name}).Resolve()"
            : $"new {parameter.SourceTypeFQN}({parameter.Name})";

    static string BuildManagedCopyBackExpression(ParameterInfo parameter)
        => parameter.IsManagedReference
            ? $"new global::Medicine.UnmanagedRef<{parameter.SourceTypeFQN}>({ManagedLocalName(parameter)}).Ptr"
            : $"{ManagedLocalName(parameter)}.Ptr";

    static string BuildScaffoldReturnExpression(TypeProjection returnType, string valueExpression)
    {
        if (returnType.IsManagedReference)
            return $"new global::Medicine.UnmanagedRef<{returnType.SourceTypeFQN}>({valueExpression}).Ptr";

        if (returnType.IsUnmanagedRef)
            return $"{valueExpression}.Ptr";

        return valueExpression;
    }

    static bool HasHelperCopyBack(GeneratorInput input)
        => input.Parameters.AsArray().Any(static x => UsesPointerScaffold(x) && x.RefKind is RefKind.Ref or RefKind.Out);

    static bool UsesPointerScaffold(ParameterInfo parameter)
        => parameter.IsManagedReference || parameter.IsUnmanagedRef;

    static bool UsesPointerScaffold(TypeProjection projection)
        => projection.IsManagedReference || projection.IsUnmanagedRef;

    static string GetParameterPrefix(RefKind refKind)
        => refKind switch
        {
            RefKind.In  => "in ",
            RefKind.Ref => "ref ",
            RefKind.Out => "out ",
            _           => "",
        };

    static string GetArgumentPrefix(RefKind refKind)
        => refKind switch
        {
            RefKind.Ref => "ref ",
            RefKind.Out => "out ",
            RefKind.In  => "in ",
            _           => "",
        };

    static bool TryProjectType(
        ITypeSymbol type,
        KnownSymbols knownSymbols,
        bool allowVoid,
        out TypeProjection projection,
        out string error
    )
    {
        if (allowVoid && type.SpecialType is SpecialType.System_Void)
        {
            projection = new("void", "void", "void", IsVoid: true, IsManagedReference: false, IsUnmanagedRef: false);
            error = "";
            return true;
        }

        if (type is IErrorTypeSymbol)
        {
            projection = default;
            error = "the type could not be resolved.";
            return false;
        }

        if (type is INamedTypeSymbol namedType && namedType.OriginalDefinition.Is(knownSymbols.UnmanagedRef1))
        {
            projection = new(type.FQN, type.FQN, "nint", IsVoid: false, IsManagedReference: false, IsUnmanagedRef: true);
            error = "";
            return true;
        }

        if (type.IsReferenceType)
        {
            string typeFQN = type.FQN;
            projection = new(typeFQN, $"global::Medicine.UnmanagedRef<{typeFQN}>", "nint", IsVoid: false, IsManagedReference: true, IsUnmanagedRef: false);
            error = "";
            return true;
        }

        if (type.IsUnmanagedType)
        {
            projection = new(type.FQN, type.FQN, type.FQN, IsVoid: false, IsManagedReference: false, IsUnmanagedRef: false);
            error = "";
            return true;
        }

        projection = default;
        error = "only unmanaged value types and managed reference types are supported.";
        return false;
    }

    static bool HasProjectedHelperCollision(
        IMethodSymbol method,
        KnownSymbols knownSymbols,
        string helperKey,
        CancellationToken ct
    )
    {
        int matches = 0;
        foreach (var member in method.ContainingType.GetMembers(method.Name))
        {
            if (member is not IMethodSymbol other ||
                other.IsStatic != method.IsStatic ||
                !other.HasAttribute(knownSymbols.UnmanagedInvokeAttribute))
                continue;

            if (!TryBuildHelperKey(other, knownSymbols, ct, out var otherKey))
                continue;

            if (otherKey != helperKey)
                continue;

            matches++;
            if (matches > 1)
                return true;
        }

        return false;
    }

    static bool TryBuildHelperKey(
        IMethodSymbol method,
        KnownSymbols knownSymbols,
        CancellationToken ct,
        out string key
    )
    {
        key = "";

        if (method.IsGenericMethod ||
            HasGenericContainingType(method.ContainingType) ||
            method.ReturnsByRef ||
            method.ReturnsByRefReadonly ||
            method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(ct) is MethodDeclarationSyntax methodDecl &&
            methodDecl.DescendantNodes().Any(static x => x is YieldStatementSyntax))
            return false;

        using var r1 = Scratch.RentB<List<ParameterInfo>>(out var parameters);
        foreach (var parameter in method.Parameters)
        {
            if (!TryProjectType(parameter.Type, knownSymbols, allowVoid: false, out var projection, out _))
                return false;

            parameters.Add(new(
                    parameter.Name,
                    parameter.RefKind,
                    projection.SourceTypeFQN,
                    projection.ProjectedTypeFQN,
                    projection.ScaffoldTypeFQN,
                    projection.IsManagedReference,
                    projection.IsUnmanagedRef
                )
            );
        }

        key = BuildHelperKey(method.IsStatic, $"{method.Name}Unmanaged", parameters);
        return true;
    }

    static string BuildHelperKey(bool isStatic, string helperName, IEnumerable<ParameterInfo> parameters)
        => $"{(isStatic ? "static" : "instance")}:{helperName}({string.Join(",", parameters.Select(static x => $"{x.RefKind}:{x.ProjectedTypeFQN}"))})";

    static bool HasGenericContainingType(INamedTypeSymbol? typeSymbol)
    {
        for (var current = typeSymbol; current is not null; current = current.ContainingType)
            if (current.TypeParameters.Length > 0)
                return true;

        return false;
    }

    static bool TryGetFirstNonPartialContainingType(MethodDeclarationSyntax methodDecl, out TypeDeclarationSyntax typeDeclaration)
    {
        for (SyntaxNode? current = methodDecl.Parent; current is not null; current = current.Parent)
        {
            if (current is not TypeDeclarationSyntax declaration)
                continue;

            if (declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                continue;

            typeDeclaration = declaration;
            return true;
        }

        typeDeclaration = null!;
        return false;
    }

    static Location GetTypeDeclarationHeaderLocation(TypeDeclarationSyntax declaration)
    {
        var start = declaration.Modifiers.Count > 0
            ? declaration.Modifiers[0].SpanStart
            : declaration.Keyword.SpanStart;

        int end = declaration.Identifier.Span.End;
        if (declaration.TypeParameterList is not null)
            end = declaration.TypeParameterList.Span.End;

        return Location.Create(declaration.SyntaxTree, TextSpan.FromBounds(start, end));
    }

    static EquatableArray<string> DeconstructContainingTypeDeclaration(MethodDeclarationSyntax methodDecl)
    {
        IEnumerable<string> Walk(MemberDeclarationSyntax? syntax)
        {
            bool NavigateToParent()
                => (syntax = syntax?.Parent as MemberDeclarationSyntax)?.Kind()
                    is SyntaxKind.NamespaceDeclaration
                    or SyntaxKind.FileScopedNamespaceDeclaration
                    or SyntaxKind.ClassDeclaration
                    or SyntaxKind.StructDeclaration
                    or SyntaxKind.RecordDeclaration;

            do
            {
                string? line = syntax switch
                {
                    BaseNamespaceDeclarationSyntax x => $"namespace {x.Name}",
                    TypeDeclarationSyntax x          => $"{GetStaticModifier(x)}partial {x.Keyword.ValueText} {x.Identifier}{x.TypeParameterList}",
                    _                                => null,
                };

                if (line is not null)
                    yield return line;
            } while (NavigateToParent());
        }

        var result = Walk(methodDecl).ToArray();
        Array.Reverse(result);
        return result;

        static string GetStaticModifier(TypeDeclarationSyntax declaration)
            => declaration.Modifiers.Any(SyntaxKind.StaticKeyword)
                ? "static "
                : "";
    }

    static ulong GetStableSignatureHash(IMethodSymbol method)
        => GetStableHash(BuildSourceSignatureKey(method));

    static string BuildSourceSignatureKey(IMethodSymbol method)
    {
        using var r1 = Scratch.RentC<List<string>>(out var parts);
        parts.Add(method.ContainingType?.FQN ?? "");
        parts.Add(method.IsStatic ? "static" : "instance");
        parts.Add(method.Name);
        parts.Add(method.ReturnType.FQN);

        foreach (var parameter in method.Parameters)
        {
            parts.Add(parameter.RefKind.ToString());
            parts.Add(parameter.Type.FQN);
        }

        return string.Join("|", parts);
    }

    static ulong GetStableHash(string value)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        ulong hash = offsetBasis;
        foreach (char c in value)
            hash = unchecked((hash ^ c) * prime);

        return hash;
    }

    static string? GetOutputFilename(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetNode is not MethodDeclarationSyntax
            {
                Identifier.ValueText: { Length: > 0 } name,
                SyntaxTree.FilePath: { Length: > 0 } filePath,
            })
            return null;

        string signature = context.TargetSymbol is IMethodSymbol method
            ? BuildSourceSignatureKey(method)
            : context.TargetNode.GetText().ToString();

        return Utility.GetOutputFilename(
            filePath: filePath,
            targetNodeName: name,
            additionalNameForHash: signature,
            label: $"[{UnmanagedInvokeAttributeNameShort}]",
            includeFilename: false
        );
    }
}
