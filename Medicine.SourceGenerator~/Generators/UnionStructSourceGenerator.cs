using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static System.StringComparison;
using static Constants;
using static Microsoft.CodeAnalysis.Accessibility;

[Generator]
public sealed class UnionStructSourceGenerator : IIncrementalGenerator
{
    record struct InterfaceMemberInput
    {
        public string Name { get; init; }
        public string ReturnTypeFQN { get; init; }
        public EquatableArray<string> Parameters { get; init; }
        public EquatableArray<byte> ParameterRefKinds { get; init; }
        public bool IsProperty { get; init; }
        public bool CanGet { get; init; }
        public bool CanSet { get; init; }
    }

    record struct HeaderFieldInput
    {
        public string Name { get; init; }
        public string TypeFQN { get; init; }
        public bool CanGet { get; init; }
        public bool CanSet { get; init; }
    }

    record struct DerivedInput
    {
        public string Name { get; init; }
        public string FQN { get; init; }
        public EquatableArray<string> Declaration { get; init; }
        public byte AssignedId { get; init; }
        public int EstimatedSizeInBytes { get; init; }
        public bool HasDirectHeader { get; init; }
        public EquatableArray<string> PubliclyImplementedMembers { get; init; }
        public EquatableArray<string> MemberNames { get; init; }
        public EquatableArray<string> ConstructorMemberInitializers { get; init; }
        public bool HasParameterlessConstructor { get; init; }
    }

    record struct DerivedDeferredInput
    {
        public EquatableArray<string> PublicMembers { get; init; }
        public EquatableArray<string> MemberNames { get; init; }
        public EquatableArray<string> ConstructorMemberInitializers { get; init; }
        public bool HasParameterlessConstructor { get; init; }
    }

    record struct Derived : ISourceGeneratorPassData
    {
        public string? SourceGeneratorOutputFilename { get; init; }
        public string? SourceGeneratorError { get; init; }
        public LocationInfo? SourceGeneratorErrorLocation { get; set; }

        public string DerivedFQN { get; init; }
        public string DerivedName { get; init; }
        public EquatableArray<string> Declaration { get; init; }
        public byte? ForcedId { get; init; }

        public EquatableIgnore<Func<string, bool>?> HasHeaderInChainFunc { get; init; }
        public EquatableIgnore<Func<string, bool>?> HasDirectHeaderFunc { get; init; }
        public EquatableIgnore<Func<string, bool>?> ImplementsUnionInterfaceFunc { get; init; }
        public EquatableIgnore<Func<int>?> EstimatedSizeInBytesBuilderFunc { get; init; }
        public EquatableIgnore<Func<DerivedDeferredInput>?> DeferredInputBuilderFunc { get; init; }

        // ReSharper disable once UnusedAutoPropertyAccessor.Local
        public EquatableArray<byte> DerivedTextCheckSumForCache { get; init; }
    }

    record struct GeneratorInput : ISourceGeneratorPassData
    {
        public string? SourceGeneratorOutputFilename { get; init; }
        public string? SourceGeneratorError { get; init; }
        public LocationInfo? SourceGeneratorErrorLocation { get; set; }
        public bool ShouldEmitDocs { get; init; }

        public LanguageVersion LangVersion { get; init; }
        public EquatableArray<string> BaseDeclaration { get; init; }
        public string BaseTypeName { get; init; }
        public string BaseTypeFQN { get; init; }
        public string InterfaceFQN { get; init; }
        public string TypeIDEnumFQN { get; init; }
        public string TypeIDFieldName { get; init; }
        public string RootTypeFQN { get; init; }
        public string RootInterfaceFQN { get; init; }
        public string RootTypeIDEnumFQN { get; init; }
        public bool IsRootTypeIDOwner { get; init; }
        public bool HasParentHeader { get; init; }
        public string ParentTypeName { get; init; }
        public string ParentTypeFQN { get; init; }
        public bool ParentIsGenericType { get; init; }
        public bool IsPublic { get; init; }
        public bool IsGenericType { get; init; }
        public int EstimatedBaseSizeInBytes { get; init; }

        public EquatableIgnore<Func<InterfaceMemberInput[]>?> InterfaceMembersBuilderFunc { get; init; }
        public EquatableIgnore<Func<HeaderFieldInput[]>?> HeaderFieldsBuilderFunc { get; init; }
        public EquatableIgnore<Func<DerivedInput[]>?> DerivedStructsBuilderFunc { get; init; }

        // ReSharper disable once UnusedAutoPropertyAccessor.Local
        public EquatableArray<byte> BaseTextCheckSumForCache { get; init; }
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
                .Select((x, ct) => x.Left with { LangVersion = x.Right })
                .CombineWithGeneratorEnvironment(context)
                .Select((x, ct) => x.Values with { ShouldEmitDocs = x.Environment.ShouldEmitDocs }),
            action: GenerateSource
        );
    }

    static GeneratorInput TransformBase(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context is not { TargetSymbol: ITypeSymbol symbol, TargetNode: StructDeclarationSyntax structDecl })
        {
            return new()
            {
                SourceGeneratorError = "Unexpected target shape for [UnionHeader].",
                SourceGeneratorErrorLocation = new LocationInfo(context.TargetNode.GetLocation()),
            };
        }

        if (symbol is not INamedTypeSymbol baseSymbol)
        {
            return new()
            {
                SourceGeneratorError = "Unexpected target symbol for [UnionHeader].",
                SourceGeneratorErrorLocation = new LocationInfo(context.TargetNode.GetLocation()),
            };
        }

        var symbolMembers = symbol.GetMembers().AsArray();
        var symbolTypeMembers = symbol.GetTypeMembers().AsArray();

        var interfaceSymbol = symbolTypeMembers.FirstOrDefault(x => x.Name is "Interface")
                              ?? symbolTypeMembers.FirstOrDefault(x => x.TypeKind is TypeKind.Interface);

        if (interfaceSymbol is null)
        {
            return new()
            {
                SourceGeneratorOutputFilename = Utility.GetOutputFilename(structDecl.SyntaxTree.FilePath, symbol.Name, "Union"),
            };
        }

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
            InterfaceFQN = interfaceSymbol.FQN,
            TypeIDEnumFQN = isRootTypeIdOwner
                ? typeIDEnumSymbol?.FQN ?? $"{symbol.FQN}.TypeIDs"
                : rootTypeIDEnumSymbol?.FQN ?? $"{rootHeader.FQN}.TypeIDs",
            TypeIDFieldName = typeIDField?.Name ?? "TypeID",
            RootTypeFQN = rootHeader.FQN,
            RootInterfaceFQN = rootInterface.FQN,
            RootTypeIDEnumFQN = rootTypeIDEnumSymbol?.FQN ?? $"{rootHeader.FQN}.TypeIDs",
            IsRootTypeIDOwner = isRootTypeIdOwner,
            HasParentHeader = hasNestedParent,
            ParentTypeName = hasNestedParent ? parentHeader!.Name : "",
            ParentTypeFQN = hasNestedParent ? parentHeader!.FQN : "",
            ParentIsGenericType = hasNestedParent && parentHeader!.IsGenericType,
            IsPublic = structDecl.Modifiers.Any(SyntaxKind.PublicKeyword),
            InterfaceMembersBuilderFunc = new(() => BuildInterfaceMembers(interfaceSymbol)),
            HeaderFieldsBuilderFunc = new(() => BuildHeaderFields(baseSymbol)),
            IsGenericType = baseSymbol.IsGenericType,
            EstimatedBaseSizeInBytes = StructSizeEstimator.EstimateTypeSizeInBytes(baseSymbol),
            BaseTextCheckSumForCache = structDecl.GetText().GetChecksum().AsArray(),
        };
    }

    static InterfaceMemberInput[] BuildInterfaceMembers(INamedTypeSymbol interfaceSymbol)
    {
        using var r1 = Scratch.RentA<List<InterfaceMemberInput>>(out var members);
        using var r2 = Scratch.RentA<HashSet<MethodSignatureKey>>(out var seenMethods);
        using var r3 = Scratch.RentA<HashSet<PropertySignatureKey>>(out var seenProperties);

        var interfaces = interfaceSymbol.AllInterfaces.AsArray();
        Array.Sort(interfaces, static (left, right) => left.FQN.CompareTo(right.FQN, Ordinal));

        AddMembers(interfaceSymbol);
        foreach (var @interface in interfaces)
            AddMembers(@interface);

        return members.ToArray();

        void AddMembers(INamedTypeSymbol @interface)
        {
            var declaredMembers = @interface.GetMembers()
                .OrderBy(x => x.Locations.FirstOrDefault()?.SourceTree?.FilePath, StringComparer.Ordinal)
                .ThenBy(x => x.Locations.FirstOrDefault()?.SourceSpan.Start ?? int.MaxValue)
                .ThenBy(x => x.Name, StringComparer.Ordinal);

            foreach (var member in declaredMembers)
            {
                if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary } method)
                {
                    if (!seenMethods.Add(new(method)))
                        continue;

                    int parameterCount = method.Parameters.Length;
                    var parameterRefKinds = new byte[parameterCount];
                    string[] parameters = new string[parameterCount];

                    for (int i = 0; i < parameterCount; i++)
                    {
                        var parameter = method.Parameters[i];
                        parameterRefKinds[i] = (byte)parameter.RefKind;
                        parameters[i] = $"{parameter.Type.FQN} {parameter.Name}";
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

                    continue;
                }

                if (member is not IPropertySymbol property)
                    continue;

                bool canGet = property.GetMethod is not null;
                bool canSet = property.SetMethod is not null;

                if (!canGet && !canSet)
                    continue;

                if (!seenProperties.Add(new(property)))
                    continue;

                members.Add(
                    new()
                    {
                        Name = property.Name,
                        ReturnTypeFQN = property.Type.FQN,
                        IsProperty = true,
                        CanGet = canGet,
                        CanSet = canSet,
                    }
                );
            }
        }
    }

    static HeaderFieldInput[] BuildHeaderFields(INamedTypeSymbol headerSymbol)
    {
        using var c1 = Scratch.RentA<List<HeaderFieldInput>>(out var result);

        var fieldsAndProperties = headerSymbol
            .GetMembers()
            .Where(x => x is IFieldSymbol or IPropertySymbol)
            .OrderBy(x => x.Locations.FirstOrDefault()?.SourceSpan.Start ?? int.MaxValue);

        foreach (var member in fieldsAndProperties)
        {
            switch (member)
            {
                case IFieldSymbol { IsStatic: false, IsImplicitlyDeclared: false } field:
                {
                    if (!field.IsAccessible)
                        break;

                    result.Add(
                        new()
                        {
                            Name = field.Name,
                            TypeFQN = field.Type.FQN,
                            CanGet = true,
                            CanSet = !field.IsReadOnly && !field.IsConst,
                        }
                    );

                    break;
                }
                case IPropertySymbol { IsStatic: false, IsIndexer: false } property:
                {
                    bool canGet = property.GetMethod is { IsAccessible: true };
                    bool canSet = property.SetMethod is { IsAccessible: true };

                    if (!canGet && !canSet)
                        break;

                    result.Add(
                        new()
                        {
                            Name = property.Name,
                            TypeFQN = property.Type.FQN,
                            CanGet = canGet,
                            CanSet = canSet,
                        }
                    );

                    break;
                }
            }
        }

        return result.ToArray();
    }

    static Derived TransformDerivedCandidate(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetNode is not StructDeclarationSyntax structDecl)
            return default;

        if (context.SemanticModel.GetDeclaredSymbol(structDecl, ct) is not { } symbol)
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
            ForcedId = forcedId,
            HasHeaderInChainFunc = new(headerFQN => HasHeaderInChain(symbol, headerFQN)),
            HasDirectHeaderFunc = new(headerFQN => HasDirectHeader(symbol, headerFQN)),
            ImplementsUnionInterfaceFunc = new(interfaceFQN => symbol.AllInterfaces.Any(x => x.FQN == interfaceFQN)),
            EstimatedSizeInBytesBuilderFunc = new(() => StructSizeEstimator.EstimateTypeSizeInBytes(symbol)),
            DeferredInputBuilderFunc = new(() => BuildDerivedDeferredInput(symbol)),
            DerivedTextCheckSumForCache = structDecl.GetText().GetChecksum().AsArray(),
        };
    }

    static DerivedDeferredInput BuildDerivedDeferredInput(INamedTypeSymbol symbol)
        => new()
        {
            PublicMembers = symbol.GetMembers()
                .Where(x => x is { DeclaredAccessibility: Public } and not IMethodSymbol { MethodKind: not MethodKind.Ordinary })
                .Select(x => x.Name)
                .Distinct()
                .ToArray(),
            MemberNames = symbol.GetMembers()
                .Select(x => x.Name)
                .Distinct()
                .ToArray(),
            ConstructorMemberInitializers = BuildConstructorMemberInitializers(symbol),
            HasParameterlessConstructor = symbol.InstanceConstructors.Any(x => x is { Parameters.Length: 0, IsImplicitlyDeclared: false }),
        };

    static string[] BuildConstructorMemberInitializers(INamedTypeSymbol symbol)
    {
        using var r1 = Scratch.RentA<List<string>>(out var members);

        var declarations = symbol.DeclaringSyntaxReferences
            .Select(x => x.GetSyntax())
            .OfType<StructDeclarationSyntax>()
            .OrderBy(x => x.SyntaxTree.FilePath, StringComparer.Ordinal)
            .ThenBy(x => x.SpanStart);

        foreach (var declaration in declarations)
        foreach (var member in declaration.Members)
        {
            if (member is FieldDeclarationSyntax field)
                if (field.Modifiers.None(SyntaxKind.StaticKeyword, SyntaxKind.ConstKeyword, SyntaxKind.FixedKeyword))
                    foreach (var variable in field.Declaration.Variables)
                        if (variable.Initializer is null)
                            members.Add(variable.Identifier.Text);

            if (member is PropertyDeclarationSyntax
                {
                    Initializer: null,
                    ExplicitInterfaceSpecifier: null,
                    IsAutoProperty: true,
                    Modifiers.IsStatic: false,
                    Identifier.Text: { } text,
                })
                members.Add(text);
        }

        return members.ToArray();
    }

    static GeneratorInput CombineBaseAndDerived((GeneratorInput Base, ImmutableArray<Derived> Candidates) input, CancellationToken ct)
    {
        var result = input.Base;
        var candidates = input.Candidates.AsArray();

        var firstError = result.GetError();

        if (firstError is null)
            foreach (var candidate in candidates)
                if ((firstError = candidate.GetError()) is not null)
                    break;

        return result with
        {
            DerivedStructsBuilderFunc = new(()
                => BuildDerivedStructs(candidates, result.InterfaceFQN, result.RootInterfaceFQN, result.BaseTypeFQN, result.RootTypeFQN)
            ),
            SourceGeneratorError = firstError?.error,
            SourceGeneratorErrorLocation = firstError?.location,
        };
    }

    readonly record struct CandidateSortItem(int CandidateIndex, string CandidateName, bool HasForcedId);

    readonly record struct AssignedCandidate(int CandidateIndex, byte AssignedId);

    static DerivedInput[] BuildDerivedStructs(Derived[] candidates, string interfaceFQN, string rootInterfaceFQN, string headerFQN, string rootHeaderFQN)
    {
        using var r1 = Scratch.RentA<List<CandidateSortItem>>(out var rootCandidates);
        using var r2 = Scratch.RentA<HashSet<byte>>(out var usedIds);
        using var r3 = Scratch.RentA<List<AssignedCandidate>>(out var assignedRootCandidates);
        using var r4 = Scratch.RentA<List<DerivedInput>>(out var derivedStructs);
        rootCandidates.EnsureCapacity(candidates.Length);

        for (int i = 0; i < candidates.Length; i++)
        {
            var candidate = candidates[i];
            if (candidate.ImplementsUnionInterfaceFunc.Value?.Invoke(rootInterfaceFQN) is not true)
                continue;

            if (candidate.HasHeaderInChainFunc.Value?.Invoke(rootHeaderFQN) is not true)
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

        assignedRootCandidates.EnsureCapacity(rootCandidates.Count);
        byte nextId = 1;
        foreach (var rootCandidate in rootCandidates)
        {
            assignedRootCandidates.Add(
                new(
                    rootCandidate.CandidateIndex,
                    candidates[rootCandidate.CandidateIndex].ForcedId ?? GetNextAvailableId(usedIds, ref nextId)
                )
            );
        }

        derivedStructs.EnsureCapacity(assignedRootCandidates.Count);
        foreach (var assigned in assignedRootCandidates)
        {
            var candidate = candidates[assigned.CandidateIndex];
            if (candidate.ImplementsUnionInterfaceFunc.Value?.Invoke(interfaceFQN) is not true)
                continue;

            if (candidate.HasHeaderInChainFunc.Value?.Invoke(headerFQN) is not true)
                continue;

            var deferredInput = candidate.DeferredInputBuilderFunc.Value?.Invoke() ?? default;

            derivedStructs.Add(
                new()
                {
                    Name = candidate.DerivedName,
                    FQN = candidate.DerivedFQN,
                    Declaration = candidate.Declaration,
                    AssignedId = assigned.AssignedId,
                    EstimatedSizeInBytes = candidate.EstimatedSizeInBytesBuilderFunc.Value?.Invoke() ?? -1,
                    HasDirectHeader = candidate.HasDirectHeaderFunc.Value?.Invoke(headerFQN) is true,
                    PubliclyImplementedMembers = deferredInput.PublicMembers,
                    MemberNames = deferredInput.MemberNames,
                    ConstructorMemberInitializers = deferredInput.ConstructorMemberInitializers,
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

    static bool HasHeaderInChain(INamedTypeSymbol symbol, string headerFQN)
    {
        var firstHeaderFieldType = GetFirstHeaderFieldType(symbol);
        if (firstHeaderFieldType is null)
            return false;

        using (Scratch.RentA<HashSet<string>>(out var visited))
        {
            var current = firstHeaderFieldType;
            while (current is not null && current.HasAttribute(UnionHeaderStructAttributeFQN))
            {
                if (!visited.Add(current.FQN))
                    break;

                if (current.FQN.Equals(headerFQN, Ordinal))
                    return true;

                current = GetFirstHeaderFieldType(current);
            }
        }

        return false;
    }

    static bool HasDirectHeader(INamedTypeSymbol symbol, string headerFQN)
        => GetFirstHeaderFieldType(symbol)?.FQN.Equals(headerFQN, Ordinal) ?? false;

    static byte GetNextAvailableId(HashSet<byte> usedIds, ref byte nextId)
    {
        while (usedIds.Contains(nextId))
            nextId++;

        return nextId++;
    }

    static IFieldSymbol? GetFirstHeaderField(INamedTypeSymbol symbol)
        => symbol.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(x => !x.IsStatic)
            .OrderBy(x => x.Locations.FirstOrDefault()?.SourceSpan.Start ?? int.MaxValue)
            .FirstOrDefault();

    static INamedTypeSymbol? GetFirstHeaderFieldType(INamedTypeSymbol symbol)
    {
        var firstField = GetFirstHeaderField(symbol);

        return firstField is { Type: INamedTypeSymbol headerType } && headerType.HasAttribute(UnionHeaderStructAttributeFQN)
            ? headerType
            : null;
    }

    static INamedTypeSymbol? GetUnionInterface(INamedTypeSymbol headerType)
    {
        var typeMembers = headerType.GetTypeMembers().AsArray();
        return typeMembers.FirstOrDefault(x => x is { Name: "Interface", TypeKind: TypeKind.Interface, DeclaredAccessibility: Public }) ??
               typeMembers.FirstOrDefault(x => x is { TypeKind: TypeKind.Interface, DeclaredAccessibility: Public });
    }

    static INamedTypeSymbol GetRootHeader(INamedTypeSymbol headerSymbol, INamedTypeSymbol headerInterface)
    {
        var currentHeader = headerSymbol;
        var currentInterface = headerInterface;
        using var r1 = Scratch.RentA<HashSet<string>>(out var visited);

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
        src.ShouldEmitDocs = input.ShouldEmitDocs;

        var derivedStructs = input.DerivedStructsBuilderFunc.Value?.Invoke() ?? [];
        var interfaceMembers = input.InterfaceMembersBuilderFunc.Value?.Invoke() ?? [];
        var headerFields = input.HeaderFieldsBuilderFunc.Value?.Invoke() ?? [];
        int wrapperSizeInBytes = input.EstimatedBaseSizeInBytes;

        foreach (var derived in derivedStructs)
            if (derived.EstimatedSizeInBytes > wrapperSizeInBytes)
                wrapperSizeInBytes = derived.EstimatedSizeInBytes;

        bool emitWrapper = !input.IsGenericType && wrapperSizeInBytes > 0;

        src.Line.Write(Alias.UsingInline);
        src.Line.Write($"using {m}UnsafeUtility = global::Unity.Collections.LowLevel.Unsafe.UnsafeUtility;");
        src.Line.Write($"using {m}BurstDiscard = global::Unity.Burst.BurstDiscardAttribute;");
        src.Linebreak();

        // 1. derived structs generated members
        foreach (var derived in derivedStructs)
        {
            foreach (var x in derived.Declaration.AsSpan())
            {
                src.Line.Write(x);
                src.Line.Write('{');
                src.IncreaseIndent();
            }

            foreach (var headerField in headerFields)
            {
                if (input.IsRootTypeIDOwner)
                    if (headerField.Name.Equals(input.TypeIDFieldName, Ordinal))
                        continue;

                if (derived.MemberNames.AsArray().Contains(headerField.Name))
                    continue;

                src.Line.Write($"public {headerField.TypeFQN} {headerField.Name}");
                using (src.Braces)
                {
                    if (headerField.CanGet)
                        src.Line.Write($"get => {m}UnsafeUtility.As<{derived.FQN}, {input.BaseTypeFQN}>(ref this).{headerField.Name};");

                    if (headerField.CanSet)
                        src.Line.Write($"set => {m}UnsafeUtility.As<{derived.FQN}, {input.BaseTypeFQN}>(ref this).{headerField.Name} = value;");
                }

                src.Linebreak();
            }

            if (input.IsRootTypeIDOwner)
            {
                src.Doc?.Write($"/// <summary>");
                src.Doc?.Write($"/// Type identifier value for this union variant.");
                src.Doc?.Write($"/// </summary>");
                src.Line.Write($"public const {input.TypeIDEnumFQN} TypeID = {input.TypeIDEnumFQN}.{derived.Name};");
                src.Linebreak();

                if (input.LangVersion >= LanguageVersion.CSharp10 && !derived.HasParameterlessConstructor)
                {
                    src.Line.Write($"public {derived.Name}()");
                    using (src.Braces)
                    {
                        foreach (var initializer in derived.ConstructorMemberInitializers.AsSpan())
                            src.Line.Write($"{initializer} = default;");

                        src.Line.Write($"{m}UnsafeUtility.As<{derived.FQN}, {input.BaseTypeFQN}>(ref this).{input.TypeIDFieldName} = TypeID;");
                    }
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
                src.Doc?.Write($"/// <summary>");
                src.Doc?.Write($"/// Enumerates generated union type identifiers.");
                src.Doc?.Write($"/// </summary>");
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

                src.Doc?.Write($"/// <summary>");
                src.Doc?.Write($"/// Returns the estimated size in bytes of the current union value.");
                src.Doc?.Write($"/// </summary>");
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

                src.Doc?.Write($"/// <summary>");
                src.Doc?.Write($"/// Returns the display name of the actual union type.");
                src.Doc?.Write($"/// </summary>");
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
                src.Doc?.Write($"/// <summary>");
                src.Doc?.Write($"/// Returns the type identifier of the actual union type.");
                src.Doc?.Write($"/// </summary>");
                src.Line.Write($"public {input.TypeIDEnumFQN} TypeID");
                using (src.Indent)
                    src.Line.Write($"=> {m}UnsafeUtility.As<{input.BaseTypeFQN}, {input.ParentTypeFQN}>(ref this).TypeID");

                src.Write(';').Linebreak();

                src.Doc?.Write($"/// <summary>");
                src.Doc?.Write($"/// Returns the estimated size in bytes of the actual union type.");
                src.Doc?.Write($"/// </summary>");
                src.Line.Write("public int SizeInBytes");
                using (src.Indent)
                    src.Line.Write($"=> {m}UnsafeUtility.As<{input.BaseTypeFQN}, {input.ParentTypeFQN}>(ref this).SizeInBytes");

                src.Write(';').Linebreak();

                src.Doc?.Write($"/// <summary>");
                src.Doc?.Write($"/// Returns the display name of the actual union type.");
                src.Doc?.Write($"/// </summary>");
                src.Line.Write("public string TypeName");
                using (src.Indent)
                    src.Line.Write($"=> {m}UnsafeUtility.As<{input.BaseTypeFQN}, {input.ParentTypeFQN}>(ref this).TypeName");

                src.Write(';').Linebreak();
            }

            if (emitWrapper)
            {
                src.Line.Write($"[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Explicit, Size = {wrapperSizeInBytes})]");
                src.Doc?.Write($"/// <summary>");
                src.Doc?.Write($"/// Mutable wrapper that exposes the header through the generated union interface.");
                src.Doc?.Write($"/// </summary>");
                src.Line.Write("public struct Wrapper : " + input.InterfaceFQN);
                using (src.Braces)
                {
                    src.Line.Write("[global::System.Runtime.InteropServices.FieldOffset(0)]");
                    src.Doc?.Write($"/// <summary>");
                    src.Doc?.Write($"/// Underlying union header value.");
                    src.Doc?.Write($"/// </summary>");
                    src.Line.Write($"public {input.BaseTypeFQN} Header;");
                    src.Linebreak();

                    foreach (var headerField in headerFields)
                    {
                        if (input.IsRootTypeIDOwner)
                            if (headerField.Name.Equals(input.TypeIDFieldName, Ordinal))
                                continue;

                        if (headerField.Name.Equals("Header", Ordinal))
                            continue;

                        src.Line.Write($"public {headerField.TypeFQN} {headerField.Name}");
                        using (src.Braces)
                        {
                            if (headerField.CanGet)
                                src.Line.Write($"get => Header.{headerField.Name};");

                            if (headerField.CanSet)
                                src.Line.Write($"set => Header.{headerField.Name} = value;");
                        }

                        src.Linebreak();
                    }

                    foreach (var member in interfaceMembers)
                    {
                        static string GetParameterName(string parameterDeclaration)
                            => parameterDeclaration.LastIndexOf(' ') switch
                            {
                                0     => parameterDeclaration,
                                var i => parameterDeclaration[(i + 1)..]
                            };

                        var parametersEnumerable = member.Parameters
                            .AsArray()
                            .Zip(member.ParameterRefKinds.AsArray().Cast<RefKind>(), (call, refKind) => (call, refKind))
                            .Select(x => $"{x.refKind.AsRefString()}{x.call}");

                        string parameters = parametersEnumerable.Join(", ");

                        var callParametersEnumerable = member.Parameters
                            .AsArray()
                            .Zip(member.ParameterRefKinds.AsArray().Cast<RefKind>(), (call, refKind) => (call, refKind))
                            .Select(x => $"{x.refKind.AsRefString()}{GetParameterName(x.call)}");

                        string callParameters = callParametersEnumerable.Join(", ");
                        string callParametersWithInvoke = $"({callParameters})";

                        if (!member.IsProperty)
                        {
                            src.Line.Write($"public {member.ReturnTypeFQN} {member.Name}({parameters})");
                            using (src.Indent)
                                src.Line.Write($"=> Header.{member.Name}{callParametersWithInvoke}");

                            src.Write(';').Linebreak();
                            src.Linebreak();
                            continue;
                        }

                        src.Line.Write($"public {member.ReturnTypeFQN} {member.Name}");
                        using (src.Braces)
                        {
                            if (member.CanGet)
                                src.Line.Write($"get => Header.{member.Name}();");

                            if (member.CanSet)
                                src.Line.Write($"set => Header.{member.Name}(value);");
                        }

                        src.Linebreak();
                    }
                }

                src.Linebreak();
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
        src.Doc?.Write($"/// <summary>");
        src.Doc?.Write($"/// Generated extension helpers for <see cref=\"{input.BaseTypeFQN}\"/> union dispatch and casts.");
        src.Doc?.Write($"/// </summary>");
        src.Line.Write($"{@public}static partial class {input.BaseTypeName}Extensions");
        using (src.Braces)
        {
            // polymorphic methods/properties
            foreach (var member in interfaceMembers)
            {
                static string GetParameterName(string parameterDeclaration)
                    => parameterDeclaration.LastIndexOf(' ') switch
                    {
                        0     => parameterDeclaration,
                        var i => parameterDeclaration[(i + 1)..]
                    };

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

                bool shouldEmitGetter = !member.IsProperty || member.CanGet;
                if (shouldEmitGetter)
                {
                    src.Doc?.Write($"/// <summary>");
                    src.Doc?.Write($"/// Dispatches to <c>{member.Name}</c> for the current union type.");
                    src.Doc?.Write($"/// </summary>");
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

                if (member is not { IsProperty: true, CanSet: true })
                    continue;

                string setterGenericInvokeName = $"{member.Name}_Set_GenericInvoke";
                src.Doc?.Write($"/// <summary>");
                src.Doc?.Write($"/// Dispatches assignment to <c>{member.Name}</c> for the actual union type.");
                src.Doc?.Write($"/// </summary>");
                src.Line.Write($"public static unsafe void {member.Name}(this ref {input.BaseTypeFQN} self, {member.ReturnTypeFQN} value)");
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
                                if (derived.PubliclyImplementedMembers.AsArray().Contains(member.Name))
                                    src.Line.Write($"{m}UnsafeUtility.As<{input.BaseTypeFQN}, {derived.FQN}>(ref self).{member.Name} = value; return;");
                                else
                                    src.Line.Write($"{setterGenericInvokeName}(ref {m}UnsafeUtility.As<{input.BaseTypeFQN}, {derived.FQN}>(ref self), value); return;");
                            }
                        }

                        src.Line.Write("default:");
                        using (src.Indent)
                            src.Line.Write($"ThrowUnknownTypeException(self.{input.TypeIDFieldName}); return;");
                    }
                }

                src.Linebreak();
                src.Line.Write($"static void {setterGenericInvokeName}<T>(this ref T self, {member.ReturnTypeFQN} value) where T : struct, {input.InterfaceFQN}");
                using (src.Indent)
                    src.Line.Write($"=> self.{member.Name} = value;");

                src.Linebreak();
            }

            // AsDerivedStruct methods
            if (emitWrapper)
            {
                src.Doc?.Write($"/// <summary>");
                src.Doc?.Write($"/// Reinterprets the union header as its generated wrapper type.");
                src.Doc?.Write($"/// </summary>");
                src.Line.Write($"public static unsafe ref {input.BaseTypeFQN}.Wrapper Wrap(this ref {input.BaseTypeFQN} self)");
                using (src.Indent)
                    src.Line.Write($"=> ref {m}UnsafeUtility.As<{input.BaseTypeFQN}, {input.BaseTypeFQN}.Wrapper>(ref self);");

                src.Linebreak();

                foreach (var derived in derivedStructs)
                {
                    if (!derived.HasDirectHeader)
                        continue;

                    src.Doc?.Write($"/// <summary>");
                    src.Doc?.Write($"/// Returns the union struct as the full-size generated wrapper.");
                    src.Doc?.Write($"/// The wrapper struct has size that is max of all possible union types, making it safe for storage.");
                    src.Doc?.Write($"/// </summary>");
                    src.Line.Write($"public static unsafe ref {input.BaseTypeFQN}.Wrapper Wrap(this ref {derived.FQN} self)");
                    using (src.Indent)
                        src.Line.Write($"=> ref {m}UnsafeUtility.As<{derived.FQN}, {input.BaseTypeFQN}.Wrapper>(ref self);");

                    src.Linebreak();
                }
            }

            if (input.HasParentHeader)
            {
                src.Doc?.Write($"/// <summary>");
                src.Doc?.Write($"/// Reinterprets <see cref=\"{input.BaseTypeFQN}\"/> as <see cref=\"{input.ParentTypeFQN}\"/>.");
                src.Doc?.Write($"/// </summary>");
                src.Line.Write($"public static unsafe ref {input.ParentTypeFQN} As{input.ParentTypeName}(this ref {input.BaseTypeFQN} self)");
                using (src.Indent)
                    src.Line.Write($"=> ref {m}UnsafeUtility.As<{input.BaseTypeFQN}, {input.ParentTypeFQN}>(ref self);");

                src.Linebreak();

                if (emitWrapper && !input.ParentIsGenericType)
                {
                    src.Doc?.Write($"/// <summary>");
                    src.Doc?.Write($"/// Reinterprets <see cref=\"{input.BaseTypeFQN}.Wrapper\"/> as <see cref=\"{input.ParentTypeFQN}.Wrapper\"/>.");
                    src.Doc?.Write($"/// </summary>");
                    src.Line.Write($"public static unsafe ref {input.ParentTypeFQN}.Wrapper As{input.ParentTypeName}(this ref {input.BaseTypeFQN}.Wrapper self)");
                    using (src.Indent)
                        src.Line.Write($"=> ref {m}UnsafeUtility.As<{input.BaseTypeFQN}.Wrapper, {input.ParentTypeFQN}.Wrapper>(ref self);");

                    src.Linebreak();
                }
            }

            // AsDerivedStruct methods
            foreach (var derived in derivedStructs)
            {
                src.Doc?.Write($"/// <summary>");
                src.Doc?.Write($"/// Reinterprets the union header as <see cref=\"{derived.FQN}\"/>.");
                src.Doc?.Write($"/// </summary>");
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

                if (!emitWrapper)
                    continue;

                src.Doc?.Write($"/// <summary>");
                src.Doc?.Write($"/// Reinterprets the wrapper as <see cref=\"{derived.FQN}\"/>.");
                src.Doc?.Write($"/// </summary>");
                src.Line.Write($"public static unsafe ref {derived.FQN} As{derived.Name}(this ref {input.BaseTypeFQN}.Wrapper self)");
                using (src.Braces)
                {
                    src.Write("\n#if DEBUG");
                    src.Line.Write($"if (self.Header.{input.TypeIDFieldName} is not {input.TypeIDEnumFQN}.{derived.Name})");
                    using (src.Indent)
                        src.Line.Write($"ThrowUnexpectedTypeException(\"{derived.Name}\", self.Header.TypeName);");

                    src.Write("\n#endif");
                    src.Line.Write($"return ref {m}UnsafeUtility.As<{input.BaseTypeFQN}.Wrapper, {derived.FQN}>(ref self);");
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
            src.Doc?.Write($"/// <summary>");
            src.Doc?.Write($"/// Generated helpers for casting <see cref=\"{input.ParentTypeFQN}\"/> values to <see cref=\"{input.BaseTypeFQN}\"/>.");
            src.Doc?.Write($"/// </summary>");
            src.Line.Write($"{parentPublic}static partial class {input.ParentTypeName}Extensions");
            using (src.Braces)
            {
                src.Doc?.Write($"/// <summary>");
                src.Doc?.Write($"/// Returns <c>true</c> when the parent header currently stores a <see cref=\"{input.BaseTypeFQN}\"/> compatible type.");
                src.Doc?.Write($"/// </summary>");
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

                src.Doc?.Write($"/// <summary>");
                src.Doc?.Write($"/// Reinterprets a compatible <see cref=\"{input.ParentTypeFQN}\"/> value as <see cref=\"{input.BaseTypeFQN}\"/>.");
                src.Doc?.Write($"/// </summary>");
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
}
