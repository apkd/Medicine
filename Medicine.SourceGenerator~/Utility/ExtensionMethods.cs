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

        public bool HasInterface(Func<INamedTypeSymbol, bool> interfacePredicate, bool checkAllInterfaces = true)
        {
            if (self is null)
                return false;

            var interfaces = checkAllInterfaces ? self.AllInterfaces : self.Interfaces;
            foreach (var x in interfaces)
                if (interfacePredicate(x))
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

        public bool IsInMedicineNamespace
            => self.ContainingNamespace is { Name: Constants.Namespace, ContainingNamespace.IsGlobalNamespace: true };
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

        public bool HasAttribute(Func<INamedTypeSymbol, bool> attributePredicate)
        {
            if (self is null)
                return false;

            var attributes = self.GetAttributes();
            foreach (var attribute in attributes)
                if (attribute.AttributeClass is { } attributeClass)
                    if (attributePredicate(attributeClass))
                        return true;

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
        public bool HasAttribute(Func<NameSyntax, bool> predicate)
        {
            if (self is null)
                return false;

            var attributeLists = self.AttributeLists;
            foreach (var attributeList in attributeLists)
            foreach (var attribute in attributeList.Attributes)
                if (predicate(attribute.Name))
                    return true;

            return false;
        }

        public bool HasAttribute(Func<string, bool> predicate)
            => self.HasAttribute((Func<NameSyntax, bool>)(x => predicate(x.ToString())));

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
        public bool HasAttribute(Func<NameSyntax, bool> predicate)
        {
            if (self is null)
                return false;

            foreach (var attributeList in self.AttributeLists)
            foreach (var x in attributeList.Attributes)
                if (predicate(x.Name))
                    return true;

            return false;
        }

        public bool HasAttribute(Func<string, bool> predicate)
            => self.HasAttribute((Func<NameSyntax, bool>)(x => predicate(x.ToString())));

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

    extension(NameSyntax? nameSyntax)
    {
        public bool MatchesQualifiedNamePattern(string fullNamePattern, int namespaceSegments = 0, string skipEnd = "")
        {
            if (nameSyntax is null)
                return false;

            if (string.IsNullOrEmpty(fullNamePattern))
                return false;

            bool hasSkipEnd = !string.IsNullOrEmpty(skipEnd);
            int skipEndLength = skipEnd.Length;

            int patternSegments = 1;
            foreach (char c in fullNamePattern)
                if (c is '.')
                    patternSegments++;

            int nameSegments = CountSegments(nameSyntax);
            if (nameSegments <= 0)
                return false;

            int requiredSegments = patternSegments - namespaceSegments;
            if (nameSegments < requiredSegments || nameSegments > patternSegments)
                return false;

            int patternCursor = fullNamePattern.Length;
            NameSyntax? cursor = nameSyntax;

            for (int i = 0; i < nameSegments; i++)
            {
                if (!TryGetNextSegment(fullNamePattern, ref patternCursor, out int segmentStart, out int segmentLength))
                    return false;

                if (!TryPopRightmostIdentifier(ref cursor, out var identifier))
                    return false;

                if (!SegmentMatches(
                        identifier.ValueText.AsSpan(),
                        fullNamePattern.AsSpan(segmentStart, segmentLength),
                        isRightmostSegment: i == 0,
                        hasSkipEnd: hasSkipEnd,
                        skipEnd: skipEnd,
                        skipEndLength: skipEndLength
                    ))
                    return false;
            }

            return true;

            static int CountSegments(NameSyntax current)
                => current switch
                {
                    QualifiedNameSyntax x      => CountSegments(x.Left) + 1,
                    AliasQualifiedNameSyntax _ => 1,
                    SimpleNameSyntax _         => 1,
                    _                          => 0,
                };

            static bool TryPopRightmostIdentifier(ref NameSyntax? current, out SyntaxToken identifier)
            {
                switch (current)
                {
                    case QualifiedNameSyntax qualified:
                        identifier = qualified.Right.Identifier;
                        current = qualified.Left;
                        return true;

                    case AliasQualifiedNameSyntax aliasQualified:
                        identifier = aliasQualified.Name.Identifier;
                        current = null;
                        return true;

                    case SimpleNameSyntax simple:
                        identifier = simple.Identifier;
                        current = null;
                        return true;

                    default:
                        identifier = default;
                        current = null;
                        return false;
                }
            }

            static bool TryGetNextSegment(string pattern, ref int cursor, out int segmentStart, out int segmentLength)
            {
                if (cursor <= 0)
                {
                    segmentStart = 0;
                    segmentLength = 0;
                    return false;
                }

                int start = cursor - 1;
                while (start >= 0 && pattern[start] != '.')
                    start--;

                segmentStart = start + 1;
                segmentLength = cursor - segmentStart;
                cursor = start;
                return true;
            }

            static bool SegmentMatches(
                ReadOnlySpan<char> identifier,
                ReadOnlySpan<char> segment,
                bool isRightmostSegment,
                bool hasSkipEnd,
                string skipEnd,
                int skipEndLength
            )
            {
                if (identifier.SequenceEqual(segment))
                    return true;

                if (!isRightmostSegment || !hasSkipEnd)
                    return false;

                if (segment.Length <= skipEndLength)
                    return false;

                var segmentTail = segment[(segment.Length - skipEndLength)..];
                if (!segmentTail.SequenceEqual(skipEnd.AsSpan()))
                    return false;

                var segmentWithoutTail = segment[..(segment.Length - skipEndLength)];
                return identifier.SequenceEqual(segmentWithoutTail);
            }
        }
    }

    extension(SyntaxReference? self)
    {
        public Location? GetLocation()
            => self is { SyntaxTree: { } tree, Span: { IsEmpty: false } span }
                ? Location.Create(tree, span)
                : null;
    }

    extension(string self)
    {
        public string HtmlEncode()
            => System.Web.HttpUtility.HtmlEncode(self);
    }

    extension(IEnumerable<string> self)
    {
        public string Join(string separator)
            => string.Join(separator, self);
    }

    extension<T>(ImmutableArray<T> self)
    {
        public T[] AsArray()
            => Unsafe.As<ImmutableArray<T>, ValueTuple<T[]>>(ref self).Item1 ?? [];
    }

    public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value)
        => (key, value) = (pair.Key, pair.Value);
}
