using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Constants;
using static Microsoft.CodeAnalysis.SpeculativeBindingOption;
using static Microsoft.CodeAnalysis.SymbolDisplayFormat;

[Generator]
sealed class WrapValueEnumerableSourceGenerator : BaseSourceGenerator, IIncrementalGenerator
{
    void IIncrementalGenerator.Initialize(IncrementalGeneratorInitializationContext context)
    {
        var wrapRequests =
            context
                .SyntaxProvider
                .ForAttributeWithMetadataName(
                    fullyQualifiedMetadataName: "Medicine.WrapValueEnumerableAttribute",
                    predicate: static (x, ct) => x is MethodDeclarationSyntax or PropertyDeclarationSyntax,
                    transform: WrapTransform(TransformForCache)
                )
                .Select(WrapTransform<CacheInput, GeneratorInput>(Transform));

        context.RegisterSourceOutput(wrapRequests, WrapGenerateSource<GeneratorInput>(GenerateSource));
    }

    [SuppressMessage("ReSharper", "NotAccessedPositionalProperty.Local")]
    record struct CacheInput() : IGeneratorTransformOutputWithContext
    {
        public EquatableIgnore<GeneratorAttributeSyntaxContext> Context { get; set; }
        public string? SourceGeneratorOutputFilename { get; set; }
        public string? SourceGeneratorError { get; set; }
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

    record struct GeneratorInput() : IGeneratorTransformOutput
    {
        public string? SourceGeneratorOutputFilename { get; init; }
        public string? SourceGeneratorError { get; set; }
        public EquatableIgnore<Location> SourceGeneratorErrorLocation { get; set; }

        public EquatableArray<string> Declaration;
        public string? WrapperName;
        public string? EnumerableFQN;
        public string? EnumeratorFQN;
        public string? EnumeratorInnerFQN;
        public string? ElementTypeFQN;
        public string? GetEnumeratorNamespace;
        public bool IsPublic;
        public ImmutableArray<byte> MethodTextChecksumForCache;
    }

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
            return default;
        }

        var retExpr = GetSymbolRetExpr(context.TargetSymbol);

        if (retExpr is null)
            return default;

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

        var innerType = TryGetTypeFromExpression(retExpr);

        if (innerType is null or IErrorTypeSymbol)
        {
            var identifiersToPatch = retExpr
                .DescendantNodesAndSelf()
                .OfType<IdentifierNameSyntax>()
                .ToArray();

            if (identifiersToPatch.Length > 0)
                innerType = ReevaluateWithPatchedIdentifiers(model, retExpr, identifiersToPatch, ct);
        }

        if (innerType is null or IErrorTypeSymbol)
            return new() { SourceGeneratorError = $"Could not get inner type of return expression. {retExpr}" };

        IMethodSymbol? GetInstanceMethod()
            => innerType.GetMembers("GetEnumerator")
                .OfType<IMethodSymbol>()
                .FirstOrDefault(x => x is { IsStatic: false, IsGenericMethod: false, Parameters.Length: 0 });

        IMethodSymbol? GetExtensionMethod()
            => model.LookupSymbols(
                    position: retExpr.SpanStart,
                    container: innerType,
                    name: "GetEnumerator",
                    includeReducedExtensionMethods: true
                )
                .OfType<IMethodSymbol>()
                .FirstOrDefault(x => x is { MethodKind: MethodKind.ReducedExtension, Parameters.Length: 0 });

        var getEnumerator = GetInstanceMethod() ?? GetExtensionMethod();

        if (getEnumerator is null)
            return new() { SourceGeneratorError = "Could not get GetEnumerator method." };

        string? enumeratorNamespace = getEnumerator.IsExtensionMethod
            ? getEnumerator.ContainingNamespace.ToDisplayString()
            : null;

        return new()
        {
            Declaration = Utility.DeconstructTypeDeclaration(declSyntax),
            WrapperName = wrapperName,
            MethodTextChecksumForCache = context.TargetNode.GetText().GetChecksum(),
            EnumerableFQN = innerType.ToDisplayString(FullyQualifiedFormat),
            EnumeratorFQN = getEnumerator.ReturnType.ToDisplayString(FullyQualifiedFormat),
            EnumeratorInnerFQN = (getEnumerator.ReturnType as INamedTypeSymbol)!.TypeArguments.First().ToDisplayString(FullyQualifiedFormat),
            ElementTypeFQN = (innerType as INamedTypeSymbol)!.TypeArguments.Last().ToDisplayString(FullyQualifiedFormat),
            GetEnumeratorNamespace = enumeratorNamespace,
            IsPublic = declSyntax.Modifiers.Any(SyntaxKind.PublicKeyword),
            SourceGeneratorOutputFilename = GetOutputFilename(declSyntax.SyntaxTree.FilePath, wrapperName, context.TargetSymbol.GetFQN()!),
        };
    }

    void GenerateSource(SourceProductionContext context, GeneratorInput input)
    {
        if (input.GetEnumeratorNamespace is not null)
        {
            Line.Append("using ").Append(input.GetEnumeratorNamespace).Append(';');
            Linebreak();
        }

        foreach (var x in input.Declaration.AsSpan())
        {
            Line.Append(x);
            Line.Append('{');
            IncreaseIndent();
        }

        Line.Append("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");

        Line.Append(input.IsPublic ? "public " : "").Append("readonly struct ").Append(input.WrapperName);
        using (Indent)
        {
            Line.Append($": global::ZLinq.IValueEnumerable<");
            using (Indent)
            {
                Line.AppendLongGenericTypeName(this, input.EnumeratorInnerFQN);
                Append(',');
                Line.Append(input.ElementTypeFQN);
                Append(">");
            }
        }

        using (Braces)
        {
            Line.Append("public ");
            using (Indent)
            {
                Line.AppendLongGenericTypeName(this, input.EnumerableFQN);
                Append(" Enumerable { get; init; }");
            }

            Linebreak();
            Line.Append("public static implicit operator ").Append(input.WrapperName).Append('(');
            using (Indent)
            {
                Line.AppendLongGenericTypeName(this, input.EnumerableFQN);
                Append(" Enumerable)");
                using (Indent)
                    Line.Append("=> new() { Enumerable = Enumerable };");
            }

            Linebreak();

            Line.Append("public ");
            using (Indent)
            {
                Text.AppendLongGenericTypeName(this, input.EnumeratorFQN);
                Append(" GetEnumerator()");
                using (Indent)
                    Line.Append("=> Enumerable.GetEnumerator();");
            }

            Linebreak();

            Line.Append("public ");
            using (Indent)
            {
                Text.AppendLongGenericTypeName(this, input.EnumerableFQN);
                Append(" AsValueEnumerable()");
                using (Indent)
                    Line.Append("=> Enumerable;");
            }
        }

        foreach (var x in input.Declaration.AsSpan())
        {
            DecreaseIndent();
            Line.Append('}');
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