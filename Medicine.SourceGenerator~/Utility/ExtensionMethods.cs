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
    extension(ITypeSymbol? self)
    {
        public bool InheritsFrom(string baseTypeFullyQualifiedName)
        {
            if (self is null || !self.IsReferenceType)
                return false;

            for (var x = self.BaseType; x is not null; x = x.BaseType)
                if (x.Is(baseTypeFullyQualifiedName))
                    return true;

            return false;
        }

        public bool InheritsFrom(ITypeSymbol baseType)
        {
            if (self is null || !self.IsReferenceType)
                return false;

            for (var x = self.BaseType; x is not null; x = x.BaseType)
                if (x.Is(baseType))
                    return true;

            return false;
        }

        public IEnumerable<ITypeSymbol> GetBaseTypes()
        {
            if (self is not null)
                for (var x = self.BaseType; x is not null; x = x.BaseType)
                    yield return x;
        }

        public bool HasInterface(string interfaceFullyQualifiedName, bool checkAllInterfaces = true)
        {
            if (self is null)
                return false;

            var interfaces = checkAllInterfaces ? self.AllInterfaces : self.Interfaces;
            foreach (var x in interfaces)
                if (x.Is(interfaceFullyQualifiedName))
                    return true;

            return false;
        }

        public bool HasInterface(ISymbol? interfaceType, bool checkAllInterfaces = true)
        {
            if (self is null || interfaceType is null)
                return false;

            var interfaces = checkAllInterfaces ? self.AllInterfaces : self.Interfaces;
            foreach (var x in interfaces)
                if (x.Is(interfaceType))
                    return true;

            return false;
        }
    }

    extension(ISymbol self)
    {
        public string FQN
            => self.ToDisplayString(FullyQualifiedFormat);

        public int Hash
            => SymbolEqualityComparer.Default.GetHashCode(self);
    }

    extension(ISymbol? self)
    {
        public bool Is(ISymbol other)
            => SymbolEqualityComparer.Default.Equals(self, other);

        public bool Is(string fqn)
            => self?.FQN.Equals(fqn, Ordinal) ?? false;

        public bool HasAttribute(string attributeFullyQualifiedName)
        {
            if (self is null)
                return false;

            var attributes = self.GetAttributes();
            foreach (var x in attributes)
                if (x.AttributeClass.Is(attributeFullyQualifiedName))
                    return true;

            return false;
        }

        public bool HasAttribute(ISymbol? attributeSymbol)
        {
            if (self is null || attributeSymbol is null)
                return false;

            var attributes = self.GetAttributes();
            foreach (var x in attributes)
                if (x.AttributeClass.Is(attributeSymbol))
                    return true;

            return false;
        }

        public bool HasAttribute(Func<string, bool> attributeFullyQualifiedNamePredicate)
        {
            if (self is null)
                return false;

            var attributes = self.GetAttributes();
            foreach (var attribute in attributes)
            {
                string fqn = attribute.AttributeClass?.FQN ?? "";
                if (attributeFullyQualifiedNamePredicate(fqn))
                    return true;
            }

            return false;
        }

        public AttributeData? GetAttribute(string attributeFullyQualifiedName)
        {
            if (self is null)
                return null;

            var attributes = self.GetAttributes();
            foreach (var x in attributes)
                if (x.AttributeClass.Is(attributeFullyQualifiedName))
                    return x;

            return null;
        }

        public AttributeData? GetAttribute(ISymbol? attributeSymbol)
        {
            if (self is null || attributeSymbol is null)
                return null;

            var attributes = self.GetAttributes();
            foreach (var x in attributes)
                if (x.AttributeClass.Is(attributeSymbol))
                    return x;

            return null;
        }

        public string? GetSafeSymbolName(SemanticModel model, int position)
        {
            if (self is null)
                return null;

            var minimal = self.ToDisplayString(MinimallyQualifiedFormat);
            var expr = SyntaxFactory.ParseTypeName(minimal);
            var typeInfo = model.GetSpeculativeTypeInfo(position, expr, SpeculativeBindingOption.BindAsTypeOrNamespace);
            var resolved = typeInfo.Type;

            if (resolved is IArrayTypeSymbol resolvedArray && self is IArrayTypeSymbol originalArray)
            {
                if (resolvedArray.ElementType.Is(originalArray.ElementType))
                    return minimal;
            }
            else if (resolved.Is(self))
            {
                return minimal;
            }

            return self.ToDisplayString(FullyQualifiedFormat);
        }
    }

    extension(MemberDeclarationSyntax? self)
    {
        public bool HasAttribute(Func<string, bool> predicate)
        {
            if (self is null)
                return false;

            var attributeLists = self.AttributeLists;
            foreach (var attributeList in attributeLists)
            {
                foreach (var attribute in attributeList.Attributes)
                    if (predicate(attribute.Name.ToString()))
                        return true;
            }

            return false;
        }

        public AttributeSyntax? GetAttribute(Func<string, bool> predicate)
        {
            if (self is null)
                return null;

            foreach (var attributeList in self.AttributeLists)
            foreach (var x in attributeList.Attributes)
                if (predicate(x.Name.ToString()))
                    return x;

            return null;
        }
    }

    extension(LocalFunctionStatementSyntax? self)
    {
        public bool HasAttribute(Func<string, bool> predicate)
        {
            if (self is null)
                return false;

            foreach (var attributeList in self.AttributeLists)
            foreach (var x in attributeList.Attributes)
                if (predicate(x.Name.ToString()))
                    return true;

            return false;
        }

        public AttributeSyntax? GetAttribute(Func<string, bool> predicate)
        {
            if (self is null)
                return null;

            foreach (var attributeList in self.AttributeLists)
            foreach (var x in attributeList.Attributes)
                if (predicate(x.Name.ToString()))
                    return x;

            return null;
        }
    }

    extension(SyntaxNode node)
    {
        public SyntaxNode RewriteThisAndFullyQualityReferences(
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
                            semanticModel.GetSymbolInfo(original, ct).Symbol?.FQN ?? nameSyntax.Identifier.ValueText
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

        public SyntaxNode WithFullyQualifiedReferences(SemanticModel semanticModel, CancellationToken ct)
            => node.ReplaceNodes(
                nodes: node.DescendantNodesAndSelf(),
                computeReplacementNode: (original, rewritten)
                    => rewritten is SimpleNameSyntax && semanticModel.GetSymbolInfo(original, ct).Symbol?.FQN is { } qualifiedName
                        ? SyntaxFactory.IdentifierName(qualifiedName)
                        : rewritten
            );
    }

    extension(string self)
    {
        public string HtmlEncode()
            => System.Web.HttpUtility.HtmlEncode(self);
    }

    public static string Join(this IEnumerable<string> self, string separator)
        => string.Join(separator, self);

    public static IEnumerable<T> SelectRecursive<T>(this T node, Func<T, T?> selector)
    {
        while (selector(node) is { } newNode)
            yield return node = newNode;
    }

    public static T[] AsArray<T>(this ImmutableArray<T> array)
        => Unsafe.As<ImmutableArray<T>, ValueTuple<T[]>>(ref array).Item1 ?? [];

    public static Location? GetLocation(this SyntaxReference? syntaxReference)
        => syntaxReference is { SyntaxTree: { } tree, Span: { IsEmpty: false } span }
            ? Location.Create(tree, span)
            : null;

    public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value)
        => (key, value) = (pair.Key, pair.Value);
}
