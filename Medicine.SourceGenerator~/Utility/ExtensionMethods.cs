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

        public bool IsAccessible
            => self.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal;
    }

    extension(ISymbol? self)
    {
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

        public bool Is(ISymbol other)
            => SymbolEqualityComparer.Default.Equals(self, other);

        public bool Is(string fqn)
            => self?.FQN.Equals(fqn, Ordinal) ?? false;

        public bool HasAttribute(string attributeFullyQualifiedName)
        {
            if (self is null)
                return false;

            foreach (var x in self.GetAttributes())
                if (x.AttributeClass.Is(attributeFullyQualifiedName))
                    return true;

            return false;
        }

        public bool HasAttribute(ISymbol? attributeSymbol)
        {
            if (self is null || attributeSymbol is null)
                return false;

            foreach (var x in self.GetAttributes())
                if (x.AttributeClass.Is(attributeSymbol))
                    return true;

            return false;
        }

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

        public AttributeData? GetAttribute(string attributeFullyQualifiedName)
        {
            if (self is null)
                return null;

            foreach (var x in self.GetAttributes())
                if (x.AttributeClass.Is(attributeFullyQualifiedName))
                    return x;

            return null;
        }

        public AttributeData? GetAttribute(ISymbol? attributeSymbol)
        {
            if (self is null || attributeSymbol is null)
                return null;

            foreach (var x in self.GetAttributes())
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

    extension(SyntaxNode self)
    {
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

        public ulong GetNodeChecksum(CancellationToken ct)
            => self
                .SyntaxTree
                .GetText(ct)
                .GetSubText(self.FullSpan)
                .CalculateChecksum64();

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
        public string Text
            => nameSyntax.Identifier.ValueText;
    }

    extension(NameSyntax nameSyntax)
    {
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

    public static bool None(this SyntaxTokenList modifiers, params ReadOnlySpan<SyntaxKind> kinds)
    {
        foreach (var kind in kinds)
            if (modifiers.Any(kind))
                return false;

        return true;
    }

    extension(SyntaxTokenList modifiers)
    {
        public bool IsStatic => modifiers.Any(SyntaxKind.StaticKeyword);
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

    public static Span<T> AsSpan<T>(this List<T> list)
        => Unsafe.As<List<T>, ListData<T>>(ref list).Items.AsSpan(0, list.Count);

    public static bool Has<TEnum>(this TEnum symbol, TEnum flag)
        where TEnum : unmanaged, Enum
    {
        ulong a = 0;
        ulong b = 0;
        Unsafe.As<ulong, TEnum>(ref a) = symbol;
        Unsafe.As<ulong, TEnum>(ref b) = flag;
        return (a & b) != 0;
    }

    public static void EnsureCapacity<T>(this List<T> list, int capacity)
    {
        if (list.Capacity < capacity)
            list.Capacity = capacity;
    }

    public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value)
        => (key, value) = (pair.Key, pair.Value);
}
