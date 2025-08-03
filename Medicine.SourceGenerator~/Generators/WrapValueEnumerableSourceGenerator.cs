using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static ActivePreprocessorSymbolNames;
using static Constants;
using static Microsoft.CodeAnalysis.SpeculativeBindingOption;
using static Microsoft.CodeAnalysis.SymbolDisplayFormat;

[Generator]
sealed class WrapValueEnumerableSourceGenerator : BaseSourceGenerator, IIncrementalGenerator
{
    void IIncrementalGenerator.Initialize(IncrementalGeneratorInitializationContext context)
    {
        var medicineSettings = context.CompilationProvider
            .Combine(context.ParseOptionsProvider)
            .Select((x, ct) => new MedicineSettings(x));

        var wrapRequests =
            context
                .SyntaxProvider
                .ForAttributeWithMetadataName(
                    fullyQualifiedMetadataName: WrapValueEnumerableAttributeMetadataName,
                    predicate: static (x, ct) => x is MethodDeclarationSyntax or PropertyDeclarationSyntax,
                    transform: WrapTransform(TransformForCache)
                )
                .Select(WrapTransform<CacheInput, GeneratorInput>(Transform))
                .Combine(medicineSettings)
                .Select((x, ct) => x.Left with { Symbols = x.Right.PreprocessorSymbolNames });

        context.RegisterSourceOutput(wrapRequests, WrapGenerateSource<GeneratorInput>(GenerateSource));
    }

    [SuppressMessage("ReSharper", "NotAccessedPositionalProperty.Local")]
    record struct CacheInput : IGeneratorTransformOutputWithContext
    {
        public EquatableIgnore<GeneratorAttributeSyntaxContext> Context { get; set; }
        public string? SourceGeneratorOutputFilename { get; set; }
        public string? SourceGeneratorError { get; init; }
        public EquatableIgnore<Location> SourceGeneratorErrorLocation { get; set; }

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
        public EquatableIgnore<Location> SourceGeneratorErrorLocation { get; set; }

        public EquatableArray<string> Declaration;
        public string? WrapperName;
        public string? EnumerableFQN;
        public string? EnumeratorFQN;
        public string? EnumeratorInnerFQN;
        public string? ElementTypeFQN;
        public string? GetEnumeratorNamespace;
        public bool IsPublic;
        public ActivePreprocessorSymbolNames Symbols;
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

    static GeneratorInput Transform(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
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

        var outputFilename = GetOutputFilename(declSyntax.SyntaxTree.FilePath, wrapperName, context.TargetSymbol.GetFQN()!);
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
                retExprType = ReevaluateWithPatchedIdentifiers(model, retExpr, identifiersToPatch, ct);
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
            MethodTextChecksumForCache = context.TargetNode.GetText().GetChecksum(),
            EnumerableFQN = retExprType.ToDisplayString(FullyQualifiedFormat),
            EnumeratorFQN = getEnumerator.ReturnType.ToDisplayString(FullyQualifiedFormat),
            EnumeratorInnerFQN = (getEnumerator.ReturnType as INamedTypeSymbol)!.TypeArguments.First().ToDisplayString(FullyQualifiedFormat),
            ElementTypeFQN = (retExprType as INamedTypeSymbol)!.TypeArguments.Last().ToDisplayString(FullyQualifiedFormat),
            GetEnumeratorNamespace = enumeratorNamespace,
            IsPublic = declSyntax.Modifiers.Any(SyntaxKind.PublicKeyword),
        };
    }

    void GenerateSource(SourceProductionContext context, GeneratorInput input)
    {
        if (input.GetEnumeratorNamespace is not null)
        {
            Line.Write($"using {input.GetEnumeratorNamespace};");
            Linebreak();
        }

        foreach (var x in input.Declaration.AsSpan())
        {
            Line.Write(x);
            Line.Write('{');
            IncreaseIndent();
        }

        if (input.Symbols.Has(UNITY_EDITOR))
        {
            Line.Write("/// <summary>");
            ;
            Line.Write("/// This is a generated wrapper struct to simplify the name of the combined generic");
            Line.Write("/// <see cref=\"ZLinq.ValueEnumerable{TE, T}\"/> constructed from a ZLINQ query.");
            Line.Write("/// This makes it easier to define a method that returns the enumerable.");
            Line.Write("/// </summary>");
            Line.Write("/// <example>");
            Line.Write("/// <code>");
            Line.Write(wrapEnumerableExample);
            Line.Write("/// </code>");
            Line.Write("/// </example>");
        }

        Line.Write("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        string @public = input.IsPublic ? "public " : "";
        Line.Write($"{@public}readonly struct {input.WrapperName}");
        using (Indent)
        {
            Line.Write($": global::ZLinq.IValueEnumerable<");
            using (Indent)
            {
                Line.Write($"{new LongGenericTypeName(this, input.EnumeratorInnerFQN)},");
                Line.Write($"{input.ElementTypeFQN}>");
            }
        }

        using (Braces)
        {
            Line.Write("public");
            using (Indent)
            {
                Line.Write($"{new LongGenericTypeName(this, input.EnumerableFQN)} Enumerable {{ get; init; }}");
            }

            Linebreak();
            Line.Write("public");
            using (Indent)
            {
                Line.Write($"static implicit operator {input.WrapperName}(");
                using (Indent)
                {
                    Line.Write($"{new LongGenericTypeName(this, input.EnumerableFQN)} enumerable)")
                        .Write(" => new() { Enumerable = enumerable };");
                }
            }

            Linebreak();

            Line.Write("public");
            using (Indent)
            {
                Line.Write($"{new LongGenericTypeName(this, input.EnumeratorFQN)} GetEnumerator()")
                    .Write(" => Enumerable.GetEnumerator();");
            }

            Linebreak();

            Line.Write("public");
            using (Indent)
            {
                Line.Write($"{new LongGenericTypeName(this, input.EnumerableFQN)} AsValueEnumerable()")
                    .Write(" => Enumerable;");
            }
        }

        foreach (var x in input.Declaration.AsSpan())
        {
            DecreaseIndent();
            Line.Write('}');
        }
    }

    static ITypeSymbol? ReevaluateWithPatchedIdentifiers(
        SemanticModel model,
        ExpressionSyntax retExpr,
        IdentifierNameSyntax[] identifiers,
        CancellationToken ct
    )
    {
        (ITypeSymbol?, MemberAccessExpressionSyntax?) TryInferAssignedType(IdentifierNameSyntax identifier)
        {
            var root = model.SyntaxTree.GetRoot(ct);

            switch (identifier.Identifier.ValueText)
            {
                // try to fix "SomeTrackedType.Instances"
                case "Instances":
                {
                    const string trackedStruct = "Medicine.Internal.TrackedInstances`1";
                    if (identifier.Parent is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax typeName } memberAccess)
                        if (model.GetSymbolInfo(typeName, ct).Symbol is INamedTypeSymbol typeSymbol)
                            if (typeSymbol.HasAttribute(TrackAttributeFQN))
                                if (model.Compilation.GetTypeByMetadataName(trackedStruct)?.Construct(typeSymbol) is { } constructed)
                                    return (constructed, memberAccess);

                    break;
                }
                // try to fix "SomeSingletonType.Instance"
                case "Instance":
                {
                    if (identifier.Parent is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax typeName } memberAccess)
                        if (model.GetSymbolInfo(typeName, ct).Symbol is INamedTypeSymbol typeSymbol)
                            if (typeSymbol.HasAttribute(SingletonAttributeFQN))
                                return (typeSymbol, memberAccess);

                    break;
                }
            }

            // try to find an assignment in an [Inject] method
            var assignment = root
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(x => x.HasAttribute(y => y is InjectAttributeNameShort or InjectAttributeName or InjectAttributeMetadataName))
                .SelectMany(x => x.DescendantNodes())
                .OfType<AssignmentExpressionSyntax>()
                .FirstOrDefault(a => a.Left is IdentifierNameSyntax left && left.Identifier.ValueText == identifier.Identifier.ValueText);

            if (assignment?.Right is not null)
            {
                var rhsType = model.GetTypeInfo(assignment.Right, ct).Type;
                if (rhsType is not null and not IErrorTypeSymbol)
                    return (rhsType, null);
            }

            return (null, null);
        }

        SyntaxNode expr = retExpr;

        foreach (var identifier in identifiers)
        {
            var (inferredType, memberAccessExpressionSyntax) = TryInferAssignedType(identifier);

            if (inferredType is null or IErrorTypeSymbol)
                continue;

            expr = memberAccessExpressionSyntax is not null
                ? new CastMemberAccessRewriter(memberAccessExpressionSyntax, inferredType).Visit(expr)
                : new CastRewriter(identifier.Identifier.ValueText, inferredType).Visit(expr);

            var result = model.GetSpeculativeTypeInfo(
                    retExpr.SpanStart,
                    (ExpressionSyntax)expr,
                    BindAsExpression
                )
                .ConvertedType;

            if (result is not (null or IErrorTypeSymbol))
                return result;
        }

        return null;
    }

    sealed class CastMemberAccessRewriter(MemberAccessExpressionSyntax memberAccessExpression, ITypeSymbol type) : CSharpSyntaxRewriter
    {
        readonly string typeName = type.ToDisplayString(FullyQualifiedFormat);

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (node.Expression.IsEquivalentTo(memberAccessExpression))
            {
                // build (TrackedInstances<TrackedObject>)TrackedObject.Instances
                var castExpr =
                    SyntaxFactory.ParenthesizedExpression(
                        SyntaxFactory.CastExpression(
                            SyntaxFactory.ParseTypeName(typeName),
                            node.Expression
                        )
                    );

                // keep the original .Name (AsValueEnumerable, Select, …)
                return node.WithExpression(castExpr);
            }

            return base.VisitMemberAccessExpression(node);
        }
    }

    sealed class CastRewriter(string identifier, ITypeSymbol type) : CSharpSyntaxRewriter
    {
        readonly string typeName = type.ToDisplayString(FullyQualifiedFormat);

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
            => node.Identifier.ValueText == identifier
                ? SyntaxFactory
                    .ParenthesizedExpression(
                        SyntaxFactory.CastExpression(
                            SyntaxFactory.ParseTypeName(typeName),
                            node.WithoutTrivia()
                        )
                    )
                    .WithTriviaFrom(node)
                : base.VisitIdentifierName(node);
    }
}