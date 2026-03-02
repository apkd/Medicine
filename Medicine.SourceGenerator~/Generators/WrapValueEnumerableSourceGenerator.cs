using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static ActivePreprocessorSymbolNames;
using static Constants;
using static Microsoft.CodeAnalysis.SpeculativeBindingOption;

[Generator]
sealed class WrapValueEnumerableSourceGenerator : IIncrementalGenerator
{
    void IIncrementalGenerator.Initialize(IncrementalGeneratorInitializationContext context)
    {
        var wrapRequests =
            context
                .SyntaxProvider
                .ForAttributeWithMetadataNameEx(
                    fullyQualifiedMetadataName: WrapValueEnumerableAttributeMetadataName,
                    predicate: static (x, ct) => x is MethodDeclarationSyntax or PropertyDeclarationSyntax,
                    transform: TransformForCache
                )
                .Combine(context.GetGeneratorEnvironment())
                .SelectEx((x, ct) => Transform(x.Left, x.Right, ct));

        context.RegisterSourceOutputEx(wrapRequests, GenerateSource);
    }

    static ContextWithCacheGeneratorInput TransformForCache(GeneratorAttributeSyntaxContext context, CancellationToken ct)
        => new()
        {
            Context = context,
            SourceGeneratorOutputFilename = GetOutputFilename(context),
            SourceGeneratorLocation = context.TargetNode.GetLocation(),
            Checksum64ForCache = (context.TargetNode.Parent ?? context.TargetNode).GetNodeChecksum(ct),
        };

    record struct GeneratorInput : ISourceGeneratorPassData
    {
        public string? SourceGeneratorOutputFilename { get; init; }
        public string? SourceGeneratorError { get; init; }
        public LocationInfo? SourceGeneratorLocation { get; set; }

        public GeneratorEnvironment GeneratorEnvironment { get; init; }
        public EquatableArray<string> Declaration { get; init; }
        public string? WrapperName { get; init; }
        public string? EnumerableFQN { get; init; }
        public string? EnumeratorFQN { get; init; }
        public string? EnumeratorInnerFQN { get; init; }
        public string? ElementTypeFQN { get; init; }
        public string? GetEnumeratorNamespace { get; init; }
        public bool IsPublic { get; init; }

        // ReSharper disable once NotAccessedField.Local
        public ulong MethodTextChecksumForCache;
    }

    static readonly string wrapEnumerableExample = """
            /// [WrapValueEnumerable]
            /// TestEnumerable1 Test1
            ///     => Find
            ///         .Instances<TrackedObject>()
            ///         .AsValueEnumerable()
            ///         .Select(z => z)
            ///         .Zip(MySingleton.Instance.name);
            ///  
        """.Trim()
        .HtmlEncode();

    static GeneratorInput Transform(ContextWithCacheGeneratorInput cacheInput, GeneratorEnvironment generatorEnvironment, CancellationToken ct)
    {
        var context = cacheInput.Context.Value;
        var knownSymbols = context.SemanticModel.Compilation.GetKnownSymbols();
        ITypeSymbol wrapperType;
        string wrapperName;
        MemberDeclarationSyntax declSyntax;

        ExpressionSyntax? GetSymbolRetExpr(ISymbol symbol)
        {
            var declRef = symbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (declRef is null)
                return null;

            var declNode = declRef.GetSyntax(ct);

            switch (symbol)
            {
                case IMethodSymbol:
                {
                    var methodDecl = declNode as MethodDeclarationSyntax;
                    return methodDecl?.ExpressionBody?.Expression
                           ?? methodDecl?.Body?.Statements.OfType<ReturnStatementSyntax>().LastOrDefault()?.Expression;
                }
                case IPropertySymbol:
                {
                    var propDecl = declNode as PropertyDeclarationSyntax;
                    var result = propDecl?.ExpressionBody?.Expression;

                    if (result is not null)
                        return result;

                    var accessorDeclarationSyntax = propDecl?.AccessorList?.Accessors
                        .LastOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));

                    result ??= accessorDeclarationSyntax
                        ?.ExpressionBody?.Expression;

                    result ??= accessorDeclarationSyntax?.Body?.Statements
                        .OfType<ReturnStatementSyntax>()
                        .LastOrDefault()
                        ?.Expression;

                    return result;
                }
                default:
                    return null;
            }
        }

        if (context.TargetSymbol is IMethodSymbol method)
        {
            wrapperType = method.ReturnType;
            wrapperName = wrapperType.Name;
            declSyntax = (MethodDeclarationSyntax)context.TargetNode;
        }
        else if (context.TargetSymbol is IPropertySymbol property)
        {
            wrapperType = property.Type;
            wrapperName = wrapperType.Name;
            declSyntax = (PropertyDeclarationSyntax)context.TargetNode;
        }
        else
        {
            return new() { SourceGeneratorError = $"Unexpected symbol type." };
        }

        var retExpr = GetSymbolRetExpr(context.TargetSymbol);

        var output = new GeneratorInput
        {
            SourceGeneratorOutputFilename = GetOutputFilename(context),
        };

        if (retExpr is null)
            return output with { SourceGeneratorError = $"Couldn't find the return expression." };

        var model = context.SemanticModel;

        ITypeSymbol? TryGetTypeFromExpression(ExpressionSyntax expr)
        {
            var result = model.GetTypeInfo(expr, ct).ConvertedType;
            if (result is not null and not IErrorTypeSymbol)
                return result;

            result = model.GetSpeculativeTypeInfo(expr.SpanStart, expr, BindAsExpression).ConvertedType;
            if (result is not null and not IErrorTypeSymbol)
                return result;

            var op = model.GetOperation(expr, ct);
            if (op?.Type is not null and not IErrorTypeSymbol)
                return op.Type;

            return result;
        }

        var retExprType = TryGetTypeFromExpression(retExpr);

        if (retExprType is null or IErrorTypeSymbol)
        {
            var identifiersToPatch = retExpr
                .DescendantNodesAndSelf()
                .OfType<IdentifierNameSyntax>()
                .ToArray();

            if (identifiersToPatch.Length > 0)
                retExprType = ReevaluateWithPatchedIdentifiers(model, retExpr, identifiersToPatch, knownSymbols, ct);
        }

        if (retExprType is null or IErrorTypeSymbol)
            return output with { SourceGeneratorError = $"Couldn't find type of the return expression." };

        IMethodSymbol? GetInstanceMethod()
            => retExprType.GetMembers("GetEnumerator")
                .OfType<IMethodSymbol>()
                .FirstOrDefault(x => x is { IsStatic: false, IsGenericMethod: false, Parameters.Length: 0 });

        IMethodSymbol? GetExtensionMethod()
            => model.LookupSymbols(
                    position: retExpr.SpanStart,
                    container: retExprType,
                    name: "GetEnumerator",
                    includeReducedExtensionMethods: true
                )
                .OfType<IMethodSymbol>()
                .FirstOrDefault(x => x is { MethodKind: MethodKind.ReducedExtension, Parameters.Length: 0 });

        var getEnumerator = GetInstanceMethod() ?? GetExtensionMethod();

        if (getEnumerator is null)
            return output with { SourceGeneratorError = "Could not get the GetEnumerator method." };

        string? enumeratorNamespace = getEnumerator.IsExtensionMethod
            ? getEnumerator.ContainingNamespace.ToDisplayString()
            : null;

        return output with
        {
            Declaration = Utility.DeconstructTypeDeclaration(declSyntax),
            WrapperName = wrapperName,
            MethodTextChecksumForCache = context.TargetNode.GetNodeChecksum(ct),
            EnumerableFQN = retExprType.FQN,
            EnumeratorFQN = getEnumerator.ReturnType.FQN,
            EnumeratorInnerFQN = (getEnumerator.ReturnType as INamedTypeSymbol)!.TypeArguments.First().FQN,
            ElementTypeFQN = (retExprType as INamedTypeSymbol)!.TypeArguments.Last().FQN,
            GetEnumeratorNamespace = enumeratorNamespace,
            IsPublic = declSyntax.Modifiers.Any(SyntaxKind.PublicKeyword),
            GeneratorEnvironment = generatorEnvironment,
        };
    }

    static string? GetOutputFilename(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetNode is not MemberDeclarationSyntax memberDecl)
            return null;

        string? wrapperName = context.TargetNode switch
        {
            MethodDeclarationSyntax x   => (x.ReturnType as NameSyntax)?.Text,
            PropertyDeclarationSyntax x => (x.Type as NameSyntax)?.Text,
            _                           => null,
        };

        if (wrapperName is null)
            return null;

        return Utility.GetOutputFilename(
            filePath: context.TargetNode.SyntaxTree.FilePath,
            targetNodeName: wrapperName,
            label: $"[{WrapValueEnumerableAttributeNameShort}]",
            additionalNameForHash: memberDecl.FullName
        );
    }

    static void GenerateSource(SourceProductionContext context, SourceWriter src, GeneratorInput input)
    {
        src.ShouldEmitDocs = input.GeneratorEnvironment.PreprocessorSymbols.Has(UNITY_EDITOR);

        if (input.GetEnumeratorNamespace is not null)
        {
            src.Line.Write($"using {input.GetEnumeratorNamespace};");
            src.Linebreak();
        }

        foreach (var x in input.Declaration.AsSpan())
        {
            src.Line.Write(x);
            src.Line.Write('{');
            src.IncreaseIndent();
        }

        {
            src.Doc?.Write("/// <summary>");
            src.Doc?.Write("/// This is a generated wrapper struct to simplify the name of the combined generic");
            src.Doc?.Write("/// <see cref=\"ZLinq.ValueEnumerable{TE, T}\"/> constructed from a ZLINQ query.");
            src.Doc?.Write("/// This makes it easier to define a method that returns the enumerable.");
            src.Doc?.Write("/// </summary>");
            src.Doc?.Write("/// <example>");
            src.Doc?.Write("/// <code>");
            src.Doc?.Write(wrapEnumerableExample);
            src.Doc?.Write("/// </code>");
            src.Doc?.Write("/// </example>");
        }

        src.Line.Write("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        string @public = input.IsPublic ? "public " : "";
        src.Line.Write($"{@public}readonly struct {input.WrapperName}");
        using (src.Indent)
        {
            src.Line.Write($": global::ZLinq.IValueEnumerable<");
            using (src.Indent)
            {
                src.Line.Write($"{input.EnumeratorInnerFQN:SplitGenericTypeName},");
                src.Line.Write($"{input.ElementTypeFQN}>");
            }
        }

        using (src.Braces)
        {
            src.Line.Write("public");
            using (src.Indent)
                src.Line.Write($"{input.EnumerableFQN:SplitGenericTypeName} Enumerable {{ get; init; }}");

            src.Linebreak();
            src.Line.Write("public");
            using (src.Indent)
            {
                src.Line.Write($"static implicit operator {input.WrapperName}(");
                using (src.Indent)
                {
                    src.Line.Write($"{input.EnumerableFQN:SplitGenericTypeName} enumerable)")
                        .Write(" => new() { Enumerable = enumerable };");
                }
            }

            src.Linebreak();

            src.Line.Write("public");
            using (src.Indent)
                src.Line.Write($"{input.EnumeratorFQN:SplitGenericTypeName} GetEnumerator() => Enumerable.GetEnumerator();");

            src.Linebreak();

            src.Line.Write("public");
            using (src.Indent)
                src.Line.Write($"{input.EnumerableFQN:SplitGenericTypeName} AsValueEnumerable() => Enumerable;");
        }

        foreach (var x in input.Declaration.AsSpan())
        {
            src.DecreaseIndent();
            src.Line.Write('}');
        }
    }

    static ITypeSymbol? ReevaluateWithPatchedIdentifiers(
        SemanticModel model,
        ExpressionSyntax retExpr,
        IdentifierNameSyntax[] identifiers,
        KnownSymbols knownSymbols,
        CancellationToken ct
    )
    {
        var unresolvedIdentifiers = identifiers
            .Where(x => model.GetSymbolInfo(x, ct).Symbol is null)
            .ToArray();

        if (unresolvedIdentifiers.Length == 0)
            return null;

        Dictionary<string, ITypeSymbol>? assignmentTypesByIdentifier;
        using (Scratch.RentA<HashSet<string>>(out var unresolvedIdentifierNames))
        {
            foreach (var identifier in unresolvedIdentifiers)
                unresolvedIdentifierNames.Add(identifier.Text);

            assignmentTypesByIdentifier = BuildInjectAssignmentTypeMap(model, retExpr, unresolvedIdentifierNames, ct);
        }

        Dictionary<(int Start, int Length), ITypeSymbol>? memberAccessCastTypes = null;
        Dictionary<string, ITypeSymbol>? identifierCastTypes = null;

        foreach (var identifier in unresolvedIdentifiers)
        {
            if (!TryInferIdentifierType(identifier, assignmentTypesByIdentifier, out var inferredType, out var memberAccessExpressionSyntax))
                continue;

            if (memberAccessExpressionSyntax is not null)
            {
                memberAccessCastTypes ??= [];
                var key = (memberAccessExpressionSyntax.SpanStart, memberAccessExpressionSyntax.Span.Length);
                if (!memberAccessCastTypes.ContainsKey(key))
                    memberAccessCastTypes[key] = inferredType;
            }
            else
            {
                identifierCastTypes ??= new(StringComparer.Ordinal);
                if (!identifierCastTypes.ContainsKey(identifier.Text))
                    identifierCastTypes[identifier.Text] = inferredType;
            }
        }

        if (memberAccessCastTypes is null && identifierCastTypes is null)
            return null;

        return SpeculativeTypePatching.Reevaluate(
            model,
            expression: retExpr,
            options: new(BindPosition: retExpr.SpanStart, ReturnOnFirstSuccessfulPass: true),
            identifierTypesByName: identifierCastTypes,
            memberAccessTypesByExpressionSpan: memberAccessCastTypes
        );

        bool TryInferIdentifierType(
            IdentifierNameSyntax identifier,
            IReadOnlyDictionary<string, ITypeSymbol>? cachedAssignmentTypes,
            [NotNullWhen(true)] out ITypeSymbol? inferredType,
            out MemberAccessExpressionSyntax? memberAccessExpressionSyntax
        )
        {
            inferredType = null;
            memberAccessExpressionSyntax = null;

            switch (identifier.Text)
            {
                // try to fix "SomeTrackedType.Instances"
                case "Instances":
                {
                    if (identifier.Parent is not MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax typeName } memberAccess)
                        break;

                    if (model.GetSymbolInfo(typeName, ct).Symbol is not INamedTypeSymbol typeSymbol)
                        break;

                    if (!typeSymbol.HasAttribute(knownSymbols.TrackAttribute))
                        break;

                    if (knownSymbols.TrackedInstances.Construct(typeSymbol) is not { } constructed)
                        break;

                    inferredType = constructed;
                    memberAccessExpressionSyntax = memberAccess;
                    return true;
                }
                // try to fix "SomeSingletonType.Instance"
                case "Instance":
                {
                    if (identifier.Parent is not MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax typeName } memberAccess)
                        break;

                    if (model.GetSymbolInfo(typeName, ct).Symbol is not INamedTypeSymbol typeSymbol)
                        break;

                    if (!typeSymbol.HasAttribute(knownSymbols.SingletonAttribute))
                        break;

                    inferredType = typeSymbol;
                    memberAccessExpressionSyntax = memberAccess;
                    return true;
                }
            }

            return cachedAssignmentTypes?.TryGetValue(identifier.Text, out inferredType) == true;
        }
    }

    static Dictionary<string, ITypeSymbol>? BuildInjectAssignmentTypeMap(
        SemanticModel model,
        ExpressionSyntax retExpr,
        HashSet<string> unresolvedIdentifierNames,
        CancellationToken ct
    )
    {
        if (retExpr.FirstAncestorOrSelf<TypeDeclarationSyntax>() is not { } containingType)
            return null;

        Dictionary<string, ITypeSymbol>? assignmentTypesByIdentifier = null;

        foreach (var method in containingType.Members.OfType<MethodDeclarationSyntax>())
        {
            if (!method.HasAttribute(x => x is InjectAttributeNameShort or InjectAttributeName or InjectAttributeMetadataName))
                continue;

            foreach (var assignment in method.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (assignment.Left is not IdentifierNameSyntax leftIdentifier)
                    continue;

                var identifierName = leftIdentifier.Text;
                if (!unresolvedIdentifierNames.Contains(identifierName))
                    continue;

                if (assignmentTypesByIdentifier?.ContainsKey(identifierName) is true)
                    continue;

                var rhsType = model.GetTypeInfo(assignment.Right, ct).Type;
                if (rhsType is null or IErrorTypeSymbol)
                    continue;

                assignmentTypesByIdentifier ??= new(StringComparer.Ordinal);
                assignmentTypesByIdentifier[identifierName] = rhsType;

                if (assignmentTypesByIdentifier.Count == unresolvedIdentifierNames.Count)
                    return assignmentTypesByIdentifier;
            }
        }

        return assignmentTypesByIdentifier;
    }
}
