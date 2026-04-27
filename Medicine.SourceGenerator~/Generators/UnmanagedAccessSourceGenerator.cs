using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using static ActivePreprocessorSymbolNames;
using static Constants;

[Generator]
public sealed class UnmanagedAccessSourceGenerator : IIncrementalGenerator
{
    static readonly DiagnosticDescriptor MED040 = new(
        id: nameof(MED040),
        title: "Conflicting [UnmanagedAccess] cast helper",
        messageFormat: "{0}",
        category: "Medicine",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    record struct GeneratorInput : ISourceGeneratorPassData
    {
        public string? SourceGeneratorOutputFilename { get; init; }
        public string? SourceGeneratorError { get; init; }
        public LocationInfo? SourceGeneratorLocation { get; set; }
        public EquatableArray<string> ContainingTypeDeclaration;
        public string ClassName;
        public string ClassFQN;
        public bool IsGenericType;
        public bool IsUnityObject;
        public bool UsesEntityId;
        public bool IsTracked;
        public GeneratorEnvironment GeneratorEnvironment;
        public AttributeSettings AttributeSettings;
        public Defer<bool>? HasCachedEnableBuilderDeferred;
        public Defer<bool>? HasIInstanceIndexBuilderDeferred;
        public EquatableArray<FieldInfo> Fields;
        public EquatableArray<string> BaseTypeFQNs;
        public Defer<string[]>? AccessROForwardingMembersForAccessRWDeferred;
    }

    record struct CastExtensionsInput : ISourceGeneratorPassData
    {
        public string? SourceGeneratorOutputFilename { get; init; }
        public string? SourceGeneratorError { get; init; }
        public LocationInfo? SourceGeneratorLocation { get; set; }
        public EquatableArray<CastInfo> Casts { get; set; }
        public EquatableArray<CastDiagnostic> Diagnostics { get; set; }
    }

    readonly record struct CastInfo(
        string SourceTypeFQN,
        string SourceTypeName,
        string TargetTypeFQN,
        string TargetTypeName,
        string HelperName
    );

    readonly record struct CastDiagnostic(string Message, LocationInfo? Location);

    record struct FieldInfo
    {
        public string Name;
        public string MetadataName;
        public string TypeFQN;
        public string ElementTypeFQN;
        public FieldFlags Flags;

        public readonly bool EmitsDirectAccess
            => Flags.Has(FieldFlags.TypeHasUnmanagedAccess) &&
               !Flags.Has(FieldFlags.IsManagedArrayType) &&
               !Flags.Has(FieldFlags.IsManagedListType);

        public readonly bool EmitsArrayNativeArray
            => Flags.Has(FieldFlags.IsManagedArrayType) &&
               (Flags.Has(FieldFlags.ElementIsUnmanagedType) || Flags.Has(FieldFlags.ElementIsReferenceType));

        public readonly bool EmitsListAccess
            => Flags.Has(FieldFlags.IsManagedListType) &&
               (Flags.Has(FieldFlags.ElementIsUnmanagedType) || Flags.Has(FieldFlags.ElementIsReferenceType));
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
        ElementIsUnmanagedType = 1 << 06,
        ElementHasUnmanagedAccess = 1 << 07,
        IsPrivateInBaseType = 1 << 08,
        IsManagedValueType = 1 << 09,
        IsManagedListType = 1 << 10,
        ElementIsReferenceType = 1 << 11,
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

        var castExtensionsProvider = inputProvider
            .Collect()
            .SelectEx(static (x, _) => BuildCastExtensionsInput(x));

        context.RegisterSourceOutputEx(
            source: castExtensionsProvider,
            action: GenerateCastExtensionsSource
        );
    }

    static ContextWithCacheGeneratorInput TransformForCache(GeneratorAttributeSyntaxContext context, CancellationToken ct)
        => new()
        {
            Context = context,
            SourceGeneratorOutputFilename = GetOutputFilename(context),
            SourceGeneratorLocation = context.TargetNode.GetLocation(),
            Checksum64ForCache = GetInputChecksum(context, ct),
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
            IsGenericType = typeSymbol is INamedTypeSymbol { IsGenericType: true },
            IsUnityObject = typeSymbol.InheritsFrom(knownSymbols.UnityObject),
            UsesEntityId = generatorEnvironment.IsUnity64OrNewer,
            IsTracked = trackAttribute is not null,
            SourceGeneratorLocation = new(typeDecl.Identifier.GetLocation()),
            BaseTypeFQNs = typeSymbol.GetBaseTypes().Select(static x => x.FQN).ToArray(),
            AccessROForwardingMembersForAccessRWDeferred = new(() => CollectAccessROForwardingMembersForAccessRW(context.SemanticModel.Compilation, typeSymbol, ct)),
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
                var listType = field.Type as INamedTypeSymbol;
                bool isManagedListType = listType?.OriginalDefinition.Is(knownSymbols.SystemList1) is true;
                var elementType = isManagedArrayType
                    ? arrayType!.ElementType
                    : isManagedListType
                        ? listType!.TypeArguments.FirstOrDefault()
                        : null;
                bool isNullableValueType = IsNullableValueType(field.Type);
                bool elementIsNullableValueType = elementType is not null && IsNullableValueType(elementType);
                bool elementTreatAsUnmanagedWrapper = elementType is not null && IsWrapperErrorType(elementType);
                bool isManagedValueType = field.Type is { IsValueType: true }
                                          && (!field.Type.IsUnmanagedType || isNullableValueType)
                                          && !treatAsUnmanagedWrapper;

                var fieldFlags
                    = 0
                      | (field.DeclaredAccessibility is Accessibility.Public ? FieldFlags.IsPublic : 0)
                      | (field.IsReadOnly ? FieldFlags.IsReadOnly : 0)
                      | ((field.Type.IsUnmanagedType && !isNullableValueType) || treatAsUnmanagedWrapper
                          ? FieldFlags.IsUnmanagedType
                          : 0)
                      | (field.Type.IsReferenceType && !treatAsUnmanagedWrapper ? FieldFlags.IsReferenceType : 0)
                      | (isManagedValueType ? FieldFlags.IsManagedValueType : 0)
                      | (isManagedArrayType ? FieldFlags.IsManagedArrayType : 0)
                      | (isManagedListType ? FieldFlags.IsManagedListType : 0)
                      | (field.Type.GetAttribute(knownSymbols.UnmanagedAccessAttribute) is not null ? FieldFlags.TypeHasUnmanagedAccess : 0)
                      | (elementType?.IsUnmanagedType is true && !elementIsNullableValueType || elementTreatAsUnmanagedWrapper
                          ? FieldFlags.ElementIsUnmanagedType
                          : 0)
                      | (elementType?.IsReferenceType is true && !elementTreatAsUnmanagedWrapper ? FieldFlags.ElementIsReferenceType : 0)
                      | (elementType?.GetAttribute(knownSymbols.UnmanagedAccessAttribute) is not null ? FieldFlags.ElementHasUnmanagedAccess : 0)
                      | (isFromBaseType && field.DeclaredAccessibility is Accessibility.Private ? FieldFlags.IsPrivateInBaseType : 0);

                fields.Add(
                    new()
                    {
                        MetadataName = field.Name,
                        Name = field.IsImplicitlyDeclared
                            ? field.Name[1..^16] // trim generated backing field name
                            : field.Name,
                        TypeFQN = field.Type.FQN,
                        ElementTypeFQN = elementType?.FQN ?? string.Empty,
                        Flags = fieldFlags,
                    }
                );
            }

            output.Fields = fields.ToArray();
        }

        return output;

        static bool IsNullableValueType(ITypeSymbol type)
            => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
    }

    static CastExtensionsInput BuildCastExtensionsInput(ImmutableArray<GeneratorInput> inputs)
    {
        var result = new CastExtensionsInput
        {
            SourceGeneratorOutputFilename = "UnmanagedAccessCasts.g.cs",
        };

        using var r1 = Scratch.RentA<List<CastInfo>>(out var casts);
        using var r2 = Scratch.RentB<List<string>>(out var duplicateMessages);
        using var r3 = Scratch.RentC<HashSet<string>>(out var emittedKeys);
        using var r4 = Scratch.RentD<HashSet<string>>(out var duplicateKeys);

        var types = inputs
            .Where(static x => !x.IsGenericType && x.ClassFQN is { Length: > 0 })
            .OrderBy(static x => x.ClassFQN, StringComparer.Ordinal)
            .ToArray();

        foreach (var source in types)
        foreach (var target in types)
        {
            if (source.ClassFQN == target.ClassFQN)
                continue;

            bool isRelated
                = source.BaseTypeFQNs.AsArray().Contains(target.ClassFQN) ||
                  target.BaseTypeFQNs.AsArray().Contains(source.ClassFQN);

            if (!isRelated)
                continue;

            string helperName = $"As{target.ClassName.Sanitize()}";
            string key = $"{source.ClassFQN}|{helperName}";
            var cast = new CastInfo(
                source.ClassFQN,
                source.ClassName,
                target.ClassFQN,
                target.ClassName,
                helperName
            );

            if (!emittedKeys.Add(key))
            {
                duplicateKeys.Add(key);
                duplicateMessages.Add($"[UnmanagedAccess] generated cast helper '{helperName}' on '{source.ClassFQN}' conflicts with another related type with the same name.");
            }

            casts.Add(cast);
        }

        if (duplicateKeys.Count > 0)
            casts.RemoveAll(x => duplicateKeys.Contains($"{x.SourceTypeFQN}|{x.HelperName}"));

        result.Casts = casts.ToArray();
        result.Diagnostics = duplicateMessages
            .Distinct(StringComparer.Ordinal)
            .Select(static x => new CastDiagnostic(x, null))
            .ToArray();
        return result;
    }

    static ulong GetInputChecksum(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        ulong hash = offsetBasis;
        bool hasSyntaxReference = false;

        foreach (var syntaxReference in context.TargetSymbol.DeclaringSyntaxReferences
                     .OrderBy(x => x.SyntaxTree.FilePath, StringComparer.Ordinal)
                     .ThenBy(x => x.Span.Start))
        {
            hasSyntaxReference = true;
            hash = unchecked((hash ^ syntaxReference.SyntaxTree.GetText(ct).CalculateChecksum64()) * prime);
            hash = unchecked((hash ^ (ulong)syntaxReference.Span.Start) * prime);
        }

        return hasSyntaxReference
            ? hash
            : (context.TargetNode.Parent ?? context.TargetNode).GetNodeChecksum(ct);
    }

    static string[] CollectAccessROForwardingMembersForAccessRW(Compilation compilation, ITypeSymbol typeSymbol, CancellationToken ct)
    {
        using var r1 = Scratch.RentA<List<string>>(out var forwardingMembers);
        using var r2 = Scratch.RentB<HashSet<string>>(out var accessRWMemberKeys);

        foreach (var accessRWDeclaration in GetAccessStructDeclarations(typeSymbol, "AccessRW", ct))
        {
            var semanticModel = compilation.GetSemanticModel(accessRWDeclaration.SyntaxTree);

            foreach (var member in accessRWDeclaration.Members)
                if (TryGetForwardableMemberKey(member, semanticModel, ct, out var key))
                    accessRWMemberKeys.Add(key);
        }

        accessRWMemberKeys.Add("M:AsReadOnly`0()");

        foreach (var accessRODeclaration in GetAccessStructDeclarations(typeSymbol, "AccessRO", ct))
        {
            var semanticModel = compilation.GetSemanticModel(accessRODeclaration.SyntaxTree);

            foreach (var member in accessRODeclaration.Members)
            {
                if (!TryBuildAccessROForwardingMember(member, semanticModel, ct, out var key, out var source) ||
                    accessRWMemberKeys.Contains(key))
                    continue;

                accessRWMemberKeys.Add(key);
                forwardingMembers.Add(source);
            }
        }

        return forwardingMembers.ToArray();
    }

    static IEnumerable<TypeDeclarationSyntax> GetAccessStructDeclarations(ITypeSymbol typeSymbol, string accessStructName, CancellationToken ct)
    {
        foreach (var syntaxReference in typeSymbol.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax(ct) is not TypeDeclarationSyntax classDeclaration)
                continue;

            foreach (var unmanagedDeclaration in classDeclaration.Members.OfType<TypeDeclarationSyntax>())
            {
                if (unmanagedDeclaration.Identifier.ValueText is not "Unmanaged")
                    continue;

                foreach (var accessDeclaration in unmanagedDeclaration.Members.OfType<TypeDeclarationSyntax>())
                    if (accessDeclaration.Identifier.ValueText == accessStructName)
                        yield return accessDeclaration;
            }
        }
    }

    static bool TryGetForwardableMemberKey(
        MemberDeclarationSyntax member,
        SemanticModel semanticModel,
        CancellationToken ct,
        out string key
    )
    {
        key = "";

        switch (semanticModel.GetDeclaredSymbol(member, ct))
        {
            case IMethodSymbol { MethodKind: MethodKind.Ordinary, IsStatic: false } method:
                key = BuildMethodKey(method);
                return true;
            case IPropertySymbol { IsStatic: false } property:
                key = BuildPropertyKey(property);
                return true;
            default:
                return false;
        }
    }

    static bool TryBuildAccessROForwardingMember(
        MemberDeclarationSyntax member,
        SemanticModel semanticModel,
        CancellationToken ct,
        out string key,
        out string source
    )
    {
        key = "";
        source = "";

        switch (semanticModel.GetDeclaredSymbol(member, ct))
        {
            case IMethodSymbol { MethodKind: MethodKind.Ordinary, IsStatic: false } method
                when HasMethodImplementation(member) && IsForwardingAccessibility(method.DeclaredAccessibility):
            {
                key = BuildMethodKey(method);
                source = BuildForwardingMethod(method);
                return true;
            }

            case IPropertySymbol { IsStatic: false, GetMethod: not null } property
                when HasPropertyGetterImplementation(member) &&
                     IsForwardingAccessibility(property.GetMethod.DeclaredAccessibility):
            {
                key = BuildPropertyKey(property);
                source = BuildForwardingProperty(property);
                return true;
            }

            default:
                return false;
        }
    }

    static bool HasMethodImplementation(MemberDeclarationSyntax member)
        => member is MethodDeclarationSyntax { Body: not null } or
           MethodDeclarationSyntax { ExpressionBody: not null };

    static bool HasPropertyGetterImplementation(MemberDeclarationSyntax member)
        => member switch
        {
            PropertyDeclarationSyntax { ExpressionBody: not null } => true,
            IndexerDeclarationSyntax { ExpressionBody: not null } => true,
            PropertyDeclarationSyntax { AccessorList: { } accessorList } => HasGetterImplementation(accessorList),
            IndexerDeclarationSyntax { AccessorList: { } accessorList } => HasGetterImplementation(accessorList),
            _ => false,
        };

    static bool HasGetterImplementation(AccessorListSyntax accessorList)
        => accessorList.Accessors.Any(static x =>
            x.Keyword.IsKind(SyntaxKind.GetKeyword) &&
            (x.Body is not null || x.ExpressionBody is not null)
        );

    static bool IsForwardingAccessibility(Accessibility accessibility)
        => accessibility is Accessibility.Public or Accessibility.Internal;

    static string BuildForwardingMethod(IMethodSymbol method)
    {
        var typeParameters = BuildTypeParameterList(method.TypeParameters);
        var parameters = BuildParameterDeclarations(method.Parameters, includeDefaultValues: true);
        var callTypeParameters = BuildCallTypeParameterList(method.TypeParameters);
        var arguments = BuildArgumentList(method.Parameters);
        var constraints = BuildTypeParameterConstraints(method.TypeParameters);
        var refReturn = method.ReturnsByRef || method.ReturnsByRefReadonly ? "ref " : "";

        var src = new StringBuilder();
        src.AppendLine(Alias.Inline);
        src.AppendLine($"{GetAccessibility(method.DeclaredAccessibility)} {GetMethodReturnType(method)} {method.Name}{typeParameters}({parameters}){constraints}");
        src.Append($"    => {refReturn}AsReadOnly().{method.Name}{callTypeParameters}({arguments});");
        return src.ToString();
    }

    static string BuildForwardingProperty(IPropertySymbol property)
    {
        var accessibility = GetAccessibility(property.GetMethod!.DeclaredAccessibility);
        var returnType = GetPropertyReturnType(property);
        var refReturn = property.ReturnsByRef || property.ReturnsByRefReadonly ? "ref " : "";
        var src = new StringBuilder();

        if (property.IsIndexer)
        {
            var parameters = BuildParameterDeclarations(property.Parameters, includeDefaultValues: true);
            var arguments = BuildArgumentList(property.Parameters);

            src.AppendLine($"{accessibility} {returnType} this[{parameters}]");
            src.AppendLine("{");
            src.AppendLine($"    {Alias.Inline} get => {refReturn}AsReadOnly()[{arguments}];");
            src.Append("}");
            return src.ToString();
        }

        src.AppendLine($"{accessibility} {returnType} {property.Name}");
        src.AppendLine("{");
        src.AppendLine($"    {Alias.Inline} get => {refReturn}AsReadOnly().{property.Name};");
        src.Append("}");
        return src.ToString();
    }

    static string BuildMethodKey(IMethodSymbol method)
        => $"M:{method.Name}`{method.Arity}({BuildParameterKey(method.Parameters)})";

    static string BuildPropertyKey(IPropertySymbol property)
        => $"P:{property.Name}({BuildParameterKey(property.Parameters)})";

    static string BuildParameterKey(IEnumerable<IParameterSymbol> parameters)
        => string.Join(
            ",",
            parameters.Select(static x => $"{x.RefKind}:{x.Type.FQN}")
        );

    static string GetMethodReturnType(IMethodSymbol method)
    {
        if (method.ReturnsVoid)
            return "void";

        if (method.ReturnsByRefReadonly)
            return $"ref readonly {method.ReturnType.FQN}";

        if (method.ReturnsByRef)
            return $"ref {method.ReturnType.FQN}";

        return method.ReturnType.FQN;
    }

    static string GetPropertyReturnType(IPropertySymbol property)
    {
        if (property.ReturnsByRefReadonly)
            return $"ref readonly {property.Type.FQN}";

        if (property.ReturnsByRef)
            return $"ref {property.Type.FQN}";

        return property.Type.FQN;
    }

    static string GetAccessibility(Accessibility accessibility)
        => accessibility switch
        {
            Accessibility.Public             => "public",
            Accessibility.Internal           => "internal",
            Accessibility.Protected          => "protected",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            _                                => "private",
        };

    static string BuildTypeParameterList(ImmutableArray<ITypeParameterSymbol> typeParameters)
        => typeParameters.Length is 0
            ? ""
            : $"<{string.Join(", ", typeParameters.Select(static x => EscapeIdentifier(x.Name)))}>";

    static string BuildCallTypeParameterList(ImmutableArray<ITypeParameterSymbol> typeParameters)
        => typeParameters.Length is 0
            ? ""
            : $"<{string.Join(", ", typeParameters.Select(static x => EscapeIdentifier(x.Name)))}>";

    static string BuildTypeParameterConstraints(ImmutableArray<ITypeParameterSymbol> typeParameters)
    {
        var constraints = new List<string>();

        foreach (var typeParameter in typeParameters)
        {
            var parts = new List<string>();

            if (typeParameter.HasUnmanagedTypeConstraint)
                parts.Add("unmanaged");
            else if (typeParameter.HasValueTypeConstraint)
                parts.Add("struct");
            else if (typeParameter.HasReferenceTypeConstraint)
                parts.Add("class");
            else if (typeParameter.HasNotNullConstraint)
                parts.Add("notnull");

            foreach (var constraintType in typeParameter.ConstraintTypes)
                parts.Add(constraintType.FQN);

            if (typeParameter.HasConstructorConstraint && !typeParameter.HasValueTypeConstraint && !typeParameter.HasUnmanagedTypeConstraint)
                parts.Add("new()");

            if (parts.Count > 0)
            {
                var typeParameterName = EscapeIdentifier(typeParameter.Name);
                constraints.Add($" where {typeParameterName} : {string.Join(", ", parts)}");
            }
        }

        return string.Join("", constraints);
    }

    static string BuildParameterDeclarations(ImmutableArray<IParameterSymbol> parameters, bool includeDefaultValues)
        => string.Join(
            ", ",
            parameters.Select(x =>
                $"{(x.IsParams ? "params " : "")}{x.RefKind.AsRefString()}{x.Type.FQN} {EscapeIdentifier(x.Name)}{(includeDefaultValues ? FormatDefaultValue(x) : "")}"
            )
        );

    static string BuildArgumentList(ImmutableArray<IParameterSymbol> parameters)
        => string.Join(
            ", ",
            parameters.Select(static x => $"{x.RefKind.AsRefString()}{EscapeIdentifier(x.Name)}")
        );

    static string FormatDefaultValue(IParameterSymbol parameter)
    {
        if (!parameter.HasExplicitDefaultValue)
            return "";

        return $" = {FormatLiteral(parameter.ExplicitDefaultValue, parameter.Type)}";
    }

    static string FormatLiteral(object? value, ITypeSymbol type)
    {
        if (value is null)
            return "null";

        if (type.TypeKind is TypeKind.Enum)
            return $"({type.FQN}){Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture)}";

        return value switch
        {
            string s  => SymbolDisplay.FormatLiteral(s, quote: true),
            char c    => SymbolDisplay.FormatLiteral(c, quote: true),
            bool b    => b ? "true" : "false",
            float f   => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "f",
            double d  => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "d",
            decimal m => m.ToString(System.Globalization.CultureInfo.InvariantCulture) + "m",
            _         => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "default",
        };
    }

    static string EscapeIdentifier(string value)
        => SyntaxFacts.GetKeywordKind(value) is SyntaxKind.None
            ? value
            : $"@{value}";

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

    static void GenerateCastExtensionsSource(SourceProductionContext context, SourceWriter src, CastExtensionsInput input)
    {
        foreach (var diagnostic in input.Diagnostics.AsArray())
            context.ReportDiagnostic(Diagnostic.Create(
                    descriptor: MED040,
                    location: diagnostic.Location?.ToLocation() ?? Location.None,
                    messageArgs: diagnostic.Message
                )
            );

        if (input.Diagnostics.Length > 0 || input.Casts.Length is 0)
            return;

        src.Line.Write(Alias.UsingInline);
        src.Linebreak();

        src.Line.Write("namespace Medicine");
        using (src.Braces)
        {
            src.Doc?.Write("/// <summary>");
            src.Doc?.Write("/// Contains generated unmanaged access cast extension methods.");
            src.Doc?.Write("/// </summary>");

            src.Line.Write("public static partial class UnmanagedAccessExtensions");
            using (src.Braces)
            {
                foreach (var cast in input.Casts.AsArray())
                {
                    EmitCast("AccessRW");
                    src.Linebreak();
                    EmitCast("AccessRO");
                    src.Linebreak();

                    void EmitCast(string accessStructName)
                    {
                        src.Line.Write(Alias.Inline);
                        src.Line.Write($"public static {cast.TargetTypeFQN}.Unmanaged.{accessStructName} {cast.HelperName}(this in {cast.SourceTypeFQN}.Unmanaged.{accessStructName} access)");
                        using (src.Indent)
                            src.Line.Write($"=> new(new global::Medicine.UnmanagedRef<{cast.TargetTypeFQN}>(access.Ref.Ptr));");
                    }
                }
            }
        }
    }

    static void GenerateSource(SourceProductionContext context, SourceWriter src, GeneratorInput input)
    {
        src.ShouldEmitDocs = input.GeneratorEnvironment.ShouldEmitDocs;

        var symbols = input.GeneratorEnvironment.PreprocessorSymbols;
        var fields = input.Fields.AsArray();

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

        static string GetCollectionElementAccessType(in FieldInfo field)
            => field.Flags.Has(FieldFlags.ElementIsReferenceType)
                ? $"Medicine.UnmanagedRef<{field.ElementTypeFQN}>"
                : field.ElementTypeFQN;

        static string GetCollectionSourceElementType(in FieldInfo field)
            => field.ElementTypeFQN;

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

        src.Doc?.Write("/// <summary>");
        src.Doc?.Write($"/// Provides generated unmanaged field access APIs for <see cref=\"{input.ClassFQN}\"/>.");
        src.Doc?.Write("/// </summary>");

        src.Line.Write("public static partial class Unmanaged");
        using (src.Braces)
        {
            src.Line.Write($"static readonly global::Unity.Burst.SharedStatic<Layout> unmanagedLayoutStorage");
            using (src.Indent)
                src.Line.Write($"= global::Unity.Burst.SharedStatic<Layout>.GetOrCreate<Layout>();");

            src.Linebreak();

            src.Doc?.Write("/// <summary>");
            src.Doc?.Write("/// Returns the cached unmanaged layout metadata for the generated access API.");
            src.Doc?.Write("/// </summary>");

            src.Line.Write("public static ref Layout ClassLayout");
            using (src.Braces)
                src.Line.Write($"{Alias.Inline} get => ref unmanagedLayoutStorage.Data;");

            src.Linebreak();

            if (input.IsTracked)
            {
                src.Doc?.Write("/// <summary>");
                src.Doc?.Write("/// Returns unmanaged access wrappers aligned with the currently tracked instances.");
                src.Doc?.Write("/// </summary>");

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
            src.Doc?.Write("/// <summary>");
            src.Doc?.Write("/// Stores byte offsets for generated field accessors.");
            src.Doc?.Write("/// </summary>");

            src.Line.Write("public readonly struct Layout");
            using (src.Braces)
            {
                foreach (var x in fields)
                {
                    src.Doc?.Write("/// <summary>");
                    src.Doc?.Write($"/// Byte offset metadata for field <c>{x.Name}</c>.");
                    src.Doc?.Write("/// </summary>");

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
                    {
                        src.Line.Write($"{x.Name} = ᵐUtility.GetFieldOffset(typeof({m}Self), \"{x.MetadataName}\", ᵐBF.{ToBindingFlagsVisibility(x.Flags.Has(FieldFlags.IsPublic))} | ᵐBF.Instance),");

                    }

                src.Write(';');
            }

            src.Linebreak();

            if (input.IsTracked)
            {
                src.Doc?.Write("/// <summary>");
                src.Doc?.Write("/// Mutable unmanaged access collection wrapper.");
                src.Doc?.Write("/// </summary>");

                src.Line.Write("public partial struct AccessArray");
                using (src.Braces)
                {
                    src.Line.Write($"Medicine.Internal.UnmanagedAccessArray<{m}Self, Layout, AccessRW, AccessRO> impl;");
                    src.Linebreak();

                    src.Doc?.Write("/// <summary>");
                    src.Doc?.Write("/// Creates an access wrapper from an unmanaged reference list.");
                    src.Doc?.Write("/// </summary>");

                    src.Line.Write($"public AccessArray(global::Unity.Collections.LowLevel.Unsafe.UnsafeList<Medicine.UnmanagedRef<{m}Self>> classRefArray)");
                    using (src.Indent)
                        src.Line.Write("=> impl = new(classRefArray);");

                    src.Linebreak();

                    src.Doc?.Write("/// <summary>");
                    src.Doc?.Write("/// Creates an access wrapper from an array of managed instances.");
                    src.Doc?.Write("/// </summary>");

                    src.Line.Write($"public AccessArray({m}Self[]? classArray)");
                    using (src.Indent)
                        src.Line.Write($"=> impl = new(ᵐUtility.AsUnsafeList<{m}Self, Medicine.UnmanagedRef<{m}Self>>(classArray));");

                    src.Linebreak();

                    src.Doc?.Write("/// <summary>");
                    src.Doc?.Write("/// Rebinds this wrapper to a different unmanaged reference list.");
                    src.Doc?.Write("/// </summary>");

                    src.Line.Write($"public void UpdateBuffer(global::Unity.Collections.LowLevel.Unsafe.UnsafeList<Medicine.UnmanagedRef<{m}Self>> classRefArray)");
                    using (src.Indent)
                        src.Line.Write("=> impl.UpdateBuffer(classRefArray);");

                    src.Linebreak();

                    src.Doc?.Write("/// <summary>");
                    src.Doc?.Write("/// Rebinds this wrapper to a different managed instance array.");
                    src.Doc?.Write("/// </summary>");

                    src.Line.Write($"public void UpdateBuffer({m}Self[]? classArray)");
                    using (src.Indent)
                        src.Line.Write($"=> impl.UpdateBuffer(ᵐUtility.AsUnsafeList<{m}Self, Medicine.UnmanagedRef<{m}Self>>(classArray));");

                    src.Linebreak();

                    src.Doc?.Write("/// <summary>");
                    src.Doc?.Write("/// Returns the number of available elements.");
                    src.Doc?.Write("/// </summary>");

                    src.Line.Write($"public int Length");
                    using (src.Indent)
                        src.Line.Write("=> impl.Length;");

                    src.Linebreak();

                    src.Doc?.Write("/// <summary>");
                    src.Doc?.Write("/// Returns mutable unmanaged field access for the specified element index.");
                    src.Doc?.Write("/// </summary>");

                    src.Line.Write("public AccessRW this[int index]");
                    using (src.Braces)
                        src.Line.Write($"{Alias.Inline} get => impl[index];");

                    src.Linebreak();

                    src.Doc?.Write("/// <summary>");
                    src.Doc?.Write("/// Returns a sliced view of this access collection.");
                    src.Doc?.Write("/// </summary>");

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

                    src.Doc?.Write("/// <summary>");
                    src.Doc?.Write("/// Converts this wrapper to a read-only view.");
                    src.Doc?.Write("/// </summary>");

                    src.Line.Write(Alias.Inline);
                    src.Line.Write("public ReadOnly AsReadOnly()");
                    using (src.Indent)
                        src.Line.Write($"=> new(impl);");

                    src.Linebreak();

                    src.Doc?.Write("/// <summary>");
                    src.Doc?.Write("/// Returns an enumerator over mutable access elements.");
                    src.Doc?.Write("/// </summary>");

                    src.Line.Write(Alias.Inline);
                    src.Line.Write($"public Medicine.Internal.UnmanagedAccessArray<{m}Self, Layout, AccessRW, AccessRO>.Enumerator GetEnumerator()");

                    src.Linebreak();

                    using (src.Indent)
                        src.Line.Write("=> impl.GetEnumerator();");

                    src.Linebreak();

                    src.Doc?.Write("/// <summary>");
                    src.Doc?.Write("/// Read-only view over an <see cref=\"AccessArray\"/>.");
                    src.Doc?.Write("/// </summary>");

                    src.Line.Write("public readonly partial struct ReadOnly");
                    using (src.Braces)
                    {
                        src.Line.Write($"readonly Medicine.Internal.UnmanagedAccessArray<{m}Self, Layout, AccessRW, AccessRO>.ReadOnly impl;");
                        src.Linebreak();

                        src.Doc?.Write("/// <summary>");
                        src.Doc?.Write("/// Creates a read-only view from a mutable access wrapper.");
                        src.Doc?.Write("/// </summary>");

                        src.Line.Write($"public ReadOnly(Medicine.Internal.UnmanagedAccessArray<{m}Self, Layout, AccessRW, AccessRO> accessArray)");
                        using (src.Indent)
                            src.Line.Write("=> impl = accessArray.AsReadOnly();");

                        src.Linebreak();

                        src.Doc?.Write("/// <summary>");
                        src.Doc?.Write("/// Creates a read-only view from an existing read-only access wrapper.");
                        src.Doc?.Write("/// </summary>");

                        src.Line.Write($"public ReadOnly(Medicine.Internal.UnmanagedAccessArray<{m}Self, Layout, AccessRW, AccessRO>.ReadOnly accessArray)");
                        using (src.Indent)
                            src.Line.Write("=> impl = accessArray;");

                        src.Linebreak();

                        src.Doc?.Write("/// <summary>");
                        src.Doc?.Write("/// Returns the number of available elements.");
                        src.Doc?.Write("/// </summary>");

                        src.Line.Write($"public int Length");
                        using (src.Indent)
                            src.Line.Write("=> impl.Length;");

                        src.Linebreak();

                        src.Doc?.Write("/// <summary>");
                        src.Doc?.Write("/// Returns read-only unmanaged field access for the specified element index.");
                        src.Doc?.Write("/// </summary>");

                        src.Line.Write("public AccessRO this[int index]");
                        using (src.Braces)
                            src.Line.Write($"{Alias.Inline} get => impl[index];");

                        src.Linebreak();

                        src.Doc?.Write("/// <summary>");
                        src.Doc?.Write("/// Returns a sliced read-only view of this access collection.");
                        src.Doc?.Write("/// </summary>");

                        src.Line.Write("public ReadOnly this[global::System.Range range]");
                        using (src.Indent)
                            src.Line.Write("=> new(impl[range]);");

                        src.Linebreak();

                        src.Doc?.Write("/// <summary>");
                        src.Doc?.Write("/// Returns an enumerator over read-only access elements.");
                        src.Doc?.Write("/// </summary>");

                        src.Line.Write(Alias.Inline);
                        src.Line.Write($"public Medicine.Internal.UnmanagedAccessArray<{m}Self, Layout, AccessRW, AccessRO>.ReadOnly.Enumerator GetEnumerator()");
                        using (src.Indent)
                            src.Line.Write("=> impl.GetEnumerator();");
                    }
                }

                src.Linebreak();
            }

            src.Linebreak();

            src.Doc?.Write("/// <summary>");
            src.Doc?.Write("/// Mutable unmanaged access wrapper over a managed list of this class.");
            src.Doc?.Write("/// </summary>");

            src.Line.Write("public readonly unsafe partial struct ListAccess : global::System.Collections.Generic.IEnumerable<AccessRW>");
            using (src.Braces)
            {
                src.Line.Write($"readonly global::Medicine.ListAccess<{m}Self, Medicine.UnmanagedRef<{m}Self>> impl;");
                src.Line.Write("readonly Layout* layoutInfo;");
                src.Linebreak();

                src.Doc?.Write("/// <summary>");
                src.Doc?.Write("/// Creates a list access wrapper from a managed list reference.");
                src.Doc?.Write("/// </summary>");

                src.Line.Write(Alias.Inline);
                src.Line.Write($"public ListAccess(");
                src.Line.Write($"    Medicine.UnmanagedRef<global::System.Collections.Generic.List<{m}Self>> listRef");
                src.Line.Write($")");
                using (src.Braces)
                {
                    src.Line.Write("impl = new(listRef);");
                    src.Line.Write("layoutInfo = (Layout*)unmanagedLayoutStorage.UnsafeDataPointer;");
                }

                src.Linebreak();

                src.Doc?.Write("/// <summary>");
                src.Doc?.Write("/// Gets or sets the logical list element count.");
                src.Doc?.Write("/// </summary>");

                src.Line.Write("public int Count");
                using (src.Braces)
                {
                    src.Line.Write($"{Alias.Inline} get => impl.Count;");
                    src.Line.Write($"{Alias.Inline} set => impl.Count = value;");
                }

                src.Linebreak();

                src.Doc?.Write("/// <summary>");
                src.Doc?.Write("/// Returns the list backing data as unmanaged references.");
                src.Doc?.Write("/// </summary>");

                src.Line.Write(Alias.Inline);
                src.Line.Write($"public global::Unity.Collections.NativeArray<Medicine.UnmanagedRef<{m}Self>> AsNativeArray()");
                using (src.Indent)
                    src.Line.Write("=> impl.AsNativeArray();");

                src.Linebreak();

                src.Doc?.Write("/// <summary>");
                src.Doc?.Write("/// Returns an enumerator over generated read-write accessors.");
                src.Doc?.Write("/// </summary>");

                src.Line.Write(Alias.Inline);
                src.Line.Write("public Enumerator GetEnumerator()");
                using (src.Indent)
                    src.Line.Write("=> new(AsNativeArray().GetEnumerator(), layoutInfo);");

                src.Linebreak();

                src.Line.Write(Alias.Inline);
                src.Line.Write("global::System.Collections.Generic.IEnumerator<AccessRW> global::System.Collections.Generic.IEnumerable<AccessRW>.GetEnumerator()");
                using (src.Indent)
                    src.Line.Write("=> GetEnumerator();");

                src.Linebreak();

                src.Line.Write(Alias.Inline);
                src.Line.Write("global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator()");
                using (src.Indent)
                    src.Line.Write("=> GetEnumerator();");

                src.Linebreak();

                src.Line.Write("public unsafe struct Enumerator : global::System.Collections.Generic.IEnumerator<AccessRW>");
                using (src.Braces)
                {
                    src.Line.Write($"global::Unity.Collections.NativeArray<Medicine.UnmanagedRef<{m}Self>>.Enumerator enumerator;");
                    src.Line.Write("readonly Layout* layoutInfo;");
                    src.Linebreak();

                    src.Line.Write(Alias.Inline);
                    src.Line.Write($"public Enumerator(global::Unity.Collections.NativeArray<Medicine.UnmanagedRef<{m}Self>>.Enumerator enumerator, Layout* layoutInfo)");
                    using (src.Braces)
                    {
                        src.Line.Write("this.enumerator = enumerator;");
                        src.Line.Write("this.layoutInfo = layoutInfo;");
                    }

                    src.Linebreak();

                    src.Line.Write("public AccessRW Current");
                    using (src.Indent)
                        src.Line.Write("=> new(enumerator.Current, ref *layoutInfo);");

                    src.Linebreak();

                    src.Line.Write("object global::System.Collections.IEnumerator.Current");
                    using (src.Indent)
                        src.Line.Write("=> Current;");

                    src.Linebreak();

                    src.Line.Write(Alias.Inline);
                    src.Line.Write("public bool MoveNext()");
                    using (src.Indent)
                        src.Line.Write("=> enumerator.MoveNext();");

                    src.Linebreak();

                    src.Line.Write("public void Reset()");
                    using (src.Indent)
                        src.Line.Write("=> ((global::System.Collections.IEnumerator)enumerator).Reset();");

                    src.Linebreak();

                    src.Line.Write(Alias.Inline);
                    src.Line.Write("public void Dispose()");
                    using (src.Indent)
                        src.Line.Write("=> enumerator.Dispose();");
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

                src.Doc?.Write("/// <summary>");
                src.Doc?.Write(
                    isReadOnly
                        ? "/// Read-only unmanaged field accessor for a single instance."
                        : "/// Read-write unmanaged field accessor for a single instance."
                );
                src.Doc?.Write("/// </summary>");

                src.Line.Write($"public readonly unsafe partial struct {accessStructName}");
                using (src.Indent)
                {
                    src.Line.Write($": global::System.IEquatable<AccessRW>,");
                    src.Line.Write($"  global::System.IEquatable<AccessRO>,");
                    src.Line.Write($"  global::System.IEquatable<Medicine.UnmanagedRef<{m}Self>>");
                }

                using (src.Braces)
                {
                    src.Doc?.Write("/// <summary>");
                    src.Doc?.Write("/// Underlying unmanaged reference.");
                    src.Doc?.Write("/// </summary>");

                    src.Line.Write($"public readonly Medicine.UnmanagedRef<{m}Self> Ref;");
                    src.Line.Write("readonly Layout* layoutInfo;");
                    src.Linebreak();

                    src.Doc?.Write("/// <summary>");
                    src.Doc?.Write("/// Layout metadata used for field offset lookup.");
                    src.Doc?.Write("/// </summary>");

                    src.Line.Write("public ref readonly Layout Layout");
                    using (src.Indent)
                        src.Line.Write($"=> ref *layoutInfo;");

                    src.Linebreak();

                    src.Doc?.Write("/// <summary>");
                    src.Doc?.Write("/// Initializes the accessor using the globally cached class layout.");
                    src.Doc?.Write("/// </summary>");

                    src.Line.Write(Alias.Inline);
                    src.Line.Write($"public {accessStructName}(Medicine.UnmanagedRef<{m}Self> Ref)");
                    using (src.Braces)
                    {
                        src.Line.Write("this.Ref = Ref;");
                        src.Line.Write("layoutInfo = (Layout*)unmanagedLayoutStorage.UnsafeDataPointer;");
                    }

                    src.Doc?.Write("/// <summary>");
                    src.Doc?.Write("/// Initializes the accessor using a caller-provided layout reference.");
                    src.Doc?.Write("/// </summary>");

                    src.Line.Write(Alias.Inline);
                    src.Line.Write($"public {accessStructName}(Medicine.UnmanagedRef<{m}Self> Ref, ref Layout layout)");
                    using (src.Braces)
                    {
                        src.Line.Write("this.Ref = Ref;");
                        src.Line.Write($"layoutInfo = (Layout*){m}UU.AddressOf(ref layout);");
                    }

                    src.Linebreak();

                    src.Line.Write(Alias.Inline);
                    src.Line.Write($"public static implicit operator Medicine.UnmanagedRef<{m}Self>({accessStructName} access)");
                    using (src.Indent)
                        src.Line.Write("=> access.Ref;");

                    src.Linebreak();

                    src.Line.Write(Alias.Inline);
                    src.Line.Write("public bool Equals(AccessRW other)");
                    using (src.Indent)
                        src.Line.Write("=> Ref.Equals(other.Ref);");

                    src.Linebreak();

                    src.Line.Write(Alias.Inline);
                    src.Line.Write("public bool Equals(AccessRO other)");
                    using (src.Indent)
                        src.Line.Write("=> Ref.Equals(other.Ref);");

                    src.Linebreak();

                    src.Line.Write(Alias.Inline);
                    src.Line.Write($"public bool Equals(Medicine.UnmanagedRef<{m}Self> other)");
                    using (src.Indent)
                        src.Line.Write("=> Ref.Equals(other);");

                    src.Linebreak();

                    src.Line.Write("public override bool Equals(object? obj)");
                    using (src.Braces)
                    {
                        src.Line.Write("if (obj is AccessRW accessRW)");
                        using (src.Indent)
                            src.Line.Write("return Equals(accessRW);");

                        src.Linebreak();

                        src.Line.Write("if (obj is AccessRO accessRO)");
                        using (src.Indent)
                            src.Line.Write("return Equals(accessRO);");

                        src.Linebreak();

                        src.Line.Write($"if (obj is Medicine.UnmanagedRef<{m}Self> unmanagedRef)");
                        using (src.Indent)
                            src.Line.Write("return Equals(unmanagedRef);");

                        src.Linebreak();

                        src.Line.Write("return false;");
                    }

                    src.Linebreak();

                    src.Line.Write(Alias.Inline);
                    src.Line.Write("public override int GetHashCode()");
                    using (src.Indent)
                        src.Line.Write("=> Ref.Ptr.GetHashCode();");

                    src.Linebreak();

                    if (!isReadOnly)
                    {
                        src.Doc?.Write("/// <summary>");
                        src.Doc?.Write("/// Returns a read-only view over this unmanaged accessor.");
                        src.Doc?.Write("/// </summary>");

                        src.Line.Write(Alias.Inline);
                        src.Line.Write("public AccessRO AsReadOnly()");
                        using (src.Indent)
                            src.Line.Write("=> new(Ref, ref *layoutInfo);");

                        src.Linebreak();
                    }

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
                        src.Doc?.Write($"/// <inheritdoc cref=\"Medicine.UnmanagedRefExtensions.IsValid\" />");
                        src.Line.Write($"public bool IsValid");
                        using (src.Indent)
                            src.Line.Write($"=> Medicine.UnmanagedRefExtensions.IsValid(Ref);");

                        src.Doc?.Write($"/// <inheritdoc cref=\"Medicine.UnmanagedRefExtensions.IsInvalid\" />");
                        src.Line.Write($"public bool IsInvalid");
                        using (src.Indent)
                            src.Line.Write($"=> Medicine.UnmanagedRefExtensions.IsInvalid(Ref);");

                        if (input.UsesEntityId)
                        {
                            src.Doc?.Write($"/// <inheritdoc cref=\"Medicine.UnmanagedRefExtensions.GetEntityID\" />");
                            src.Line.Write($"public global::UnityEngine.EntityId EntityID");
                            PropertyWithSafetyChecks($"Medicine.UnmanagedRefExtensions.GetEntityID(Ref);");

                            src.Doc?.Write("/// <summary>Legacy Unity object identity API. Use <see cref=\"EntityID\"/> on Unity 6000.4 or newer.</summary>");
                            src.Line.Write($"[global::System.Obsolete(\"{InstanceIdMigrationMessage}\", true)]");
                            src.Line.Write("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
                            src.Line.Write($"public int InstanceID");
                            ObsoleteInstanceIdProperty();
                        }
                        else
                        {
                            src.Doc?.Write($"/// <inheritdoc cref=\"Medicine.UnmanagedRefExtensions.GetInstanceID\" />");
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

                        if (field.EmitsArrayNativeArray)
                            return isReadOnly
                                ? $"global::Unity.Collections.NativeArray<{GetCollectionElementAccessType(field)}>.ReadOnly"
                                : $"global::Unity.Collections.NativeArray<{GetCollectionElementAccessType(field)}>";

                        if (field.EmitsListAccess)
                        {
                            if (isReadOnly)
                                return $"global::Unity.Collections.NativeArray<{GetCollectionElementAccessType(field)}>";

                            if (field.Flags.Has(FieldFlags.ElementHasUnmanagedAccess))
                                return $"{field.ElementTypeFQN}.Unmanaged.ListAccess";

                            if (field.Flags.Has(FieldFlags.ElementIsUnmanagedType))
                                return $"global::Medicine.ListAccess<{field.ElementTypeFQN}>";

                            return $"global::Medicine.ListAccess<{field.ElementTypeFQN}, {GetCollectionElementAccessType(field)}>";
                        }

                        return $"{(isReadOnly || field.Flags.Has(FieldFlags.IsReadOnly) ? "ref readonly" : "ref")} Medicine.UnmanagedRef<{field.TypeFQN}>";
                    }

                    string GetProjectedListAccess(in FieldInfo field)
                    {
                        var listRef = $"Ref.Read<Medicine.UnmanagedRef<{field.TypeFQN}>>(layoutInfo->{field.Name})";

                        if (isReadOnly)
                            return $"ᵐUtility.AsNativeArray<{GetCollectionSourceElementType(field)}, {GetCollectionElementAccessType(field)}>({listRef});";

                        if (field.Flags.Has(FieldFlags.ElementHasUnmanagedAccess))
                            return $"new {field.ElementTypeFQN}.Unmanaged.ListAccess({listRef});";

                        if (field.Flags.Has(FieldFlags.ElementIsUnmanagedType))
                            return $"new global::Medicine.ListAccess<{GetCollectionSourceElementType(field)}>({listRef});";

                        return $"new global::Medicine.ListAccess<{GetCollectionSourceElementType(field)}, {GetCollectionElementAccessType(field)}>({listRef});";
                    }

                    string GetProjectedAccess(in FieldInfo field)
                    {
                        if (field.Flags.Has(FieldFlags.IsUnmanagedType))
                            return $"ref Ref.Read<{field.TypeFQN}>(layoutInfo->{field.Name});";

                        if (field.Flags.Has(FieldFlags.IsManagedValueType))
                            return $"ref {m}Utility.AsRefUnchecked<{field.TypeFQN}>((void*)(Ref.Ptr + layoutInfo->{field.Name}));";

                        if (field.EmitsDirectAccess)
                            return $"new {field.TypeFQN}.Unmanaged.{(isReadOnly ? "AccessRO" : "AccessRW")}(Ref.Read<Medicine.UnmanagedRef<{field.TypeFQN}>>(layoutInfo->{field.Name}));";

                        if (field.EmitsArrayNativeArray)
                        {
                            var arrayRef = $"Ref.Read<Medicine.UnmanagedRef<{field.TypeFQN}>>(layoutInfo->{field.Name})";
                            return $"ᵐUtility.AsNativeArray{(isReadOnly ? "RO" : string.Empty)}<{GetCollectionSourceElementType(field)}, {GetCollectionElementAccessType(field)}>({arrayRef}, ᵐUtility.GetArrayLength({arrayRef}));";
                        }

                        if (field.EmitsListAccess)
                            return GetProjectedListAccess(field);

                        return $"ref Ref.Read<Medicine.UnmanagedRef<{field.TypeFQN}>>(layoutInfo->{field.Name});";
                    }

                    foreach (var x in fields)
                    {
                        if (x.Flags.Has(FieldFlags.IsUnmanagedType)
                            || x.Flags.Has(FieldFlags.IsReferenceType)
                            || x.Flags.Has(FieldFlags.IsManagedValueType))
                        {
                            src.Doc?.Write($"/// <inheritdoc cref=\"{m}Self.{x.Name}\" />");

                            // base-private fields aren't accessible from here, so we omit `DeclaredAt`
                            if (!x.Flags.Has(FieldFlags.IsPrivateInBaseType))
                                src.Line.Write($"[{m}DeclaredAt(nameof({m}Self.{x.Name}))]");
                        }

                        src.Line.Write($"public {GetProjectedType(x)} {x.Name}");
                        PropertyWithSafetyChecks(GetProjectedAccess(x));
                    }

                    if (!isReadOnly)
                    {
                        foreach (var member in input.AccessROForwardingMembersForAccessRWDeferred?.Value ?? [])
                        {
                            src.Linebreak();

                            foreach (var line in member.Split('\n'))
                                src.Line.Write(line.TrimEnd('\r'));
                        }
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
            src.Doc?.Write("/// <summary>");
            src.Doc?.Write($"/// Extension methods for <see cref=\"UnmanagedRef{{T}}\"/> when <c>T</c> is <see cref=\"{input.ClassFQN}\"/>.");
            src.Doc?.Write("/// </summary>");

            src.Line.Write("public static partial class UnmanagedAccessExtensions");
            using (src.Braces)
            {
                src.Doc?.Write("/// <summary>");
                src.Doc?.Write("/// Returns a generated read-write unmanaged accessor.");
                src.Doc?.Write("/// </summary>");

                src.Line.Write(Alias.Inline);
                src.Line.Write($"public static {m}Self.Unmanaged.AccessRW AccessRW(this in UnmanagedRef<{m}Self> classRef)");
                using (src.Indent)
                    src.Line.Write("=> new(classRef);");

                src.Linebreak();

                src.Doc?.Write($"/// <inheritdoc cref=\"AccessRW(in UnmanagedRef{{T}})\" />");
                src.Line.Write(Alias.Inline);
                src.Line.Write($"public static {m}Self.Unmanaged.AccessRW AccessRW(this in UnmanagedRef<{m}Self> classRef, ref {m}Self.Unmanaged.Layout layout)");
                using (src.Indent)
                    src.Line.Write("=> new(classRef, ref layout);");

                src.Linebreak();

                src.Doc?.Write("/// <summary>");
                src.Doc?.Write("/// Returns a generated read-only unmanaged accessor.");
                src.Doc?.Write("/// </summary>");

                src.Line.Write(Alias.Inline);
                src.Line.Write($"public static {m}Self.Unmanaged.AccessRO AccessRO(this UnmanagedRef<{m}Self> classRef)");
                using (src.Indent)
                    src.Line.Write("=> new(classRef);");

                src.Linebreak();

                src.Linebreak();
                src.Doc?.Write($"/// <inheritdoc cref=\"AccessRO(UnmanagedRef{{T}})\" />");
                src.Line.Write(Alias.Inline);
                src.Line.Write($"public static {m}Self.Unmanaged.AccessRO AccessRO(this UnmanagedRef<{m}Self> classRef, ref {m}Self.Unmanaged.Layout layout)");
                using (src.Indent)
                    src.Line.Write("=> new(classRef, ref layout);");

                src.Linebreak();
            }
        }
    }
}
