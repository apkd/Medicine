using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.SpeculativeBindingOption;
using static Microsoft.CodeAnalysis.SymbolDisplayFormat;

/// <summary>
/// Shared helper for recovering expression type information by applying synthetic cast patches
/// and rebinding the patched expression with Roslyn speculative binding.
/// </summary>
static class SpeculativeTypePatching
{

    /// <param name="BindPosition">
    /// Syntax position used for <see cref="SemanticModel.GetSpeculativeTypeInfo(int, ExpressionSyntax, SpeculativeBindingOption)"/>.
    /// </param>
    /// <param name="ReturnOnFirstSuccessfulPass">
    /// If true, returns as soon as a patch pass yields a valid type; otherwise continues to later passes.
    /// </param>
    public readonly record struct Options(
        int BindPosition,
        bool ReturnOnFirstSuccessfulPass = true
    );

    /// <summary>
    /// Attempts to recover a valid expression type by applying optional member-access and identifier cast patches.
    /// </summary>
    /// <remarks>
    /// Patch order is deterministic: member-access patches first, identifier patches second.
    /// </remarks>
    /// <param name="model">Semantic model used for speculative rebinding.</param>
    /// <param name="expression">Original expression to patch and rebind.</param>
    /// <param name="options">Binding options controlling pass behavior.</param>
    /// <param name="identifierTypesByName">
    /// Optional identifier-to-type map. Each identifier occurrence is cast to the mapped type.
    /// </param>
    /// <param name="memberAccessTypesByExpressionSpan">
    /// Optional map keyed by <c>(SpanStart, SpanLength)</c> of a member-access expression's <c>Expression</c> node.
    /// That expression is cast to the mapped type.
    /// </param>
    /// <returns>A valid rebound type, or <see langword="null"/> if no pass produced one.</returns>
    public static ITypeSymbol? Reevaluate(
        SemanticModel model,
        ExpressionSyntax expression,
        Options options,
        IReadOnlyDictionary<string, ITypeSymbol>? identifierTypesByName = null,
        IReadOnlyDictionary<(int Start, int Length), ITypeSymbol>? memberAccessTypesByExpressionSpan = null
    )
    {
        var current = expression;

        // Pass 1: patch member-access receiver expressions first.
        // This has higher confidence for patterns like `SomeType.Instances.SomeCall()`
        // where fixing the receiver often restores the whole chain.
        if (memberAccessTypesByExpressionSpan is { Count: > 0 })
        {
            var memberAccessCastTypes = new Dictionary<(int Start, int Length), TypeSyntax>(memberAccessTypesByExpressionSpan.Count);
            foreach (var (key, typeSymbol) in memberAccessTypesByExpressionSpan)
                if (typeSymbol is not null and not IErrorTypeSymbol)
                    memberAccessCastTypes[key] = SyntaxFactory.ParseTypeName(typeSymbol.ToDisplayString(FullyQualifiedFormat));

            if (memberAccessCastTypes.Count > 0)
                if (new CastMemberAccessRewriter(memberAccessCastTypes).Visit(current) is ExpressionSyntax patchedExpression)
                    if (!ReferenceEquals(patchedExpression, current))
                    {
                        current = patchedExpression;
                        var result = GetValidType(model, options.BindPosition, current);
                        if (result is not null && options.ReturnOnFirstSuccessfulPass)
                            return result;
                    }
        }

        // Pass 2: patch plain identifiers in a single batch.
        // This minimizes speculative bind calls and keeps output deterministic.
        if (identifierTypesByName is not { Count: > 0 })
            return null;

        var identifierCastTypes = new Dictionary<string, TypeSyntax>(identifierTypesByName.Count, StringComparer.Ordinal);
        foreach (var (identifier, typeSymbol) in identifierTypesByName)
            if (typeSymbol is not null and not IErrorTypeSymbol)
                identifierCastTypes[identifier] = SyntaxFactory.ParseTypeName(typeSymbol.ToDisplayString(FullyQualifiedFormat));

        if (identifierCastTypes.Count is 0)
            return null;

        if (new CastRewriter(identifierCastTypes).Visit(current) is not ExpressionSyntax fullyPatchedExpression)
            return null;

        if (ReferenceEquals(fullyPatchedExpression, current))
            return null;

        return GetValidType(model, options.BindPosition, fullyPatchedExpression);
    }

    /// <summary>
    /// Re-evaluates an expression by patching identifiers using already-resolved identifier types.
    /// </summary>
    /// <param name="model">Semantic model used for speculative rebinding.</param>
    /// <param name="expression">Expression whose identifiers may need cast patching.</param>
    /// <param name="resolvedTypes">
    /// Resolved identifier types keyed by identifier name.
    /// Only identifiers present in <paramref name="expression"/> are used.
    /// </param>
    public static ITypeSymbol? ReevaluateWithResolvedIdentifiers(
        SemanticModel model,
        ExpressionSyntax expression,
        IReadOnlyDictionary<string, ITypeSymbol> resolvedTypes
    )
    {
        Dictionary<string, ITypeSymbol>? identifierTypesByName = null;

        foreach (var nameSyntax in expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            string identifierName = nameSyntax.Text;

            if (identifierTypesByName?.ContainsKey(identifierName) is true)
                continue;

            if (!resolvedTypes.TryGetValue(identifierName, out var type))
                continue;

            identifierTypesByName ??= new(StringComparer.Ordinal);
            identifierTypesByName[identifierName] = type;
        }

        if (identifierTypesByName is null)
            return null;

        return Reevaluate(
            model,
            expression,
            options: new(BindPosition: expression.SpanStart),
            identifierTypesByName: identifierTypesByName
        );
    }

    /// <summary> Performs speculative binding and normalizes invalid results to null. </summary>
    static ITypeSymbol? GetValidType(SemanticModel model, int bindPosition, ExpressionSyntax expression)
    {
        var type = model.GetSpeculativeTypeInfo(bindPosition, expression, BindAsExpression).ConvertedType;
        return type is null or IErrorTypeSymbol
            ? null
            : type;
    }

    /// <summary> Rewrites configured member-access receiver expressions by inserting explicit casts. </summary>
    sealed class CastMemberAccessRewriter(IReadOnlyDictionary<(int Start, int Length), TypeSyntax> castTypesByMemberAccessSpan) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (!castTypesByMemberAccessSpan.TryGetValue((node.Expression.SpanStart, node.Expression.Span.Length), out var castType))
                return base.VisitMemberAccessExpression(node);

            return node.WithExpression(
                SyntaxFactory.ParenthesizedExpression(
                    SyntaxFactory.CastExpression(
                        castType,
                        node.Expression
                    )
                )
            );
        }
    }

    /// <summary> Rewrites identifier expressions by inserting explicit casts. </summary>
    sealed class CastRewriter(IReadOnlyDictionary<string, TypeSyntax> castTypesByIdentifier) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
            => castTypesByIdentifier.TryGetValue(node.Text, out var castType)
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
