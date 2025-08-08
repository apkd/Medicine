using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static System.StringComparison;
using static Microsoft.CodeAnalysis.SymbolDisplayFormat;

[EditorBrowsable(EditorBrowsableState.Never)]
[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
public static partial class ExtensionMethods
{
    public static string Join(this IEnumerable<string> self, string separator)
        => string.Join(separator, self);

    public static string HtmlEncode(this string self)
        => System.Web.HttpUtility.HtmlEncode(self);

    public static bool Is(this ISymbol? self, string fullyQualifiedName)
        => fullyQualifiedName.Equals(self.GetFQN(), Ordinal);

    public static bool Is(this ISymbol? self, ISymbol? other)
        => SymbolEqualityComparer.Default.Equals(self, other);

    public static IEnumerable<T> SelectRecursive<T>(this T node, Func<T, T?> selector)
    {
        while (selector(node) is { } newNode)
            yield return node = newNode;
    }

    public static T[] AsArray<T>(this ImmutableArray<T> array)
        => Unsafe.As<ImmutableArray<T>, ValueTuple<T[]>>(ref array).Item1;

    public static string? GetSafeSymbolName(this ISymbol? symbol, SemanticModel model, int position)
    {
        if (symbol is null)
            return null;

        var minimal = symbol.ToDisplayString(MinimallyQualifiedFormat);
        var expr = SyntaxFactory.ParseTypeName(minimal);
        var typeInfo = model.GetSpeculativeTypeInfo(position, expr, SpeculativeBindingOption.BindAsTypeOrNamespace);
        var resolved = typeInfo.Type;

        if (resolved is IArrayTypeSymbol resolvedArray && symbol is IArrayTypeSymbol originalArray)
        {
            if (resolvedArray.ElementType.Is(originalArray.ElementType))
                return minimal;
        }
        else if (resolved.Is(symbol))
        {
            return minimal;
        }

        return symbol.ToDisplayString(FullyQualifiedFormat);
    }

    public static SyntaxNode RewriteThisAndFullyQualityReferences(
        this SyntaxNode node,
        string replacement,
        SemanticModel semanticModel,
        CancellationToken ct
    )
    {
        return node.ReplaceNodes(
            nodes: node.DescendantNodesAndSelf(),
            computeReplacementNode: (original, rewritten) => rewritten switch
            {
                // explicit this
                ThisExpressionSyntax
                    => SyntaxFactory.IdentifierName(replacement),

                // implicit this: analyze the original node
                SimpleNameSyntax nameSyntax when original is SimpleNameSyntax originalName && IsImplicitThisMemberAccess(originalName) =>
                    SyntaxFactory.MemberAccessExpression(
                        kind: SyntaxKind.SimpleMemberAccessExpression,
                        expression: SyntaxFactory.IdentifierName(replacement),
                        name: nameSyntax // but use the rewritten node in the new structure
                    ),

                // leave identifiers that bind to parameters (named-argument labels) untouched
                SimpleNameSyntax nameSyntax when semanticModel.GetSymbolInfo(original, ct).Symbol is IParameterSymbol
                    => nameSyntax,

                // fully qualify other names
                SimpleNameSyntax nameSyntax
                    => SyntaxFactory.IdentifierName(
                        semanticModel.GetSymbolInfo(original, ct).Symbol?.GetFQN() ?? nameSyntax.Identifier.ValueText
                    ),

                _ => rewritten,
            }
        );

        bool IsImplicitThisMemberAccess(SimpleNameSyntax nameSyntax)
        {
            if (nameSyntax.Parent is MemberAccessExpressionSyntax memberAccess)
                if (memberAccess.Name == nameSyntax)
                    return false; // already has an explicit receiver

            return semanticModel.GetSymbolInfo(nameSyntax, ct).Symbol
                is (IMethodSymbol or IPropertySymbol or IFieldSymbol)
                and { IsStatic: false, ContainingType: not null };
        }
    }

    public static SyntaxNode WithFullyQualitiedReferences(this SyntaxNode node, SemanticModel semanticModel, CancellationToken ct)
        => node.ReplaceNodes(
            nodes: node.DescendantNodesAndSelf(),
            computeReplacementNode: (original, rewritten)
                => rewritten is SimpleNameSyntax && semanticModel.GetSymbolInfo(original, ct).Symbol?.GetFQN() is { } qualifiedName
                    ? SyntaxFactory.IdentifierName(qualifiedName)
                    : rewritten
        );

    public static IEnumerable<ITypeSymbol> GetBaseTypes(this ITypeSymbol? self)
        => self != null ? self.SelectRecursive(x => x.BaseType) : [];

    public static string? GetFQN(this ISymbol? self)
        => self?.ToDisplayString(FullyQualifiedFormat);

    public static bool InheritsFrom(this ITypeSymbol? self, string baseTypeFullyQualifiedName)
        => self is not null && self.IsReferenceType && self.GetBaseTypes().Any(x => x.Is(baseTypeFullyQualifiedName));

    public static bool HasAttribute(this ISymbol? self, string attributeFullyQualifiedName)
        => self?.GetAttributes().Any(x => x.AttributeClass.Is(attributeFullyQualifiedName)) is true;

    public static bool HasAttribute(this ISymbol? self, Func<string, bool> attributeFullyQualifiedNamePredicate)
        => self?.GetAttributes().Select(x => x.AttributeClass.GetFQN() ?? "").Any(attributeFullyQualifiedNamePredicate) is true;

    public static AttributeData? GetAttribute(this ISymbol? self, string attributeFullyQualifiedName)
        => self?.GetAttributes().FirstOrDefault(x => x.AttributeClass.Is(attributeFullyQualifiedName));

    public static bool HasAttribute(this MemberDeclarationSyntax? self, Func<string, bool> predicate)
        => self?.AttributeLists.SelectMany(x => x.Attributes.Select(x => x.Name.ToString())).Any(predicate) is true;

    public static AttributeSyntax? GetAttribute(this MemberDeclarationSyntax? self, Func<string, bool> predicate)
        => self?.AttributeLists.SelectMany(x => x.Attributes).FirstOrDefault(x => predicate(x.Name.ToString()));

    public static bool HasInterface(this ITypeSymbol? self, string interfaceFullyQualifiedName, bool checkAllInterfaces = true)
        => (checkAllInterfaces ? self?.AllInterfaces : self?.Interfaces)?.Any(x => x.Is(interfaceFullyQualifiedName)) is true;

    public static Location? GetLocation(this SyntaxReference? syntaxReference)
        => syntaxReference is { SyntaxTree: { } tree, Span: { } span }
            ? Location.Create(tree, span)
            : null;
}