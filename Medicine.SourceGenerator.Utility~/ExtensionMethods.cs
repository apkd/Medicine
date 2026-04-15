using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using static System.StringComparison;
using static Microsoft.CodeAnalysis.SymbolDisplayFormat;

/// <summary>
/// Roslyn and collection helpers used across the source-generator codebase.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
public static partial class ExtensionMethods
{
    extension(ITypeSymbol? self)
    {
        /// <summary>
        /// Returns whether the type inherits from the specified fully qualified base type.
        /// </summary>
        /// <param name="baseTypeFullyQualifiedName">Fully qualified base type name to match.</param>
        /// <returns><c>true</c> when the current type derives from the specified base type.</returns>
        public bool InheritsFrom(string baseTypeFullyQualifiedName)
        {
            if (self is null || !self.IsReferenceType)
                return false;

            for (var x = self.BaseType; x is not null; x = x.BaseType)
                if (x.Is(baseTypeFullyQualifiedName))
                    return true;

            return false;
        }

        /// <summary>
        /// Returns whether the type inherits from the specified base type.
        /// </summary>
        /// <param name="baseType">Base type symbol to match.</param>
        /// <returns><c>true</c> when the current type derives from <paramref name="baseType"/>.</returns>
        public bool InheritsFrom(ITypeSymbol baseType)
        {
            if (self is null || !self.IsReferenceType)
                return false;

            for (var x = self.BaseType; x is not null; x = x.BaseType)
                if (x.Is(baseType))
                    return true;

            return false;
        }

        /// <summary>
        /// Enumerates the base types of the current type from nearest to farthest.
        /// </summary>
        /// <returns>The base type chain.</returns>
        public IEnumerable<ITypeSymbol> GetBaseTypes()
        {
            if (self is not null)
                for (var x = self.BaseType; x is not null; x = x.BaseType)
                    yield return x;
        }

        /// <summary>
        /// Returns whether the type implements an interface matched by the predicate.
        /// </summary>
        /// <param name="interfacePredicate">Predicate applied to candidate interfaces.</param>
        /// <param name="checkAllInterfaces">
        /// Whether to search the full inherited interface closure instead of only directly declared interfaces.
        /// </param>
        /// <returns><c>true</c> when a matching interface is found.</returns>
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

        /// <summary>
        /// Returns whether the type implements the specified interface symbol.
        /// </summary>
        /// <param name="interfaceType">Interface symbol to match.</param>
        /// <param name="checkAllInterfaces">
        /// Whether to search the full inherited interface closure instead of only directly declared interfaces.
        /// </param>
        /// <returns><c>true</c> when the interface is found.</returns>
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

    extension(INamedTypeSymbol self)
    {
        /// <summary>
        /// Original generic definition for a constructed generic type, or the symbol itself otherwise.
        /// </summary>
        public INamedTypeSymbol OriginalDefinitionOrSelf
            => self is { IsGenericType: true, IsUnboundGenericType: false }
                ? self.OriginalDefinition
                : self;
    }

    extension(ISymbol self)
    {
        /// <summary>
        /// Fully qualified display name for the symbol.
        /// </summary>
        public string FQN
            => self.ToDisplayString(FullyQualifiedFormat);

        /// <summary>
        /// Hash code produced by <see cref="SymbolEqualityComparer.Default"/>.
        /// </summary>
        public int Hash
            => SymbolEqualityComparer.Default.GetHashCode(self);

        /// <summary>
        /// Returns whether the symbol is visible to generated code in the same assembly.
        /// </summary>
        public bool IsAccessible
            => self.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal;
    }

    extension(ISymbol? self)
    {
        /// <summary>
        /// Computes a checksum over the declaration headers in the type hierarchy.
        /// </summary>
        /// <returns>A combined checksum, or <c>0</c> when the symbol is not a type.</returns>
        public ulong GetDeclarationHierarchyChecksum(CancellationToken ct)
        {
            if (self is not ITypeSymbol typeSymbol)
                return 0;

            ulong combinedHash = 0;
            foreach (var syntaxReference in self.DeclaringSyntaxReferences)
                if (syntaxReference.GetSyntax(ct) is TypeDeclarationSyntax decl)
                    XorHash(ref combinedHash, decl.GetDeclarationChecksum(ct));

            switch (typeSymbol.TypeKind)
            {
                case TypeKind.Class:
                {
                    for (var baseType = typeSymbol.BaseType; baseType is not null; baseType = baseType.BaseType)
                        foreach (var syntaxReference in baseType.DeclaringSyntaxReferences)
                            if (syntaxReference.GetSyntax(ct) is TypeDeclarationSyntax decl)
                                XorHash(ref combinedHash, decl.GetDeclarationChecksum(ct));

                    goto default;
                }
                default:
                {
                    foreach (var inheritedInterface in typeSymbol.AllInterfaces)
                    foreach (var syntaxReference in inheritedInterface.DeclaringSyntaxReferences)
                        if (syntaxReference.GetSyntax(ct) is TypeDeclarationSyntax decl)
                            XorHash(ref combinedHash, decl.GetDeclarationChecksum(ct));

                    break;
                }
            }

            return combinedHash;

            static void XorHash(ref ulong accumulator, ulong hash)
                => accumulator ^= hash;
        }

        /// <summary>
        /// Returns whether two symbols are equal under Roslyn symbol equality.
        /// </summary>
        /// <param name="other">Symbol to compare.</param>
        /// <returns><c>true</c> when both symbols represent the same symbol.</returns>
        public bool Is(ISymbol other)
            => SymbolEqualityComparer.Default.Equals(self, other);

        /// <summary>
        /// Returns whether the symbol's fully qualified name matches the supplied text.
        /// </summary>
        /// <param name="fqn">Fully qualified symbol name to compare.</param>
        /// <returns><c>true</c> when the names match ordinally.</returns>
        public bool Is(string fqn)
            => self?.FQN.Equals(fqn, Ordinal) ?? false;

        /// <summary>
        /// Returns whether the symbol is annotated with the specified attribute type.
        /// </summary>
        /// <param name="attributeFullyQualifiedName">Fully qualified attribute type name.</param>
        /// <returns><c>true</c> when a matching attribute is present.</returns>
        public bool HasAttribute(string attributeFullyQualifiedName)
        {
            if (self is null)
                return false;

            foreach (var x in self.GetAttributes())
                if (x.AttributeClass.Is(attributeFullyQualifiedName))
                    return true;

            return false;
        }

        /// <summary>
        /// Returns whether the symbol is annotated with the specified attribute symbol.
        /// </summary>
        /// <param name="attributeSymbol">Attribute symbol to match.</param>
        /// <returns><c>true</c> when a matching attribute is present.</returns>
        public bool HasAttribute(ISymbol? attributeSymbol)
        {
            if (self is null || attributeSymbol is null)
                return false;

            foreach (var x in self.GetAttributes())
                if (x.AttributeClass.Is(attributeSymbol))
                    return true;

            return false;
        }

        /// <summary>
        /// Returns whether the symbol has an attribute matched by the predicate.
        /// </summary>
        /// <param name="attributePredicate">Predicate applied to attribute classes.</param>
        /// <returns><c>true</c> when a matching attribute is present.</returns>
        public bool HasAttribute(Func<INamedTypeSymbol, bool> attributePredicate)
        {
            if (self is null)
                return false;

            foreach (var attribute in self.GetAttributes())
                if (attribute.AttributeClass is { } attributeClass)
                    if (attributePredicate(attributeClass))
                        return true;

            return false;
        }

        /// <summary>
        /// Returns the first attribute whose class matches the specified fully qualified name.
        /// </summary>
        /// <param name="attributeFullyQualifiedName">Fully qualified attribute type name.</param>
        /// <returns>The matching attribute, or <c>null</c> when no match exists.</returns>
        public AttributeData? GetAttribute(string attributeFullyQualifiedName)
        {
            if (self is null)
                return null;

            foreach (var x in self.GetAttributes())
                if (x.AttributeClass.Is(attributeFullyQualifiedName))
                    return x;

            return null;
        }

        /// <summary>
        /// Returns the first attribute whose class matches the specified attribute symbol.
        /// </summary>
        /// <param name="attributeSymbol">Attribute symbol to match.</param>
        /// <returns>The matching attribute, or <c>null</c> when no match exists.</returns>
        public AttributeData? GetAttribute(ISymbol? attributeSymbol)
        {
            if (self is null || attributeSymbol is null)
                return null;

            foreach (var x in self.GetAttributes())
                if (x.AttributeClass.Is(attributeSymbol))
                    return x;

            return null;
        }

        /// <summary>
        /// Returns a minimally qualified type name that still binds correctly at the given position.
        /// </summary>
        /// <param name="model">Semantic model used for speculative binding.</param>
        /// <param name="position">Position used for speculative binding.</param>
        /// <returns>A minimally qualified name, or a fully qualified fallback when required.</returns>
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
        /// <summary>
        /// Returns the full name of the member, including the namespace and containing type name(s).
        /// </summary>
        public string FullName
        {
            get
            {
                using var c1 = Scratch.RentA<List<string>>(out var segments);

                for (SyntaxNode? current = self; current is not null; current = current.Parent)
                    if (current.ShortName is { Length: > 0 } segment)
                        segments.Add(segment);

                if (segments.Count is 0)
                    return "";

                segments.Reverse();
                return string.Join(".", segments);
            }
        }

        /// <summary>
        /// Returns whether the member has an attribute whose syntax name matches the predicate.
        /// </summary>
        /// <param name="predicate">Predicate applied to attribute names.</param>
        /// <returns><c>true</c> when a matching attribute is present.</returns>
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

        /// <summary>
        /// Returns whether the member has an attribute whose textual name matches the predicate.
        /// </summary>
        /// <param name="predicate">Predicate applied to the attribute name text.</param>
        /// <returns><c>true</c> when a matching attribute is present.</returns>
        public bool HasAttribute(Func<string, bool> predicate)
            => self.HasAttribute((Func<NameSyntax, bool>)(x => predicate(x.ToString())));

        /// <summary>
        /// Computes a checksum over the member's attribute list text.
        /// </summary>
        /// <returns>A checksum, or <c>0</c> when the member has no attributes.</returns>
        public ulong GetAttributeListChecksum(CancellationToken ct)
        {
            if (self?.AttributeLists is not { Count: > 0 } lists)
                return 0;

            var span = TextSpan.FromBounds(lists[0].FullSpan.Start, lists[^1].FullSpan.End);

            return self
                .SyntaxTree
                .GetText(ct)
                .GetSubText(span)
                .CalculateChecksum64();
        }
    }

    extension(PropertyDeclarationSyntax? property)
    {
        /// <summary>
        /// Returns whether the property is auto-implemented.
        /// </summary>
        public bool IsAutoProperty
        {
            get
            {
                if (property is not { ExpressionBody: null, AccessorList.Accessors: { Count: > 0 } accessors })
                    return false;

                foreach (var accessor in accessors)
                    if (accessor is not { Body: null, ExpressionBody: null })
                        return false;

                return true;
            }
        }
    }

    extension(BaseTypeDeclarationSyntax? self)
    {
        /// <summary>
        /// Computes a checksum over the declaration header for a type declaration.
        /// </summary>
        /// <returns>A checksum, or <c>0</c> when there is no relevant declaration text.</returns>
        public ulong GetDeclarationChecksum(CancellationToken ct)
        {
            if (self is null)
                return 0;

            int start = int.MaxValue;
            int end = int.MinValue;

            if (self.AttributeLists.Count > 0)
            {
                start = self.AttributeLists[0].FullSpan.Start;
                end = self.AttributeLists[^1].FullSpan.End;
            }

            if (self.BaseList is { } baseList)
            {
                if (baseList.FullSpan.Start < start)
                    start = baseList.FullSpan.Start;

                if (baseList.FullSpan.End > end)
                    end = baseList.FullSpan.End;
            }

            if (start > end)
                return 0;

            var span = TextSpan.FromBounds(start, end);

            return self
                .SyntaxTree
                .GetText(ct)
                .GetSubText(span)
                .CalculateChecksum64();
        }
    }

    extension(LocalFunctionStatementSyntax? self)
    {
        /// <summary>
        /// Returns whether the local function has an attribute whose syntax name matches the predicate.
        /// </summary>
        /// <param name="predicate">Predicate applied to attribute names.</param>
        /// <returns><c>true</c> when a matching attribute is present.</returns>
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
    }

    extension(SyntaxNode self)
    {
        /// <summary>
        /// Short identifier-like name extracted from the syntax node, when available.
        /// </summary>
        public string? ShortName
        {
            get
            {
                return self switch
                {
                    BaseNamespaceDeclarationSyntax ns          => ns.Name.ToString(),
                    BaseTypeDeclarationSyntax type             => type.Identifier.ValueText,
                    MethodDeclarationSyntax method             => method.Identifier.ValueText,
                    ConstructorDeclarationSyntax ctor          => ctor.Identifier.ValueText,
                    DestructorDeclarationSyntax dtor           => $"~{dtor.Identifier.ValueText}",
                    PropertyDeclarationSyntax property         => property.Identifier.ValueText,
                    EventDeclarationSyntax @event              => @event.Identifier.ValueText,
                    EventFieldDeclarationSyntax @event         => GetVariableName(@event.Declaration),
                    FieldDeclarationSyntax @field              => GetVariableName(@field.Declaration),
                    LocalFunctionStatementSyntax localFunction => localFunction.Identifier.ValueText,
                    _                                          => null,
                };

                static string? GetVariableName(VariableDeclarationSyntax? declaration)
                    => declaration is { Variables.Count: > 0 } ? declaration.Variables[0].Identifier.ValueText : null;
            }
        }

        /// <summary>
        /// Computes a checksum over the full text of the syntax node.
        /// </summary>
        /// <returns>A checksum of the node's full span.</returns>
        public ulong GetNodeChecksum(CancellationToken ct)
            => self
                .SyntaxTree
                .GetText(ct)
                .GetSubText(self.FullSpan)
                .CalculateChecksum64();

        /// <summary>
        /// Rewrites <c>this</c> accesses to a replacement identifier and fully qualifies other referenced names.
        /// </summary>
        /// <param name="replacement">Identifier text that replaces <c>this</c>.</param>
        /// <param name="semanticModel">Semantic model used to resolve symbol names.</param>
        /// <returns>A rewritten syntax node.</returns>
        public SyntaxNode RewriteThisAndFullyQualityReferences(
            string replacement,
            SemanticModel semanticModel,
            CancellationToken ct
        )
        {
            return self.ReplaceNodes(
                nodes: self.DescendantNodesAndSelf(),
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
                            semanticModel.GetSymbolInfo(original, ct).Symbol?.FQN ?? nameSyntax.Text
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

        /// <summary>
        /// Rewrites every resolvable simple name as a fully qualified reference.
        /// </summary>
        /// <param name="semanticModel">Semantic model used to resolve symbol names.</param>
        /// <returns>A rewritten syntax node.</returns>
        public SyntaxNode WithFullyQualifiedReferences(SemanticModel semanticModel, CancellationToken ct)
            => self.ReplaceNodes(
                nodes: self.DescendantNodesAndSelf(),
                computeReplacementNode: (original, rewritten)
                    => rewritten is SimpleNameSyntax && semanticModel.GetSymbolInfo(original, ct).Symbol?.FQN is { } qualifiedName
                        ? SyntaxFactory.IdentifierName(qualifiedName)
                        : rewritten
            );
    }

    extension(NameSyntax? nameSyntax)
    {
        /// <summary>
        /// Returns whether the name matches a qualified-name pattern from right to left.
        /// </summary>
        /// <param name="fullNamePattern">Full pattern to match.</param>
        /// <param name="namespaceSegments">
        /// Number of leading pattern segments that may be omitted from the candidate name.
        /// </param>
        /// <param name="skipEnd">
        /// Optional suffix that may be ignored on the rightmost pattern segment.
        /// </param>
        /// <returns><c>true</c> when the pattern matches.</returns>
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

                var segmentTail = segment[^skipEndLength..];
                if (!segmentTail.SequenceEqual(skipEnd.AsSpan()))
                    return false;

                var segmentWithoutTail = segment[..^skipEndLength];
                return identifier.SequenceEqual(segmentWithoutTail);
            }
        }
    }

    extension(SimpleNameSyntax nameSyntax)
    {
        /// <summary>
        /// Identifier text for the simple name.
        /// </summary>
        public string Text
            => nameSyntax.Identifier.ValueText;
    }

    extension(NameSyntax nameSyntax)
    {
        /// <summary>
        /// Rightmost identifier text for the name.
        /// </summary>
        public string? Text
            => nameSyntax switch
            {
                SimpleNameSyntax x         => x.Text,
                QualifiedNameSyntax x      => x.Right.Text,
                AliasQualifiedNameSyntax x => x.Name.Text,
                _                          => null,
            };
    }

    extension(SyntaxReference? self)
    {
        /// <summary>
        /// Creates a Roslyn <see cref="Location"/> from the syntax reference.
        /// </summary>
        /// <returns>A location for the referenced span, or <c>null</c> when unavailable.</returns>
        public Location? GetLocation()
            => self is { SyntaxTree: { } tree, Span: { IsEmpty: false } span }
                ? Location.Create(tree, span)
                : null;
    }

    extension(string self)
    {
        /// <summary>
        /// HTML-encodes the string.
        /// </summary>
        /// <returns>The encoded string.</returns>
        public string HtmlEncode()
            => System.Web.HttpUtility.HtmlEncode(self);

        /// <summary>
        /// Returns the type name after the last namespace or containing-type separator.
        /// </summary>
        /// <returns>The trailing type name segment.</returns>
        public string GetTypeNameTail()
        {
            if (string.IsNullOrEmpty(self))
                return self;

            int tailStart = self.GetTypeNameTailStartIndex();
            return tailStart is 0
                ? self
                : self[tailStart..];
        }

        /// <summary>
        /// Returns the index where the trailing type name segment begins.
        /// </summary>
        /// <returns>The start index of the final type-name segment.</returns>
        public int GetTypeNameTailStartIndex()
        {
            if (string.IsNullOrEmpty(self))
                return 0;

            int genericDepth = 0;
            for (int i = self.Length - 1; i >= 0; i--)
            {
                switch (self[i])
                {
                    case '>':
                        genericDepth++;
                        break;
                    case '<':
                        if (genericDepth > 0)
                            genericDepth--;
                        break;
                    case '.' when genericDepth is 0:
                    case ':' when genericDepth is 0 && i > 0 && self[i - 1] is ':':
                        return i + 1;
                }
            }

            return 0;
        }

        /// <summary>
        /// Returns a documentation-friendly type name without <c>global::</c> prefixes.
        /// </summary>
        /// <returns>An HTML-encoded display name.</returns>
        public string GetTypeDisplayNameForDocs()
        {
            if (string.IsNullOrEmpty(self))
                return self;

            var source = self.AsSpan(self.GetTypeNameTailStartIndex());
            if (source.Length is 0)
                return string.Empty;

            var result = source.Length <= 1024
                ? stackalloc char[source.Length]
                : new char[source.Length];

            int i = 0;
            for (int j = 0; j < source.Length;)
            {
                if (HasGlobalPrefix(source, j))
                {
                    j += "global::".Length;
                    continue;
                }

                result[i++] = source[j++];
            }

            return (i == source.Length ? source.ToString() : result[..i].ToString()).HtmlEncode();
        }

        /// <summary>
        /// Returns a sanitized member-name fragment derived from the type name.
        /// </summary>
        /// <returns>An identifier-safe name fragment.</returns>
        public string GetTypeMemberName()
        {
            if (string.IsNullOrWhiteSpace(self))
                return "_";

            var source = self.AsSpan(self.GetTypeNameTailStartIndex());
            if (source.Length is 0)
                return "_";

            var result = source.Length <= 1024
                ? stackalloc char[source.Length]
                : new char[source.Length];

            int i = 0;
            bool isFirst = true;
            for (int j = 0; j < source.Length;)
            {
                if (HasGlobalPrefix(source, j))
                {
                    j += "global::".Length;
                    continue;
                }

                char c = source[j++];

                if (isFirst)
                {
                    result[i++] = char.IsLetter(c) || c is '_'
                        ? c
                        : '_';
                    isFirst = false;
                }
                else
                {
                    result[i++] = char.IsLetterOrDigit(c) || c is '_'
                        ? c
                        : '_';
                }
            }

            while (i > 0 && result[i - 1] is '_')
                i--;

            return i > 0 ? result[..i].ToString() : "_";
        }
    }

    static bool HasGlobalPrefix(ReadOnlySpan<char> source, int index)
        => source[index..] is ['g', 'l', 'o', 'b', 'a', 'l', ':', ':', ..];

    extension(IEnumerable<string> self)
    {
        /// <summary>
        /// Joins the sequence with the specified separator.
        /// </summary>
        /// <param name="separator">Separator inserted between items.</param>
        /// <returns>The joined string.</returns>
        public string Join(string separator)
            => string.Join(separator, self);
    }

    extension<T>(ImmutableArray<T> self)
    {
        /// <summary>
        /// Returns the underlying array without copying.
        /// This violates the <see cref="ImmutableArray{T}"/> contract, but is useful for optimization.
        /// </summary>
        /// <returns>The backing array, or an empty array when the value is default.</returns>
        public T[] AsArray()
            => Unsafe.As<ImmutableArray<T>, ValueTuple<T[]>>(ref self).Item1 ?? [];
    }

    /// <summary>
    /// Returns whether none of the specified modifier kinds are present.
    /// </summary>
    /// <param name="modifiers">Modifier list to inspect.</param>
    /// <param name="kinds">Modifier kinds that must be absent.</param>
    /// <returns><c>true</c> when no requested modifier is present.</returns>
    public static bool None(this SyntaxTokenList modifiers, params ReadOnlySpan<SyntaxKind> kinds)
    {
        foreach (var kind in kinds)
            if (modifiers.Any(kind))
                return false;

        return true;
    }

    extension(SyntaxTokenList modifiers)
    {
        /// <summary>
        /// Returns whether the modifier list contains <see cref="SyntaxKind.StaticKeyword"/>.
        /// </summary>
        public bool IsStatic => modifiers.Any(SyntaxKind.StaticKeyword);

        /// <summary>
        /// Returns whether the modifier list contains <see cref="SyntaxKind.SealedKeyword"/>.
        /// </summary>
        public bool IsSealed => modifiers.Any(SyntaxKind.SealedKeyword);
    }

#pragma warning disable CS0649
    // ReSharper disable once ClassNeverInstantiated.Local
    sealed class ListData<T>
    {
        public required T[] Items;
        public required int Size;
        public required int Version;
    }
#pragma warning restore CS0649

    /// <summary>
    /// Returns whether an enum value contains the specified flag bits.
    /// </summary>
    /// <typeparam name="TEnum">Enum type.</typeparam>
    /// <param name="symbol">Enum value to inspect.</param>
    /// <param name="flag">Flag bits to test.</param>
    /// <returns><c>true</c> when any bits in <paramref name="flag"/> are present.</returns>
    public static bool Has<TEnum>(this TEnum symbol, TEnum flag)
        where TEnum : unmanaged, Enum
    {
        ulong a = 0;
        ulong b = 0;
        Unsafe.As<ulong, TEnum>(ref a) = symbol;
        Unsafe.As<ulong, TEnum>(ref b) = flag;
        return (a & b) != 0;
    }

    extension<T>(List<T> list)
    {
        /// <summary>
        /// Ensures the list can hold at least the specified number of items without reallocating.
        /// </summary>
        /// <param name="capacity">Required capacity.</param>
        public void EnsureCapacity(int capacity)
        {
            if (list.Capacity < capacity)
                list.Capacity = capacity;
        }

        /// <summary>
        /// Adds the value only when it is not already present.
        /// </summary>
        /// <param name="value">Value to add.</param>
        /// <returns><c>true</c> when the value was added.</returns>
        public bool AddUnique(T value)
        {
            if (list.Contains(value))
                return false;

            list.Add(value);
            return true;
        }

        /// <summary>
        /// Returns a span over the populated portion of the list.
        /// </summary>
        /// <returns>A span over the list contents.</returns>
        public Span<T> AsSpan()
            => Unsafe.As<List<T>, ListData<T>>(ref list).Items.AsSpan(0, list.Count);
    }

    /// <summary>
    /// Deconstructs a key-value pair into its key and value.
    /// </summary>
    public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value)
        => (key, value) = (pair.Key, pair.Value);
}

