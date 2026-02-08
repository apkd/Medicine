using System.Collections.Immutable;
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
        var knownSymbolsProvider = context.GetKnownSymbolsProvider();

        var medicineSettings = context.CompilationProvider
            .Combine(context.ParseOptionsProvider)
            .Select((x, ct) => new MedicineSettings(x, ct));

        var wrapRequests =
            context
                .SyntaxProvider
                .ForAttributeWithMetadataNameEx(
                    fullyQualifiedMetadataName: WrapValueEnumerableAttributeMetadataName,
                    predicate: static (x, ct) => x is MethodDeclarationSyntax or PropertyDeclarationSyntax,
                    transform: TransformForCache
                )
                .Combine(knownSymbolsProvider)
                .SelectEx((x, ct) => Transform(x.Left, x.Right, ct))
                .Combine(medicineSettings)
                .Select((x, ct) => x.Left with { Symbols = x.Right.PreprocessorSymbolNames });

        context.RegisterSourceOutputEx(wrapRequests, GenerateSource);
    }

    [SuppressMessage("ReSharper", "NotAccessedPositionalProperty.Local")]
    record struct CacheInput : IGeneratorTransformOutput
    {
        public EquatableIgnore<GeneratorAttributeSyntaxContext> Context { get; init; }

        public string? SourceGeneratorOutputFilename { get; set; }
        public string? SourceGeneratorError { get; init; }
        public LocationInfo? SourceGeneratorErrorLocation { get; set; }

        // explicitly avoid regenerating code unless this property has changed.
        // - this generator is fairly slow and can potentially depend on a lot of unpredictable context
        // - in most cases, though, we're only interested in the body of the method/property changing
        //   ReSharper disable once NotAccessedField.Local
        public EquatableArray<byte> RawTextForCache;
    }

    static CacheInput TransformForCache(GeneratorAttributeSyntaxContext context, CancellationToken ct)
        => new()
        {
            RawTextForCache = (context.TargetNode.Parent ?? context.TargetNode).GetText().GetChecksum().AsArray(),
            Context = context,
        };

    record struct GeneratorInput : IGeneratorTransformOutput
    {
        public string? SourceGeneratorOutputFilename { get; init; }
        public string? SourceGeneratorError { get; init; }
        public LocationInfo? SourceGeneratorErrorLocation { get; set; }

        public EquatableArray<string> Declaration;
        public string? WrapperName;
        public string? EnumerableFQN;
        public string? EnumeratorFQN;
        public string? EnumeratorInnerFQN;
        public string? ElementTypeFQN;
        public string? GetEnumeratorNamespace;
        public bool IsPublic;
        public ActivePreprocessorSymbolNames Symbols;

        // ReSharper disable once NotAccessedField.Local
        public ImmutableArray<byte> MethodTextChecksumForCache;
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

    static GeneratorInput Transform(CacheInput cacheInput, in KnownSymbols knownSymbols, CancellationToken ct)
    {
        var context = cacheInput.Context.Value;
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

        var outputFilename = Utility.GetOutputFilename(declSyntax.SyntaxTree.FilePath, wrapperName, context.TargetSymbol.FQN);
        var retExpr = GetSymbolRetExpr(context.TargetSymbol);

        var output = new GeneratorInput
        {
            SourceGeneratorOutputFilename = outputFilename,
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
            Declaration = Utility.DeconstructTypeDeclaration(declSyntax, context.SemanticModel, ct),
            WrapperName = wrapperName,
            MethodTextChecksumForCache = context.TargetNode.GetText().GetChecksum(),
            EnumerableFQN = retExprType.FQN,
            EnumeratorFQN = getEnumerator.ReturnType.FQN,
            EnumeratorInnerFQN = (getEnumerator.ReturnType as INamedTypeSymbol)!.TypeArguments.First().FQN,
            ElementTypeFQN = (retExprType as INamedTypeSymbol)!.TypeArguments.Last().FQN,
            GetEnumeratorNamespace = enumeratorNamespace,
            IsPublic = declSyntax.Modifiers.Any(SyntaxKind.PublicKeyword),
        };
    }

    static void GenerateSource(SourceProductionContext context, SourceWriter src, GeneratorInput input)
    {
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

        if (input.Symbols.Has(UNITY_EDITOR))
        {
            src.Line.Write("/// <summary>");
            src.Line.Write("/// This is a generated wrapper struct to simplify the name of the combined generic");
            src.Line.Write("/// <see cref=\"ZLinq.ValueEnumerable{TE, T}\"/> constructed from a ZLINQ query.");
            src.Line.Write("/// This makes it easier to define a method that returns the enumerable.");
            src.Line.Write("/// </summary>");
            src.Line.Write("/// <example>");
            src.Line.Write("/// <code>");
            src.Line.Write(wrapEnumerableExample);
            src.Line.Write("/// </code>");
            src.Line.Write("/// </example>");
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
            {
                src.Line.Write($"{input.EnumerableFQN:SplitGenericTypeName} Enumerable {{ get; init; }}");
            }

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
            {
                src.Line.Write($"{input.EnumeratorFQN:SplitGenericTypeName} GetEnumerator() => Enumerable.GetEnumerator();");
            }

            src.Linebreak();

            src.Line.Write("public");
            using (src.Indent)
            {
                src.Line.Write($"{input.EnumerableFQN:SplitGenericTypeName} AsValueEnumerable() => Enumerable;");
            }
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

        var unresolvedIdentifierNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var identifier in unresolvedIdentifiers)
            unresolvedIdentifierNames.Add(identifier.Identifier.ValueText);

        var assignmentTypesByIdentifier = BuildInjectAssignmentTypeMap(model, retExpr, unresolvedIdentifierNames, ct);

        Dictionary<(int Start, int Length), TypeSyntax>? memberAccessCastTypes = null;
        Dictionary<string, TypeSyntax>? identifierCastTypes = null;

        foreach (var identifier in unresolvedIdentifiers)
        {
            if (!TryInferIdentifierType(identifier, assignmentTypesByIdentifier, out var inferredType, out var memberAccessExpressionSyntax))
                continue;

            var castType = SyntaxFactory.ParseTypeName(inferredType.FQN);

            if (memberAccessExpressionSyntax is not null)
            {
                memberAccessCastTypes ??= [];
                var key = (memberAccessExpressionSyntax.SpanStart, memberAccessExpressionSyntax.Span.Length);
                if (!memberAccessCastTypes.ContainsKey(key))
                    memberAccessCastTypes[key] = castType;
            }
            else
            {
                identifierCastTypes ??= new(StringComparer.Ordinal);
                if (!identifierCastTypes.ContainsKey(identifier.Identifier.ValueText))
                    identifierCastTypes[identifier.Identifier.ValueText] = castType;
            }
        }

        if (memberAccessCastTypes is null && identifierCastTypes is null)
            return null;

        ExpressionSyntax expr = retExpr;

        // first pass: apply high-confidence member access fixes and rebind once.
        if (memberAccessCastTypes is not null)
        {
            if (new CastMemberAccessRewriter(memberAccessCastTypes).Visit(expr) is not ExpressionSyntax patchedExpression)
                return null;

            if (!ReferenceEquals(patchedExpression, expr))
            {
                expr = patchedExpression;

                var result = model.GetSpeculativeTypeInfo(retExpr.SpanStart, expr, BindAsExpression).ConvertedType;
                if (result is not (null or IErrorTypeSymbol))
                    return result;
            }
        }

        if (identifierCastTypes is null)
            return null;

        // second pass: batch all identifier casts and rebind once.
        if (new CastRewriter(identifierCastTypes).Visit(expr) is not ExpressionSyntax fullyPatchedExpression)
            return null;

        if (ReferenceEquals(fullyPatchedExpression, expr))
            return null;

        var finalResult = model.GetSpeculativeTypeInfo(retExpr.SpanStart, fullyPatchedExpression, BindAsExpression).ConvertedType;
        return finalResult is not (null or IErrorTypeSymbol)
            ? finalResult
            : null;

        bool TryInferIdentifierType(
            IdentifierNameSyntax identifier,
            IReadOnlyDictionary<string, ITypeSymbol>? cachedAssignmentTypes,
            [NotNullWhen(true)] out ITypeSymbol? inferredType,
            out MemberAccessExpressionSyntax? memberAccessExpressionSyntax
        )
        {
            inferredType = null;
            memberAccessExpressionSyntax = null;

            switch (identifier.Identifier.ValueText)
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

            return cachedAssignmentTypes?.TryGetValue(identifier.Identifier.ValueText, out inferredType) == true;
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

                var identifierName = leftIdentifier.Identifier.ValueText;
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

    sealed class CastMemberAccessRewriter(IReadOnlyDictionary<(int Start, int Length), TypeSyntax> castTypesByMemberAccessSpan) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (!castTypesByMemberAccessSpan.TryGetValue((node.Expression.SpanStart, node.Expression.Span.Length), out var castType))
                return base.VisitMemberAccessExpression(node);

            // build (TrackedInstances<TrackedObject>)TrackedObject.Instances
            var castExpr =
                SyntaxFactory.ParenthesizedExpression(
                    SyntaxFactory.CastExpression(
                        castType,
                        node.Expression
                    )
                );

            // keep the original .Name (AsValueEnumerable, Select, …)
            return node.WithExpression(castExpr);
        }
    }

    sealed class CastRewriter(IReadOnlyDictionary<string, TypeSyntax> castTypesByIdentifier) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
            => castTypesByIdentifier.TryGetValue(node.Identifier.ValueText, out var castType)
                ? SyntaxFactory
                    .ParenthesizedExpression(
                        SyntaxFactory.CastExpression(
                            castType,
                            node.WithoutTrivia()
                        )
                    )
                    .WithTriviaFrom(node)
                : base.VisitIdentifierName(node);
    }
}
