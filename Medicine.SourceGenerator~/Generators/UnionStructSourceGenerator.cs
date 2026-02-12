using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static System.StringComparison;
using static Constants;

[Generator]
public sealed class UnionStructSourceGenerator : IIncrementalGenerator
{
    record struct InterfaceMemberInput
    {
        public string Name;
        public string ReturnTypeFQN;
        public EquatableArray<string> Parameters;
        public EquatableArray<byte> ParameterRefKinds;
        public bool IsProperty;
    }

    record struct DerivedInput
    {
        public string Name;
        public string FQN;
        public EquatableArray<string> Declaration;
        public byte AssignedId;
        public EquatableArray<string> PubliclyImplementedMembers;
        public bool HasParameterlessConstructor;
    }

    record struct GeneratorInput : IGeneratorTransformOutput
    {
        public string? SourceGeneratorOutputFilename { get; init; }
        public string? SourceGeneratorError { get; init; }
        public LocationInfo? SourceGeneratorErrorLocation { get; set; }

        public LanguageVersion LangVersion { get; init; }
        public EquatableArray<string> BaseDeclaration;
        public string BaseTypeName;
        public string BaseTypeFQN;
        public string InterfaceName;
        public string InterfaceFQN;
        public string TypeIDEnumFQN;
        public string TypeIDFieldName;
        public string RootTypeName;
        public string RootTypeFQN;
        public string RootInterfaceFQN;
        public string RootTypeIDEnumFQN;
        public bool IsRootTypeIDOwner;
        public bool HasParentHeader;
        public string ParentTypeName;
        public string ParentTypeFQN;
        public bool IsPublic;

        public EquatableIgnore<Func<InterfaceMemberInput[]>?> InterfaceMembersBuilderFunc;
        public EquatableIgnore<Func<DerivedInput[]>?> DerivedStructsBuilderFunc;

        // ReSharper disable once NotAccessedField.Local
        public EquatableArray<byte> BaseTextCheckSumForCache;
    }

    record struct DerivedDeferredInput
    {
        public EquatableArray<string> PublicMembers;
        public bool HasParameterlessConstructor;
    }

    record struct Derived : IGeneratorTransformOutput
    {
        public string? SourceGeneratorOutputFilename { get; init; }
        public string? SourceGeneratorError { get; init; }
        public LocationInfo? SourceGeneratorErrorLocation { get; set; }

        public string DerivedFQN { get; init; }
        public string DerivedName { get; init; }
        public EquatableArray<string> Declaration { get; init; }
        public EquatableArray<string> HeaderChainFQNs { get; init; }
        public byte? ForcedId { get; init; }

        public EquatableIgnore<Func<string, bool>?> ImplementsUnionInterfaceFunc;
        public EquatableIgnore<Func<DerivedDeferredInput>?> DeferredInputBuilderFunc;

        // ReSharper disable once NotAccessedField.Local
        public EquatableArray<byte> DerivedTextCheckSumForCache;
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var languageVersion = context.CompilationProvider
            .Select((c, _) => c is CSharpCompilation comp
                ? comp.LanguageVersion
                : LanguageVersion.LatestMajor
            );

        var baseStructs = context.SyntaxProvider
            .ForAttributeWithMetadataNameEx(
                fullyQualifiedMetadataName: UnionHeaderStructAttributeMetadataName,
                predicate: (node, _) => node is StructDeclarationSyntax,
                transform: TransformBase
            );

        var candidateStructs = context.SyntaxProvider
            .ForAttributeWithMetadataNameEx(
                fullyQualifiedMetadataName: UnionStructAttributeMetadataName,
                predicate: (node, _) => node is StructDeclarationSyntax { BaseList.Types.Count: > 0 },
                transform: TransformDerivedCandidate
            )
            .Collect();

        context.RegisterSourceOutputEx(
            source: baseStructs
                .Combine(candidateStructs)
                .SelectEx(CombineBaseAndDerived)
                .Combine(languageVersion)
                .Select((x, ct) => x.Left with { LangVersion = x.Right }),
            action: GenerateSource
        );
    }

    static GeneratorInput TransformBase(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context is not { TargetSymbol: ITypeSymbol symbol, TargetNode: StructDeclarationSyntax structDecl })
            return new()
            {
                SourceGeneratorError = "Unexpected target shape for [UnionHeader].",
                SourceGeneratorErrorLocation = new LocationInfo(context.TargetNode.GetLocation()),
            };

        if (symbol is not INamedTypeSymbol baseSymbol)
            return new()
            {
                SourceGeneratorError = "Unexpected target symbol for [UnionHeader].",
                SourceGeneratorErrorLocation = new LocationInfo(context.TargetNode.GetLocation()),
            };

        var symbolMembers = symbol.GetMembers().AsArray();
        var symbolTypeMembers = symbol.GetTypeMembers().AsArray();

        var interfaceSymbol = symbolTypeMembers.FirstOrDefault(x => x.Name is "Interface")
                              ?? symbolTypeMembers.FirstOrDefault(x => x.TypeKind is TypeKind.Interface);

        if (interfaceSymbol is null)
            return new()
            {
                SourceGeneratorOutputFilename = Utility.GetOutputFilename(structDecl.SyntaxTree.FilePath, symbol.Name, "Union"),
            };

        var typeIDEnumSymbol = symbolTypeMembers.FirstOrDefault(x => x.Name is "TypeIDs");
        var typeIDField = symbolMembers.FirstOrDefault(x => x is IFieldSymbol { Name: "TypeID", Type.Name: "TypeIDs" });
        var parentHeader = GetFirstHeaderFieldType(baseSymbol);

        var parentInterface = parentHeader is not null
            ? GetUnionInterface(parentHeader)
            : null;

        bool hasNestedParent
            = parentHeader is not null &&
              parentInterface is not null &&
              interfaceSymbol.AllInterfaces.Any(x => x.Is(parentInterface));

        bool isRootTypeIdOwner = !hasNestedParent || typeIDField is not null;

        var rootHeader = isRootTypeIdOwner
            ? baseSymbol
            : GetRootHeader(baseSymbol, interfaceSymbol);

        var rootInterface = isRootTypeIdOwner
            ? interfaceSymbol
            : GetUnionInterface(rootHeader) ?? interfaceSymbol;

        var rootTypeIDEnumSymbol = rootHeader.GetTypeMembers().FirstOrDefault(x => x.Name is "TypeIDs");

        return new()
        {
            SourceGeneratorOutputFilename = Utility.GetOutputFilename(structDecl.SyntaxTree.FilePath, symbol.Name, "Union"),
            BaseDeclaration = Utility.DeconstructTypeDeclaration(structDecl, context.SemanticModel, ct),
            BaseTypeName = symbol.Name,
            BaseTypeFQN = symbol.FQN,
            InterfaceName = interfaceSymbol.Name,
            InterfaceFQN = interfaceSymbol.FQN,
            TypeIDEnumFQN = isRootTypeIdOwner
                ? typeIDEnumSymbol?.FQN ?? $"{symbol.FQN}.TypeIDs"
                : rootTypeIDEnumSymbol?.FQN ?? $"{rootHeader.FQN}.TypeIDs",
            TypeIDFieldName = typeIDField?.Name ?? "TypeID",
            RootTypeName = rootHeader.Name,
            RootTypeFQN = rootHeader.FQN,
            RootInterfaceFQN = rootInterface.FQN,
            RootTypeIDEnumFQN = rootTypeIDEnumSymbol?.FQN ?? $"{rootHeader.FQN}.TypeIDs",
            IsRootTypeIDOwner = isRootTypeIdOwner,
            HasParentHeader = hasNestedParent,
            ParentTypeName = hasNestedParent ? parentHeader!.Name : "",
            ParentTypeFQN = hasNestedParent ? parentHeader!.FQN : "",
            IsPublic = structDecl.Modifiers.Any(SyntaxKind.PublicKeyword),
            InterfaceMembersBuilderFunc = new(() => BuildInterfaceMembers(interfaceSymbol)),
            BaseTextCheckSumForCache = structDecl.GetText().GetChecksum().AsArray(),
        };
    }

    static InterfaceMemberInput[] BuildInterfaceMembers(INamedTypeSymbol interfaceSymbol)
    {
        var members = new List<InterfaceMemberInput>();

        foreach (var member in interfaceSymbol.GetMembers())
        {
            if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary } method)
            {
                int parameterCount = method.Parameters.Length;
                string[] parameters = new string[parameterCount];
                byte[] parameterRefKinds = new byte[parameterCount];

                for (int i = 0; i < parameterCount; i++)
                {
                    var parameter = method.Parameters[i];
                    parameters[i] = $"{parameter.Type.FQN} {parameter.Name}";
                    parameterRefKinds[i] = (byte)parameter.RefKind;
                }

                members.Add(
                    new()
                    {
                        Name = method.Name,
                        ReturnTypeFQN = method.ReturnType.FQN,
                        Parameters = parameters,
                        ParameterRefKinds = parameterRefKinds,
                        IsProperty = false,
                    }
                );
            }
            else if (member is IPropertySymbol property)
            {
                members.Add(
                    new()
                    {
                        Name = property.Name,
                        ReturnTypeFQN = property.Type.FQN,
                        IsProperty = true,
                    }
                );
            }
        }

        return members.ToArray();
    }

    static Derived TransformDerivedCandidate(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetNode is not StructDeclarationSyntax structDecl)
            return default;

        if (context.SemanticModel.GetDeclaredSymbol(structDecl, ct) is not INamedTypeSymbol symbol)
            return default;

        byte? forcedId = context.Attributes
            .First()
            .GetAttributeConstructorArguments(ct)
            .Get<byte>("id", null);

        if (forcedId == 0)
            forcedId = null;

        return new()
        {
            DerivedFQN = symbol.FQN,
            DerivedName = symbol.Name,
            Declaration = Utility.DeconstructTypeDeclaration(structDecl, context.SemanticModel, ct),
            HeaderChainFQNs = BuildHeaderChainFQNs(symbol),
            ForcedId = forcedId,
            ImplementsUnionInterfaceFunc = new(interfaceFQN => symbol.AllInterfaces.Any(x => x.FQN == interfaceFQN)),
            DeferredInputBuilderFunc = new(() => BuildDerivedDeferredInput(symbol)),
            DerivedTextCheckSumForCache = structDecl.GetText().GetChecksum().AsArray(),
        };
    }

    static DerivedDeferredInput BuildDerivedDeferredInput(INamedTypeSymbol symbol)
    {
        var publicMembers = symbol.GetMembers()
            .Where(x => x is { DeclaredAccessibility: Accessibility.Public } and not IMethodSymbol { MethodKind: not MethodKind.Ordinary })
            .Select(x => x.Name)
            .Distinct()
            .ToArray();

        return new()
        {
            PublicMembers = publicMembers,
            HasParameterlessConstructor = symbol.InstanceConstructors.Any(x => x is { Parameters.Length: 0, IsImplicitlyDeclared: false }),
        };
    }

    static GeneratorInput CombineBaseAndDerived((GeneratorInput Base, ImmutableArray<Derived> Candidates) input, CancellationToken ct)
    {
        var result = input.Base;
        var candidates = input.Candidates.AsArray();

        var firstError = result.GetError();
        if (firstError is null)
        {
            foreach (var candidate in candidates)
            {
                firstError = candidate.GetError();
                if (firstError is not null)
                    break;
            }
        }

        return result with
        {
            DerivedStructsBuilderFunc = new(()
                => BuildDerivedStructs(candidates, result.InterfaceFQN, result.RootInterfaceFQN, result.BaseTypeFQN, result.RootTypeFQN)),
            SourceGeneratorError = firstError?.error,
            SourceGeneratorErrorLocation = firstError?.location,
        };
    }

    readonly record struct CandidateSortItem(int CandidateIndex, string CandidateName, bool HasForcedId);

    readonly record struct AssignedCandidate(int CandidateIndex, byte AssignedId);

    static DerivedInput[] BuildDerivedStructs(Derived[] candidates, string interfaceFQN, string rootInterfaceFQN, string headerFQN, string rootHeaderFQN)
    {
        var rootCandidates = new List<CandidateSortItem>(candidates.Length);
        var usedIds = new HashSet<byte>();

        for (int i = 0; i < candidates.Length; i++)
        {
            var candidate = candidates[i];
            if (candidate.ImplementsUnionInterfaceFunc.Value?.Invoke(rootInterfaceFQN) is not true)
                continue;

            if (!HasHeaderInChain(candidate, rootHeaderFQN))
                continue;

            if (candidate.ForcedId is { } forcedId)
            {
                rootCandidates.Add(new(i, candidate.DerivedName, true));
                usedIds.Add(forcedId);
            }
            else
            {
                rootCandidates.Add(new(i, candidate.DerivedName, false));
            }
        }

        if (rootCandidates.Count == 0)
            return [];

        rootCandidates.Sort(static (left, right) =>
            {
                if (left.HasForcedId != right.HasForcedId)
                    return left.HasForcedId ? -1 : 1;

                if (left.CandidateName.CompareTo(right.CandidateName, Ordinal) is var nameCompare and not 0)
                    return nameCompare;

                return left.CandidateIndex.CompareTo(right.CandidateIndex);
            }
        );

        var assignedRootCandidates = new List<AssignedCandidate>(rootCandidates.Count);
        byte nextId = 1;
        foreach (var rootCandidate in rootCandidates)
        {
            var candidate = candidates[rootCandidate.CandidateIndex];
            assignedRootCandidates.Add(
                new(
                    rootCandidate.CandidateIndex,
                    candidate.ForcedId ?? GetNextAvailableId(usedIds, ref nextId)
                )
            );
        }

        var derivedStructs = new List<DerivedInput>(assignedRootCandidates.Count);
        foreach (var assigned in assignedRootCandidates)
        {
            var candidate = candidates[assigned.CandidateIndex];
            if (candidate.ImplementsUnionInterfaceFunc.Value?.Invoke(interfaceFQN) is not true)
                continue;

            if (!HasHeaderInChain(candidate, headerFQN))
                continue;

            var deferredInput = candidate.DeferredInputBuilderFunc.Value?.Invoke() ?? default;

            derivedStructs.Add(
                new()
                {
                    Name = candidate.DerivedName,
                    FQN = candidate.DerivedFQN,
                    Declaration = candidate.Declaration,
                    AssignedId = assigned.AssignedId,
                    PubliclyImplementedMembers = deferredInput.PublicMembers,
                    HasParameterlessConstructor = deferredInput.HasParameterlessConstructor,
                }
            );
        }

        var result = derivedStructs.ToArray();
        Array.Sort(
            result,
            static (left, right) =>
            {
                var byId = left.AssignedId.CompareTo(right.AssignedId);
                if (byId != 0)
                    return byId;

                return left.Name.CompareTo(right.Name, Ordinal);
            }
        );

        return result;
    }

    static bool HasHeaderInChain(Derived candidate, string headerFQN)
        => candidate.HeaderChainFQNs.AsArray().Any(x => x.Equals(headerFQN, Ordinal));

    static byte GetNextAvailableId(HashSet<byte> usedIds, ref byte nextId)
    {
        while (usedIds.Contains(nextId))
            nextId++;

        return nextId++;
    }

    static EquatableArray<string> BuildHeaderChainFQNs(INamedTypeSymbol symbol)
    {
        var firstHeaderFieldType = GetFirstHeaderFieldType(symbol);
        if (firstHeaderFieldType is null)
            return [];

        var chain = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = firstHeaderFieldType;
        while (current is not null && current.HasAttribute(UnionHeaderStructAttributeFQN))
        {
            if (!visited.Add(current.FQN))
                break;

            chain.Add(current.FQN);
            current = GetFirstHeaderFieldType(current);
        }

        return chain.ToArray();
    }

    static INamedTypeSymbol? GetFirstHeaderFieldType(INamedTypeSymbol symbol)
    {
        var firstField = symbol.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(x => !x.IsStatic)
            .OrderBy(x => x.Locations.FirstOrDefault()?.SourceSpan.Start ?? int.MaxValue)
            .FirstOrDefault();

        return firstField?.Type is INamedTypeSymbol headerType && headerType.HasAttribute(UnionHeaderStructAttributeFQN)
            ? headerType
            : null;
    }

    static INamedTypeSymbol? GetUnionInterface(INamedTypeSymbol headerType)
    {
        var typeMembers = headerType.GetTypeMembers().AsArray();
        return typeMembers.FirstOrDefault(x =>
                   x is
                   {
                       Name: "Interface",
                       TypeKind: TypeKind.Interface,
                       DeclaredAccessibility: Accessibility.Public,
                   }
               ) ??
               typeMembers.FirstOrDefault(x => x is { TypeKind: TypeKind.Interface, DeclaredAccessibility: Accessibility.Public });
    }

    static INamedTypeSymbol GetRootHeader(INamedTypeSymbol headerSymbol, INamedTypeSymbol headerInterface)
    {
        var currentHeader = headerSymbol;
        var currentInterface = headerInterface;
        var visited = new HashSet<string>(StringComparer.Ordinal);

        while (visited.Add(currentHeader.FQN))
        {
            var parentHeader = GetFirstHeaderFieldType(currentHeader);
            if (parentHeader is null)
                break;

            var parentInterface = GetUnionInterface(parentHeader);
            if (parentInterface is null)
                break;

            if (!currentInterface.AllInterfaces.Any(x => x.Is(parentInterface)))
                break;

            currentHeader = parentHeader;
            currentInterface = parentInterface;
        }

        return currentHeader;
    }

    static void GenerateSource(SourceProductionContext context, SourceWriter src, GeneratorInput input)
    {
        var derivedStructs = input.DerivedStructsBuilderFunc.Value?.Invoke() ?? [];
        var interfaceMembers = input.InterfaceMembersBuilderFunc.Value?.Invoke() ?? [];

        src.Line.Write(Alias.UsingInline);
        src.Line.Write($"using {m}UnsafeUtility = global::Unity.Collections.LowLevel.Unsafe.UnsafeUtility;");
        src.Line.Write($"using {m}BurstDiscard = global::Unity.Burst.BurstDiscardAttribute;");
        src.Linebreak();

        // 1. derived structs generated members
        if (input.IsRootTypeIDOwner)
        {
            foreach (var derived in derivedStructs)
            {
                foreach (var x in derived.Declaration.AsSpan())
                {
                    src.Line.Write(x);
                    src.Line.Write('{');
                    src.IncreaseIndent();
                }

                src.Line.Write($"public const {input.TypeIDEnumFQN} TypeID = {input.TypeIDEnumFQN}.{derived.Name};");
                src.Linebreak();

                if (input.LangVersion >= LanguageVersion.CSharp10 && !derived.HasParameterlessConstructor)
                {
                    src.Line.Write($"public {derived.Name}()");
                    using (src.Braces)
                    {
                        src.Line.Write("this = default;");
                        src.Line.Write($"{m}UnsafeUtility.As<{derived.FQN}, {input.BaseTypeFQN}>(ref this).{input.TypeIDFieldName} = TypeID;");
                    }
                }

                src.TrimEndWhitespace();

                foreach (var _ in derived.Declaration.AsSpan())
                {
                    src.DecreaseIndent();
                    src.Line.Write('}');
                }

                src.Linebreak();
            }
        }

        // 2. base struct generated members
        {
            foreach (var x in input.BaseDeclaration.AsSpan())
            {
                src.Line.Write(x);
                src.Line.Write('{');
                src.IncreaseIndent();
            }

            if (input.IsRootTypeIDOwner)
            {
                src.Line.Write("public enum TypeIDs : byte");
                using (src.Braces)
                {
                    src.Line.Write("Unset = 0,");
                    foreach (var derived in derivedStructs)
                        src.Line.Write($"{derived.Name} = {derived.AssignedId},");
                }

                src.Linebreak();

                src.Line.Write("static readonly int[] derivedStructSizes =");
                using (src.Braces)
                {
                    byte maxId = derivedStructs.Length > 0
                        ? derivedStructs[^1].AssignedId
                        : (byte)0;

                    var sizes = new string[maxId + 1];
                    for (int i = 0; i < sizes.Length; i++)
                        sizes[i] = "-1";

                    foreach (var derived in derivedStructs)
                        sizes[derived.AssignedId] = $"{m}UnsafeUtility.SizeOf<{derived.FQN}>()";

                    for (int i = 0; i < sizes.Length; i++)
                        src.Line.Write($"{sizes[i]},");
                }

                src.Write(';').Linebreak();

                src.Line.Write("static readonly string[] derivedStructNames =");
                using (src.Braces)
                {
                    byte maxId = derivedStructs.Length > 0
                        ? derivedStructs[^1].AssignedId
                        : (byte)0;

                    var names = new string[maxId + 1];
                    for (int i = 0; i < names.Length; i++)
                        names[i] = $"\"Undefined (TypeID={i})\"";

                    foreach (var derived in derivedStructs)
                        names[derived.AssignedId] = $"\"{derived.Name}\"";

                    for (int i = 0; i < names.Length; i++)
                        src.Line.Write($"{names[i]},");
                }

                src.Write(';').Linebreak();

                src.Line.Write("public int SizeInBytes");
                using (src.Indent)
                {
                    if (derivedStructs.Length > 0)
                    {
                        src.Line.Write("=> TypeID switch");
                        using (src.Braces)
                        {
                            src.Line.Write($"<= TypeIDs.{derivedStructs[^1].Name} => derivedStructSizes[(int)TypeID],");
                            src.Line.Write("_ => -1,");
                        }
                    }
                    else
                    {
                        src.Line.Write("=> -1");
                    }
                }

                src.Write(';').Linebreak();

                src.Line.Write("public string TypeName");
                using (src.Indent)
                {
                    src.Line.Write("=> TypeID switch");
                    using (src.Braces)
                    {
                        if (derivedStructs.Length > 0)
                            src.Line.Write($"<= TypeIDs.{derivedStructs[^1].Name} => derivedStructNames[(int)TypeID],");
                        else
                            src.Line.Write("TypeIDs.Unset => \"Undefined (TypeID=0)\",");

                        src.Line.Write("var unknown => $\"Unknown (TypeID={(byte)unknown})\",");
                    }
                }

                src.Write(';').Linebreak();
            }
            else
            {
                src.Line.Write($"public {input.TypeIDEnumFQN} TypeID");
                using (src.Indent)
                    src.Line.Write($"=> {m}UnsafeUtility.As<{input.BaseTypeFQN}, {input.ParentTypeFQN}>(ref this).TypeID");

                src.Write(';').Linebreak();

                src.Line.Write("public int SizeInBytes");
                using (src.Indent)
                    src.Line.Write($"=> {m}UnsafeUtility.As<{input.BaseTypeFQN}, {input.ParentTypeFQN}>(ref this).SizeInBytes");

                src.Write(';').Linebreak();

                src.Line.Write("public string TypeName");
                using (src.Indent)
                    src.Line.Write($"=> {m}UnsafeUtility.As<{input.BaseTypeFQN}, {input.ParentTypeFQN}>(ref this).TypeName");

                src.Write(';').Linebreak();
            }

            foreach (var _ in input.BaseDeclaration.AsSpan())
            {
                src.DecreaseIndent();
                src.Line.Write('}');
            }

            src.Linebreak();
        }

        // 3. extensions class
        bool emitWithAssignHelper = false;
        string @public = input.IsPublic ? "public " : "";
        src.Line.Write($"{@public}static partial class {input.BaseTypeName}Extensions");
        using (src.Braces)
        {
            // polymorphic methods/properties
            foreach (var member in interfaceMembers)
            {
                var parametersEnumerable
                    = member
                        .Parameters
                        .AsArray()
                        .Zip(member.ParameterRefKinds.AsArray().Cast<RefKind>(), (call, refKind) => (call, refKind))
                        .Select(x => x.refKind.AsRefString() + x.call);

                string parameters = !member.IsProperty
                    ? parametersEnumerable.Join(", ")
                    : "";

                var callParametersEnumerable
                    = member
                        .Parameters
                        .AsArray()
                        .Zip(member.ParameterRefKinds.AsArray().Cast<RefKind>(), (call, refKind) => (call, refKind))
                        .Select(x => x.refKind.AsRefString() + GetParameterName(x.call));

                string callParameters = !member.IsProperty
                    ? callParametersEnumerable.Join(", ")
                    : "";

                string callParametersWithInvoke = !member.IsProperty
                    ? $"({callParameters})"
                    : "";

                string methodParameters = member is { IsProperty: false, Parameters.Length: > 0 }
                    ? $", {parameters}"
                    : "";

                bool emitGenericInvokeHelper = false;
                string genericInvokeName = $"{member.Name}_GenericInvoke";

                src.Line.Write($"public static unsafe {member.ReturnTypeFQN} {member.Name}(this ref {input.BaseTypeFQN} self{methodParameters})");
                if (member.ReturnTypeFQN is "void")
                {
                    using (src.Braces)
                    {
                        src.Line.Write($"switch (self.{input.TypeIDFieldName})");
                        using (src.Braces)
                        {
                            foreach (var derived in derivedStructs)
                            {
                                src.Line.Write($"case {input.TypeIDEnumFQN}.{derived.Name}:");
                                using (src.Indent)
                                {
                                    string comma = member is { IsProperty: false, Parameters.Length: > 0 }
                                        ? ", "
                                        : "";

                                    if (derived.PubliclyImplementedMembers.AsArray().Contains(member.Name))
                                    {
                                        src.Line.Write($"{m}UnsafeUtility.As<{input.BaseTypeFQN}, {derived.FQN}>(ref self).{member.Name}{callParametersWithInvoke}; return;");
                                    }
                                    else
                                    {
                                        src.Line.Write($"{genericInvokeName}(ref {m}UnsafeUtility.As<{input.BaseTypeFQN}, {derived.FQN}>(ref self){comma}{callParameters}); return;");
                                        emitGenericInvokeHelper = true;
                                    }
                                }
                            }

                            src.Line.Write("default:");
                            using (src.Indent)
                            {
                                var outParameters = member.Parameters
                                    .AsArray()
                                    .Zip(member.ParameterRefKinds.AsArray(), (call, refKind) => (call: GetParameterName(call), refKind))
                                    .Where(x => x.refKind is (byte)RefKind.Out)
                                    .ToArray();

                                foreach (var (call, _) in outParameters)
                                    src.Line.Write($"{call} = default;");

                                if (outParameters.Length > 0)
                                    emitWithAssignHelper = true;

                                src.Line.Write($"ThrowUnknownTypeException(self.{input.TypeIDFieldName}); return;");
                            }
                        }
                    }
                }
                else
                {
                    using (src.Indent)
                    {
                        src.Line.Write($"=> self.{input.TypeIDFieldName} switch");
                        using (src.Braces)
                        {
                            foreach (var derived in derivedStructs)
                            {
                                src.Line.Write($"{input.TypeIDEnumFQN}.{derived.Name}");
                                using (src.Indent)
                                {
                                    string comma = member is { IsProperty: false, Parameters.Length: > 0 }
                                        ? ", "
                                        : "";

                                    if (derived.PubliclyImplementedMembers.AsArray().Contains(member.Name))
                                    {
                                        src.Line.Write($"=> {m}UnsafeUtility.As<{input.BaseTypeFQN}, {derived.FQN}>(ref self).{member.Name}{callParametersWithInvoke},");
                                    }
                                    else
                                    {
                                        src.Line.Write($"=> {genericInvokeName}(ref {m}UnsafeUtility.As<{input.BaseTypeFQN}, {derived.FQN}>(ref self){comma}{callParameters}),");
                                        emitGenericInvokeHelper = true;
                                    }
                                }
                            }

                            var outParameters = member.Parameters
                                .AsArray()
                                .Zip(member.ParameterRefKinds.AsArray(), (call, refKind) => (call: GetParameterName(call), refKind))
                                .Where(x => x.refKind is (byte)RefKind.Out)
                                .ToArray();

                            if (outParameters.Length > 0)
                                emitWithAssignHelper = true;

                            string outInit = outParameters
                                .Select(x => $".WithAssign(out {x.call})")
                                .Join(", ");

                            src.Line.Write($"_ => *({member.ReturnTypeFQN}*)ThrowUnknownTypeException(self.{input.TypeIDFieldName}){outInit},");
                        }

                        src.Write(';');
                    }
                }

                src.Linebreak();

                if (emitGenericInvokeHelper)
                {
                    src.Line.Write($"static {member.ReturnTypeFQN} {genericInvokeName}<T>(this ref T self{methodParameters}) where T : struct, {input.InterfaceFQN}");
                    using (src.Indent)
                        src.Line.Write($"=> self.{member.Name}{callParametersWithInvoke};");

                    src.Linebreak();
                }
            }

            // AsDerivedStruct methods
            foreach (var derived in derivedStructs)
            {
                src.Line.Write($"public static unsafe ref {derived.FQN} As{derived.Name}(this ref {input.BaseTypeFQN} self)");
                using (src.Braces)
                {
                    src.Write("\n#if DEBUG");
                    src.Line.Write($"if (self.{input.TypeIDFieldName} is not {input.TypeIDEnumFQN}.{derived.Name})");
                    using (src.Indent)
                        src.Line.Write($"ThrowUnexpectedTypeException(\"{derived.Name}\", self.TypeName);");

                    src.Write("\n#endif");
                    src.Line.Write($"return ref {m}UnsafeUtility.As<{input.BaseTypeFQN}, {derived.FQN}>(ref self);");
                }

                src.Linebreak();
            }

            // throw helpers
            src.Line.Write(Alias.NoInline);
            src.Line.Write($"static unsafe nint ThrowUnknownTypeException({input.TypeIDEnumFQN} typeId)");
            using (src.Braces)
            {
                src.Line.Write($"[{m}BurstDiscard]");
                src.Line.Write($"void Throw() => throw new global::System.InvalidOperationException($\"Unknown {input.BaseTypeName} type ID: {{typeId}}\");");
                src.Linebreak();
                src.Line.Write("Throw();");
                src.Line.Write("return 0;");
            }

            if (emitWithAssignHelper)
            {
                src.Line.Write(Alias.Inline);
                src.Line.Write($"static unsafe nint WithAssign<T>(this nint ptr, out T? value)");
                using (src.Braces)
                {
                    src.Line.Write("value = default;");
                    src.Line.Write("return ptr;");
                }
            }

            src.Linebreak();

            src.Line.Write($"[{m}BurstDiscard]");
            src.Line.Write(Alias.NoInline);
            src.Line.Write("static void ThrowUnexpectedTypeException(string expected, string got)");
            using (src.Indent)
                src.Line.Write("=> throw new global::System.InvalidOperationException($\"Invalid struct type ID: expected {expected}, got {got}\");");
        }

        // parent header support
        if (input.HasParentHeader && derivedStructs.Length > 0)
        {
            string parentPublic = input.IsPublic ? "public " : "";
            string helperMethodName = $"ThrowIncompatibleCastTo{input.BaseTypeName}Exception";

            src.Linebreak();
            src.Line.Write($"{parentPublic}static partial class {input.ParentTypeName}Extensions");
            using (src.Braces)
            {
                src.Line.Write($"public static bool Is{input.BaseTypeName}(this in {input.ParentTypeFQN} self)");
                using (src.Indent)
                {
                    src.Line.Write("=> self.TypeID switch");
                    using (src.Braces)
                    {
                        foreach (var derived in derivedStructs)
                            src.Line.Write($"{input.TypeIDEnumFQN}.{derived.Name} => true,");

                        src.Line.Write("_ => false,");
                    }
                }

                src.Write(';').Linebreak();
                src.Linebreak();

                src.Line.Write($"public static unsafe ref {input.BaseTypeFQN} As{input.BaseTypeName}(this ref {input.ParentTypeFQN} self)");
                using (src.Braces)
                {
                    src.Write("\n#if DEBUG");
                    src.Line.Write($"if (!self.Is{input.BaseTypeName}())");
                    using (src.Indent)
                        src.Line.Write($"{helperMethodName}(self.TypeID);");

                    src.Write("\n#endif");
                    src.Line.Write($"return ref {m}UnsafeUtility.As<{input.ParentTypeFQN}, {input.BaseTypeFQN}>(ref self);");
                }

                src.Linebreak();

                src.Line.Write($"[{m}BurstDiscard]");
                src.Line.Write(Alias.NoInline);
                src.Line.Write($"static void {helperMethodName}({input.RootTypeIDEnumFQN} typeId)");
                using (src.Indent)
                    src.Line.Write($"=> throw new global::System.InvalidOperationException($\"Cannot cast {input.ParentTypeName} to {input.BaseTypeName} - type ID is not compatible: {{typeId}}\");");
            }

            src.Linebreak();
        }
    }

    static string GetParameterName(string parameterDeclaration)
    {
        int lastSpaceIndex = parameterDeclaration.LastIndexOf(' ');
        return lastSpaceIndex >= 0
            ? parameterDeclaration[(lastSpaceIndex + 1)..]
            : parameterDeclaration;
    }
}
