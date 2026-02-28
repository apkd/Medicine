using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using static System.StringComparison;
using static ActivePreprocessorSymbolNames;
using static Constants;
using static InjectSourceGenerator.ExpressionFlags;
using static Microsoft.CodeAnalysis.SymbolDisplayFormat;
using Identifier = Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax;
using Name = Microsoft.CodeAnalysis.CSharp.Syntax.NameSyntax;
using GenericName = Microsoft.CodeAnalysis.CSharp.Syntax.GenericNameSyntax;
using MemberAccess = Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax;
using Invocation = Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax;
using Lambda = Microsoft.CodeAnalysis.CSharp.Syntax.LambdaExpressionSyntax;
using Expression = Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax;
using MethodDecl = Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax;
using LocalFunctionDecl = Microsoft.CodeAnalysis.CSharp.Syntax.LocalFunctionStatementSyntax;
using CT = System.Threading.CancellationToken;

static class InjectSourceGeneratorExtensions
{
    public static bool Has(this InjectSourceGenerator.ExpressionFlags flags, InjectSourceGenerator.ExpressionFlags value)
        => (flags & value) == value;

    public static bool Any(this InjectSourceGenerator.ExpressionFlags flags, InjectSourceGenerator.ExpressionFlags value)
        => (flags & value) > 0;

    public static void Set(this ref InjectSourceGenerator.ExpressionFlags flags, InjectSourceGenerator.ExpressionFlags flag, bool value)
        => flags = value ? flags | flag : flags & ~flag;
}

[Generator]
public sealed class InjectSourceGenerator : IIncrementalGenerator
{
    static readonly DiagnosticDescriptor MED006 = new(
        id: nameof(MED006),
        title: "Multiple assignments to injected property",
        messageFormat: $"The property '{{0}}' can only be assigned to once within the [{InjectAttributeName}] method",
        category: "Medicine",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    static readonly DiagnosticDescriptor MED014 = new(
        id: nameof(MED014),
        title: $"Unable to determine type of injected property",
        messageFormat: $"The source generator was unable to determine the type of the property '{{0}}'.",
        category: "Medicine",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    record struct GeneratorInput() : ISourceGeneratorPassData
    {
        public string? SourceGeneratorOutputFilename { get; init; }
        public string? SourceGeneratorError { get; init; }
        public LocationInfo? SourceGeneratorErrorLocation { get; set; }

        public ActivePreprocessorSymbolNames Symbols;
        public bool ShouldEmitDocs;
        public string? InjectMethodName;
        public string? InjectMethodOrLocalFunctionName;
        public bool IsSealed;
        public bool IsStatic;
        public bool? MakePublic;
        public EquatableArray<string> NamespaceImports;
        public EquatableArray<string> ContainingTypeDeclaration;
        public EquatableIgnore<Func<string>> ClassName;
        public EquatableIgnore<Func<InitExpressionInfo[]>> InitExpressionInfoArrayBuilderFunc = new(() => []);

        // ReSharper disable once NotAccessedField.Local
        public EquatableArray<byte> MethodTextCheckSumForCache;
    }

    record struct InitExpressionInfo
    {
        public string PropertyName;
        public string InitExpression;
        public string[] EditModeLocalDeclarations;
        public string? TypeFQN;
        public string TypeDisplayName;
        public string? CleanupExpression;
        public string? TypeXmlDocId;
        public LocationInfo Location;
        public ExpressionFlags Flags;
    }

    [Flags]
    public enum ExpressionFlags : uint
    {
        IsSingleton = 1 << 00,
        IsTracked = 1 << 01,
        NeedsNullCheck = 1 << 02,
        IsUnityObject = 1 << 03,
        IsUnityComponent = 1 << 04,
        IsUnityScriptableObject = 1 << 05,
        IsDisposable = 1 << 06,
        IsArray = 1 << 07,
        IsLazy = 1 << 08,
        IsValueType = 1 << 09,
        IsOptional = 1 << 10,
        IsOptionalViaComment = 1 << 11,
        IsTransient = 1 << 12,
        IsCleanupDispose = 1 << 13,
        IsCleanupDestroy = 1 << 14,
    }

    void IIncrementalGenerator.Initialize(IncrementalGeneratorInitializationContext context)
    {
        var syntaxProvider = context.SyntaxProvider
            .ForAttributeWithMetadataNameEx(
                fullyQualifiedMetadataName: InjectAttributeMetadataName,
                predicate: static (node, _)
                    => node switch
                    {
                        MethodDecl x        => !x.Modifiers.Any(SyntaxKind.AbstractKeyword),
                        LocalFunctionDecl x => !x.Modifiers.Any(SyntaxKind.AbstractKeyword),
                        _                   => false,
                    },
                transform: static (attributeContext, _) => new GeneratorAttributeContextInput { Context = attributeContext }
            )
            .CombineWithGeneratorEnvironment(context)
            .SelectEx((x, ct) =>
                {
                    var input = TransformSyntaxContext(x.Values.Context, x.Environment.KnownSymbols, ct);
                    input.MakePublic ??= x.Environment.MedicineSettings.MakePublic;
                    input.Symbols = x.Environment.PreprocessorSymbols;
                    input.ShouldEmitDocs = x.Environment.ShouldEmitDocs;
                    return input;
                }
            );

        context.RegisterSourceOutputEx(
            source: syntaxProvider,
            action: GenerateSource
        );
    }

    static GeneratorInput TransformSyntaxContext(GeneratorAttributeSyntaxContext context, KnownSymbols knownSymbols, CT ct)
    {
        var targetNode = context.TargetNode;

        if (targetNode.FirstAncestorOrSelf<TypeDeclarationSyntax>() is not { } classDecl)
            return default;

        if (targetNode.FirstAncestorOrSelf<MethodDecl>() is not { } methodDecl)
            return default;

        var containingClassSymbol = context.SemanticModel.GetDeclaredSymbol(classDecl, ct);

        var (injectMethodName, injectMethodModifiers) = targetNode switch
        {
            MethodDecl x        => (x.Identifier.Text, x.Modifiers),
            LocalFunctionDecl x => (methodDecl.Identifier.Text, x.Modifiers),
            _                   => (null, default),
        };

        if (injectMethodName is null)
            return default;

        var output = new GeneratorInput
        {
            InjectMethodName = injectMethodName,
            InjectMethodOrLocalFunctionName = (targetNode as LocalFunctionDecl)?.Identifier.ValueText ?? injectMethodName,
            SourceGeneratorOutputFilename = Utility.GetOutputFilename(
                filePath: classDecl.Identifier.ValueText,
                targetFQN: injectMethodName,
                shadowTargetFQN: (targetNode as LocalFunctionDecl)?.Identifier.ValueText ?? "",
                label: $"[{InjectAttributeNameShort}]"
            ),
            ContainingTypeDeclaration = Utility.DeconstructTypeDeclaration(classDecl, context.SemanticModel, ct),
            MethodTextCheckSumForCache = targetNode.GetText().GetChecksum().AsArray(),
        };

        if (targetNode.SyntaxTree.GetRoot(ct) is CompilationUnitSyntax compilationUnit)
        {
            using var r1 = Scratch.RentA<List<string>>(out var list);

            foreach (var usingDirectiveSyntax in compilationUnit.Usings)
                list.Add(usingDirectiveSyntax.ToString());

            output.NamespaceImports = list.ToArray();
        }

        output.MakePublic = context.Attributes.First()
            .GetAttributeConstructorArguments(ct)
            .Select(x => x.Get<bool>("makePublic", null));

        output.IsSealed = classDecl.Modifiers.IsSealed;
        output.IsStatic = injectMethodModifiers.IsStatic;

        // defer the expensive calls to the source gen phase

        output.ClassName = new(() => context.SemanticModel
            .GetDeclaredSymbol(classDecl, ct)
            ?.ToMinimalDisplayString(context.SemanticModel, targetNode.SpanStart, MinimallyQualifiedFormat) ?? ""
        );

        output.InitExpressionInfoArrayBuilderFunc = new(() =>
            {
                var assignments = targetNode
                    .DescendantNodes(x => x is MethodDecl or LocalFunctionDecl or BlockSyntax or ArrowExpressionClauseSyntax or ExpressionStatementSyntax or ReturnStatementSyntax)
                    .OfType<AssignmentExpressionSyntax>()
                    .Where(x => x.Left is Identifier && context.SemanticModel.GetSymbolInfo(x.Left, ct).Symbol is null)
                    .ToArray();

                // multi-pass type resolution.
                // some expressions might be referencing properties generated by other assignments; in that case
                // we need to re-evaluate the type of the assignment expression with the patched identifiers.
                // we do this multiple times until all assignments are resolved, or until we're stuck.
                var resolvedTypes = new Dictionary<string, ITypeSymbol>(capacity: assignments.Length);
                var results = new InitExpressionInfo[assignments.Length];
                var resolvedMask = new bool[assignments.Length];

                for (int pass = 0; pass < assignments.Length; pass++)
                {
                    bool anyResolvedThisPass = false;

                    for (int i = 0; i < assignments.Length; i++)
                    {
                        if (resolvedMask[i])
                            continue;

                        var assignment = assignments[i];
                        var typeSymbol = context.SemanticModel.GetTypeInfo(assignment.Right, ct).Type;

                        if (typeSymbol is null or IErrorTypeSymbol)
                            typeSymbol = SpeculativeTypePatching.ReevaluateWithResolvedIdentifiers(context.SemanticModel, assignment.Right, resolvedTypes);

                        if (typeSymbol is null or IErrorTypeSymbol)
                            continue;

                        resolvedTypes[((Identifier)assignment.Left).Identifier.ValueText] = typeSymbol;
                        results[i] = CreateInitExpressionInfo(assignment, typeSymbol);
                        resolvedMask[i] = true;
                        anyResolvedThisPass = true;
                    }

                    if (!anyResolvedThisPass)
                        break; // time to give up
                }

                // fill in expressions with unresolved types
                for (int i = 0; i < assignments.Length; i++)
                    if (!resolvedMask[i])
                        results[i] = CreateInitExpressionInfo(assignments[i], null);

                return results;

                InitExpressionInfo CreateInitExpressionInfo(AssignmentExpressionSyntax assignment, ITypeSymbol? typeSymbol)
                {
                    var typeSymbolForDisplayName = typeSymbol;
                    var flags = default(ExpressionFlags);
                    var normalizedExpression = NormalizeExpressionText(assignment.Right.ToString());
                    var expression = normalizedExpression.AsSpan();
                    var editModeLocalDeclarations = GetEditModeLocalDeclarations(assignment);

                    if (assignment.Right is ImplicitObjectCreationExpressionSyntax { ArgumentList.Arguments: [{ Expression: { } expr }, ..] })
                    {
                        if (GetDelegateReturnType(expr) is { } exprType)
                        {
                            var lazyType = exprType.IsValueType
                                ? knownSymbols.LazyVal
                                : knownSymbols.LazyRef;

                            typeSymbol = lazyType?.Construct(exprType);
                            typeSymbolForDisplayName = exprType;
                            flags.Set(IsLazy, true);
                        }
                    }
                    else if (assignment.Right is Lambda or MemberAccess or GenericName or Name)
                    {
                        if (GetDelegateReturnType(assignment.Right) is { } exprType)
                        {
                            typeSymbol = knownSymbols.SystemFunc1?.Construct(exprType);
                            typeSymbolForDisplayName = exprType;
                            flags.Set(IsTransient, true);
                        }
                    }

                    if (typeSymbol is { IsStatic: true })
                        typeSymbol = null;

                    ITypeSymbol? GetDelegateReturnType(Expression delegateExpression)
                        => delegateExpression switch
                        {
                            Lambda { Body: Expression body }
                                => context.SemanticModel.GetTypeInfo(body, ct).Type,
                            Lambda { Body: BlockSyntax body }
                                => GetFirstNonNullReturnType(body),
                            MemberAccess or GenericName or Name
                                => GetMethodGroupReturnType(delegateExpression),
                            _ => null,
                        };

                    ITypeSymbol? GetFirstNonNullReturnType(BlockSyntax body)
                    {
                        foreach (var node in body.DescendantNodes())
                            if (node is ReturnStatementSyntax { Expression: { } returnExpression })
                                if (context.SemanticModel.GetTypeInfo(returnExpression, ct).Type is { } returnType)
                                    return returnType;

                        return null;
                    }

                    ITypeSymbol? GetMethodGroupReturnType(Expression delegateExpression)
                    {
                        var symbolInfo = context.SemanticModel.GetSymbolInfo(delegateExpression, ct);
                        foreach (var symbol in symbolInfo.CandidateSymbols)
                            if (symbol is IMethodSymbol { Parameters.Length: 0 } method)
                                return method.ReturnType;

                        return null;
                    }

                    bool MatchOption(ref ReadOnlySpan<char> expression, ExpressionFlags flag, ReadOnlySpan<char> pattern)
                    {
                        if (expression.EndsWith(pattern, Ordinal))
                        {
                            expression = expression[..^pattern.Length].TrimEnd();
                            flags |= flag;
                            return true;
                        }

                        return false;
                    }

                    bool MatchOptionWithDelegateArg(ref ReadOnlySpan<char> expression, out string argument, ReadOnlySpan<char> pattern)
                    {
                        argument = "";

                        if (expression is not [.., ')'])
                            return false;

                        int openBracketIndex = -1;
                        int bracketBalance = 0;
                        for (int i = expression.Length - 2; i >= 0; i--)
                        {
                            char c = expression[i];
                            if (c == ')')
                            {
                                bracketBalance++;
                            }
                            else if (c == '(')
                            {
                                if (bracketBalance == 0)
                                {
                                    openBracketIndex = i;
                                    break;
                                }

                                bracketBalance--;
                            }
                        }

                        if (openBracketIndex == -1)
                            return false;

                        var patternStartIndex = openBracketIndex - pattern.Length;

                        if (patternStartIndex <= 0 || expression[patternStartIndex - 1] != '.')
                            return false;

                        var patternInExpression = expression.Slice(patternStartIndex, pattern.Length);

                        if (!patternInExpression.SequenceEqual(pattern))
                            return false;

                        var argumentStartIndex = openBracketIndex + 1;
                        var argumentLength = expression.Length - 1 - argumentStartIndex;
                        var argumentSpan = expression.Slice(argumentStartIndex, argumentLength);
                        argument = argumentSpan.Trim().ToString();

                        expression = expression[..(patternStartIndex - 1)];

                        return true;
                    }

                    var right = assignment.Right;

                    bool IsStaticPropertyAccess(INamedTypeSymbol? classAttribute, string propertyName)
                        => right is MemberAccess
                           {
                               Expression: { } classIdentifier,
                               Name.Text: { Length: > 0 } propertyNameIdentifierText,
                           }
                           && propertyNameIdentifierText == propertyName
                           && context.SemanticModel.GetSymbolInfo(classIdentifier, ct).Symbol is ITypeSymbol accessedClassTypeSymbol
                           && accessedClassTypeSymbol.HasAttribute(classAttribute)
                           || right is Identifier
                           {
                               Identifier.ValueText: { Length: > 0 } simpleIdentifierText,
                           }
                           && simpleIdentifierText == propertyName
                           && containingClassSymbol is { } classSymbol
                           && classSymbol.HasAttribute(classAttribute);

                    bool IsFindMethodCall(string methodName)
                        => right is Invocation
                           {
                               Expression: MemberAccess
                               {
                                   Expression: { } findClassIdentifier,
                                   Name.Text: var identifierText,
                               },
                           }
                           && context.SemanticModel.GetSymbolInfo(findClassIdentifier, ct).Symbol is ITypeSymbol findClassType
                           && findClassType.Is(knownSymbols.MedicineFind)
                           && identifierText == methodName;

                    string? cleanupExpression = null;

                    while (true)
                    {
                        if (MatchOption(ref expression, IsOptional, ".Optional()".AsSpan()))
                        {
                            if (right is Invocation { Expression: MemberAccess { Expression: { } inner, Name.Text: "Optional" } })
                                right = inner;

                            continue;
                        }

                        if (MatchOptionWithDelegateArg(ref expression, out string cleanup, "Cleanup".AsSpan()))
                        {
                            cleanupExpression = cleanup;
                            continue;
                        }

                        break;
                    }

                    // match optional comment
                    {
                        SyntaxTriviaList trivia = [];

                        static SyntaxTriviaList TerminatorTrailingTrivia(SyntaxNode node)
                            => node.GetLastToken(includeZeroWidth: true).TrailingTrivia;

                        // prefer the nearest statement terminator (covers: `x = ...; // optional`, `return x = ...; // optional`, etc.)
                        if (assignment.FirstAncestorOrSelf<StatementSyntax>() is { } stmt)
                            trivia = TerminatorTrailingTrivia(stmt);
                        // expression-bodied methods/local functions: `=> x = ...; // optional`
                        else if (assignment.FirstAncestorOrSelf<ArrowExpressionClauseSyntax>()?.Parent is { } owner)
                            trivia = TerminatorTrailingTrivia(owner);

                        foreach (var t in trivia)
                        {
                            if (t.IsKind(SyntaxKind.SingleLineCommentTrivia))
                            {
                                var text = t.ToString().AsSpan().Trim();
                                if (text.StartsWith("//", Ordinal) && text[2..].Trim().Equals("optional", OrdinalIgnoreCase))
                                {
                                    flags |= IsOptional | IsOptionalViaComment;
                                    break;
                                }
                            }
                            else if (t.IsKind(SyntaxKind.MultiLineCommentTrivia))
                            {
                                var text = t.ToString().AsSpan().Trim();
                                if (text.StartsWith("/*", Ordinal) && text.EndsWith("*/", Ordinal) && text[2..^2].Trim().Equals("optional", OrdinalIgnoreCase))
                                {
                                    flags |= IsOptional | IsOptionalViaComment;
                                    break;
                                }
                            }
                        }
                    }

                    bool isSingleton =
                        IsStaticPropertyAccess(knownSymbols.SingletonAttribute, "Instance") ||
                        IsFindMethodCall("Singleton");

                    bool isTracked =
                        IsStaticPropertyAccess(knownSymbols.TrackAttribute, "Instances") ||
                        IsFindMethodCall("Instances");

                    flags.Set(IsSingleton, isSingleton);
                    flags.Set(IsTracked, isTracked);

                    if (isSingleton || isTracked)
                    {
                        if (right is MemberAccess memberAccessExpr)
                            if (context.SemanticModel.GetSymbolInfo(memberAccessExpr.Expression, ct).Symbol is ITypeSymbol accessedExpressionSymbol)
                                typeSymbol = typeSymbolForDisplayName = accessedExpressionSymbol;
                    }

                    const string name1 = "TrackedInstances<T>.ImmediateEnumerable";
                    const string name2 = "TrackedInstances<T>.StrideEnumerable";
                    if ((isTracked, typeSymbol) is (true, { Name: name1 or name2, ContainingType.TypeArguments: [var containingTypeArg] }))
                        typeSymbolForDisplayName = containingTypeArg;
                    else if ((isTracked, typeSymbol) is (true, INamedTypeSymbol { TypeArguments: [var typeArg] }))
                        typeSymbolForDisplayName = typeArg;

                    if (isSingleton)
                    {
                        if (right is Identifier { Identifier.ValueText: "Instance" })
                        {
                            if (containingClassSymbol is ITypeSymbol classSymbol)
                                typeSymbol = classSymbol;
                        }
                        else
                        {
                            if (right is MemberAccess { Name.Text: "Instance" } memberAccess)
                                if (context.SemanticModel.GetTypeInfo(memberAccess.Expression, ct).Type is { } symbol)
                                    typeSymbol = symbol;
                        }
                    }
                    else if (isTracked)
                    {
                        if (right is Invocation { Expression: MemberAccess { Name: GenericName { TypeArgumentList.Arguments: [var arg] } } })
                        {
                            if (context.SemanticModel.GetTypeInfo(arg, ct).Type is { } symbol)
                                typeSymbol = symbol;
                        }
                        else if (right is Identifier { Identifier.ValueText: "Instances" })
                        {
                            if (containingClassSymbol is ITypeSymbol classSymbol)
                                typeSymbol = classSymbol;
                        }
                        else
                        {
                            var first = right
                                .DescendantNodesAndSelf()
                                .FirstOrDefault(x => x is MemberAccess { Expression: Identifier, Name.Text: "Instances" });

                            if (first is MemberAccess memberAccessExpr)
                                if (context.SemanticModel.GetTypeInfo(memberAccessExpr.Expression, ct).Type is { } symbol)
                                    typeSymbol = symbol;
                        }
                    }

                    string typeDisplayName
                        = typeSymbolForDisplayName?.ToDisplayString(MinimallyQualifiedFormat) ?? "";

                    string? typeFQN
                        = typeSymbol
                            .GetSafeSymbolName(context.SemanticModel, right.SpanStart) is { Length : > 0 } name
                            ? name.TrimEnd('?')
                            : null;

                    flags.Set(
                        NeedsNullCheck,
                        value: typeSymbol?.IsReferenceType is true &&
                               // right is not BaseObjectCreationExpressionSyntax &&
                               typeSymbol is not IArrayTypeSymbol &&
                               !flags.Has(IsOptional) &&
                               !flags.Has(IsTransient) &&
                               !isSingleton &&
                               !isTracked
                    );

                    flags.Set(
                        IsUnityObject,
                        value: typeSymbol.InheritsFrom(knownSymbols.UnityObject)
                    );

                    flags.Set(
                        IsUnityComponent,
                        value: typeSymbol.InheritsFrom(knownSymbols.UnityComponent)
                    );

                    flags.Set(
                        flag: IsOptional,
                        value: flags.Has(IsOptional) &&
                               !flags.Has(IsArray) &&
                               !flags.Has(IsTransient) &&
                               !isSingleton &&
                               !isTracked
                    );

                    flags.Set(IsValueType, typeSymbol?.IsValueType is true);
                    flags.Set(IsArray, typeSymbol is IArrayTypeSymbol);
                    flags.Set(IsDisposable, typeSymbol.HasInterface(knownSymbols.SystemIDisposable));

                    return new()
                    {
                        PropertyName = assignment.Left.ToString().Trim(),
                        InitExpression = expression.ToString(),
                        EditModeLocalDeclarations = editModeLocalDeclarations,
                        CleanupExpression = cleanupExpression,
                        TypeFQN = typeFQN,
                        TypeDisplayName = typeDisplayName,
                        TypeXmlDocId = flags.Has(IsArray)
                            ? (typeSymbol as IArrayTypeSymbol)?.ElementType.GetDocumentationCommentId()
                            : typeSymbol?.GetDocumentationCommentId(),
                        Location = new(assignment.GetLocation()),
                        Flags = flags,
                    };
                }

                string[] GetEditModeLocalDeclarations(AssignmentExpressionSyntax assignment)
                {
                    using (Scratch.RentA<List<(int SpanStart, string Statement)>>(out var declarations))
                    using (Scratch.RentA<HashSet<int>>(out var seen))
                    using (Scratch.RentA<HashSet<ISymbol>>(out var visitedLocals))
                    {
                        void AddDeclaration(int spanStart, string statement)
                        {
                            if (seen.Add(spanStart))
                                declarations.Add((spanStart, statement));
                        }

                        void AddDependencies(Expression expr)
                        {
                            foreach (var identifier in expr.DescendantNodesAndSelf().OfType<Identifier>())
                            {
                                if (context.SemanticModel.GetSymbolInfo(identifier, ct).Symbol is not ILocalSymbol localSymbol)
                                    continue;

                                AddLocal(localSymbol);
                            }
                        }

                        void AddLocal(ILocalSymbol localSymbol)
                        {
                            if (!visitedLocals.Add(localSymbol))
                                return;

                            foreach (var syntaxRef in localSymbol.DeclaringSyntaxReferences)
                            {
                                var declSyntax = syntaxRef.GetSyntax(ct);

                                if (!methodDecl.Span.Contains(declSyntax.SpanStart))
                                    continue;

                                if (declSyntax is VariableDeclaratorSyntax varDeclarator)
                                {
                                    if (varDeclarator.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>() is { } localDeclStmt)
                                        AddDeclaration(localDeclStmt.SpanStart, localDeclStmt.ToString());
                                    else if (varDeclarator.Parent is VariableDeclarationSyntax varDecl)
                                        AddDeclaration(varDecl.SpanStart, $"{varDecl};");

                                    if (varDeclarator.Initializer?.Value is { } initExpr)
                                        AddDependencies(initExpr);

                                    continue;
                                }

                                if (declSyntax is LocalDeclarationStatementSyntax localDecl)
                                {
                                    AddDeclaration(localDecl.SpanStart, localDecl.ToString());

                                    var declarator = localDecl.Declaration.Variables
                                        .FirstOrDefault(x => x.Identifier.ValueText == localSymbol.Name);

                                    if (declarator?.Initializer?.Value is { } initExpr)
                                        AddDependencies(initExpr);
                                }
                            }
                        }

                        AddDependencies(assignment.Right);

                        if (declarations.Count == 0)
                            return [];

                        declarations.Sort(static (a, b) => a.SpanStart.CompareTo(b.SpanStart));

                        var result = new string[declarations.Count];
                        for (int i = 0; i < declarations.Count; i++)
                            result[i] = NormalizeLocalDeclarationText(declarations[i].Statement);

                        return result;
                    }
                }
            }
        );

        return output;
    }

    static string NormalizeExpressionText(string text)
        => NormalizeText(text.AsSpan(), replaceStandaloneLfWithSpace: false);

    static string NormalizeLocalDeclarationText(string text)
        => NormalizeText(text.AsSpan(), replaceStandaloneLfWithSpace: true);

    static string NormalizeText(ReadOnlySpan<char> source, bool replaceStandaloneLfWithSpace)
    {
        var buffer = source.Length <= 1024
            ? stackalloc char[source.Length]
            : new char[source.Length];

        int written = 0;
        for (int i = 0; i < source.Length; i++)
        {
            switch (source[i])
            {
                case '\r':
                {
                    if (i + 1 < source.Length && source[i + 1] == '\n')
                        i++;

                    continue;
                }
                case '\n':
                {
                    if (replaceStandaloneLfWithSpace)
                        buffer[written++] = ' ';

                    continue;
                }
                default:
                    buffer[written++] = source[i];
                    break;
            }
        }

        return buffer[..written].Trim().ToString();
    }

    static void GenerateSource(SourceProductionContext context, SourceWriter src, GeneratorInput input)
    {
        src.ShouldEmitDocs = input.ShouldEmitDocs;

        var expressions = input.InitExpressionInfoArrayBuilderFunc.Value();

        foreach (var group in expressions.GroupBy(x => x.PropertyName).Where(x => x.Count() > 1))
        foreach (var x in group)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    descriptor: MED006,
                    location: x.Location.ToLocation(),
                    group.Key
                )
            );
        }

        expressions = expressions
            .GroupBy(x => x.PropertyName, StringComparer.Ordinal)
            .Select(x => x.First())
            .ToArray();

        string storageSuffix = input.InjectMethodOrLocalFunctionName is "Awake" ? "" : $"For{input.InjectMethodOrLocalFunctionName}";
        string storagePropName = $"{m}MedicineInternal{storageSuffix}";
        string storageStructName = $"{m}MedicineInternalBackingStorage{storageSuffix}";

        string className = input.ClassName.Value();

        using var r1 = Scratch.RentA<List<string>>(out var deferredLines);

        void Defer(string line)
            => deferredLines.Add(line);

        void DeferLinebreak()
            => deferredLines.Add("");

        src.Line.Write("#pragma warning disable CS0628 // New protected member declared in sealed type");
        src.Line.Write("#pragma warning disable CS0108 // Member hides inherited member; missing new keyword");
        src.Line.Write("#pragma warning disable CS0618 // Type or member is obsolete");
        src.Linebreak();

        src.Line.Write(Alias.UsingInline);
        src.Line.Write(Alias.UsingUtility);
        src.Line.Write(Alias.UsingFind);
        src.Line.Write(Alias.UsingStorage);
        src.Line.Write(Alias.UsingDebug);
        src.Line.Write(Alias.UsingDeclaredAt);

        string access = input switch
        {
            { MakePublic: true } => "public ",
            { IsSealed: false }  => "protected ",
            _                    => "",
        };

        string @private = access is "" ? "" : "private ";
        string @static = input.IsStatic ? "static " : "";

        src.Linebreak();
        foreach (var @using in input.NamespaceImports)
            src.Line.Write(@using);

        src.Linebreak();

        foreach (var x in input.ContainingTypeDeclaration)
        {
            src.Line.Write(x);
            src.Line.Write('{');
            src.IncreaseIndent();
        }

        foreach (var x in expressions)
        {
            string nul = x.Flags.Has(IsValueType) ? "" : "?";
            string opt = x.Flags.Has(IsOptional) ? "?" : "";
            string exc = x.Flags.Has(IsOptional) ? "" : "!";

            void AppendInjectionDeclaredIn()
            {
                int line = x.Location.FileLineSpan.StartLinePosition.Line + 1;
                src.Line.Write($"[{m}DeclaredAt(method: nameof({input.InjectMethodName}), line: {line})] ");
            }

            void OpenListAndAppendOptionalDescription(string nullIf)
            {
                if (x.Flags.Has(IsOptional))
                    src.Doc?.Write($"/// This code-generated property is marked as <b>optional</b>:");
                else if (x.Flags.Has(NeedsNullCheck))
                    src.Doc?.Write($"/// This code-generated property is checked for <c>null</c>:");

                src.Doc?.Write($"/// <list type=\"bullet\">");
                if (x.Flags.Has(IsOptional))
                {
                    src.Doc?.Write($"/// <item>The getter will <b>silently</b> return <c>null</c> if {nullIf}.</item>");
                    if (x.Flags.Has(IsOptionalViaComment))
                        src.Doc?.Write($"/// <item>Remove <c>// optional</c> comment from the end of the assignment to re-enable the null check + error log. </item>");
                    else
                        src.Doc?.Write($"/// <item>Remove <c>.Optional()</c> call from the end of the assignment to re-enable the null check + error log. </item>");
                }
                else if (x.Flags.Has(NeedsNullCheck))
                {
                    src.Doc?.Write($"/// <item>The getter <b>will log an error</b> and return <c>null</c> if {nullIf}.</item>");
                    src.Doc?.Write($"/// <item>Append <c>.Optional()</c> call or a <c>// optional</c> comment at the end of the assignment to suppress the null check + error log. </item>");
                }
                else if (x.Flags.Has(IsArray))
                {
                    src.Doc?.Write($"/// <item>The getter will never return <c>null</c> - will always fall back to an empty array.</item>");
                }
            }

            // handle unrecognized types
            if (x.TypeFQN is null)
            {
                src.Doc?.Write($"/// <summary>");
                src.Doc?.Write($"/// <p><b>The source generator was unable to determine the type of this property.</b></p>");
                src.Doc?.Write($"/// <p>Make sure that the assignment expression is correct, and that it isn't referring to other code-generated properties.</p>");
                src.Doc?.Write($"/// </summary>");
                AppendInjectionDeclaredIn();
                src.Line.Write($"{@static}object {x.PropertyName};");
                src.Linebreak();

                context.ReportDiagnostic(
                    Diagnostic.Create(
                        descriptor: MED014,
                        location: x.Location.ToLocation(),
                        x.PropertyName
                    )
                );
            }
            // handle singleton classes
            else if (x.Flags.Has(IsSingleton))
            {
                if (input.Symbols.Has(UNITY_EDITOR))
                {
                    src.Doc?.Write($"/// <summary> Provides access to the active <see cref=\"{x.TypeXmlDocId}\"/> singleton instance. </summary>");
                    src.Doc?.Write($"/// <remarks>");
                    OpenListAndAppendOptionalDescription(nullIf: "the singleton instance could not be found");
                    src.Doc?.Write($"/// </list>");
                    src.Doc?.Write($"/// Additional notes:");
                    src.Doc?.Write($"/// <inheritdoc cref=\"{x.TypeFQN}.Instance\"/>");
                    src.Doc?.Write($"/// </remarks>");
                    AppendInjectionDeclaredIn();
                }

                if (!x.Flags.Has(IsOptional))
                    src.Line.Write("[global::System.Diagnostics.CodeAnalysis.AllowNull, global::JetBrains.Annotations.CanBeNull]");

                src.Line.Write($"{access}{@static}{x.TypeFQN}{opt} {x.PropertyName}");
                using (src.Braces)
                {
                    src.Line.Write($"{Alias.Inline} get => {x.TypeFQN}.Instance!;");

                    // discard the assign - only need this for type resolution
                    src.Line.Write($"{Alias.Inline} set {{ }}");
                }
            }
            // handle tracked classes
            else if (x.Flags.Has(IsTracked))
            {
                if (input.Symbols.Has(UNITY_EDITOR))
                {
                    src.Doc?.Write($"/// <inheritdoc cref=\"{x.TypeFQN}.Instances\"/>");
                    AppendInjectionDeclaredIn();
                }

                src.Line.Write($"{access}{@static}global::Medicine.TrackedInstances<{x.TypeFQN}> {x.PropertyName}");
                using (src.Braces)
                {
                    // we can save some memory by omitting the backing field when the struct is a static accessor with no stored state
                    src.Line.Write($"{Alias.Inline} get => default;");

                    // discard the assign - only need this for type resolution
                    src.Line.Write($"{Alias.Inline} set {{ }}");
                }
            }
            // handle other classes and structs
            else
            {
                string label = (x.Flags.Has(IsArray), x.Flags.Has(IsValueType)) switch
                {
                    (true, _) => "array",
                    (_, true) => "struct",
                    _         => "instance",
                };

                string WithFallback(string expr) => x.Flags.Has(IsArray)
                    ? $"{m}Utility.FallbackToEmpty({expr})"
                    : expr;

                if (input.Symbols.Has(UNITY_EDITOR))
                {
                    src.Doc?.Write($"/// <summary> Cached <see cref=\"{x.TypeXmlDocId}\"/> {label}.");
                    src.Doc?.Write($"/// <br/>Initialized from expression: <c>{x.InitExpression.HtmlEncode()}</c></summary>");
                    src.Doc?.Write($"/// <remarks>");
                    OpenListAndAppendOptionalDescription(nullIf: "the component could not be found");
                    if (x.TypeFQN?.StartsWith("global::Medicine.Internal.ComponentEnumerable<", Ordinal) is true)
                        src.Doc?.Write($"/// <item> This struct lazily enumerates all components of the given type.</item>");
                    else if (x.TypeFQN?.StartsWith("global::Medicine.Internal.ComponentsInSceneEnumerable<", Ordinal) is true)
                        src.Doc?.Write($"/// <item> This struct lazily enumerates all components of the given type that exist in the given scene.</item>");
                    else if (x.Flags.Has(IsLazy))
                        src.Doc?.Write($"/// <item> This property lazily evaluates the given expression.</item>");

                    src.Doc?.Write($"/// </list>");

                    src.Doc?.Write($"/// </remarks>");
                    AppendInjectionDeclaredIn();
                }

                if (!x.Flags.Has(IsOptional))
                    src.Line.Write("[global::System.Diagnostics.CodeAnalysis.AllowNull]");

                if (x.Flags.Has(IsValueType))
                {
                    src.Line.Write($"{access}{@static}ref {x.TypeFQN}{opt} {x.PropertyName}");
                    using (src.Braces)
                    {
                        src.Line.Write($"{Alias.Inline} get");
                        using (src.Braces)
                        {
                            if (input.Symbols.Has(UNITY_EDITOR) && !x.Flags.Has(IsDisposable))
                            {
                                if (x.EditModeLocalDeclarations is { Length: > 0 })
                                {
                                    src.Line.Write($"{Alias.NoInline} void {m}Expr()");
                                    using (src.Braces)
                                    {
                                        foreach (var declaration in x.EditModeLocalDeclarations)
                                            src.Line.Write(declaration);

                                        src.Line.Write($"{storagePropName}._{m}{x.PropertyName} = {x.InitExpression};");
                                    }
                                }
                                else
                                {
                                    src.Line.Write($"{Alias.NoInline} void {m}Expr() => {storagePropName}._{m}{x.PropertyName} = {x.InitExpression};");
                                }

                                src.Line.Write($"if ({m}Utility.EditMode)");
                                using (src.Indent)
                                    src.Line.Write($"{m}Expr();"); // in edit mode, always call initializer
                            }

                            src.Line.Write($"return ref {storagePropName}._{m}{x.PropertyName};");
                        }
                    }
                }
                else
                {
                    src.Line.Write($"{access}{@static}{x.TypeFQN}{opt} {x.PropertyName}");
                    using (src.Braces)
                    {
                        src.Line.Write($"{Alias.Inline} get");
                        using (src.Braces)
                        {
                            if (input.Symbols.Has(UNITY_EDITOR) && !x.Flags.Has(IsDisposable))
                            {
                                src.Line.Write($"#nullable disable");
                                if (x.EditModeLocalDeclarations is { Length: > 0 })
                                {
                                    src.Line.Write($"{Alias.NoInline} {x.TypeFQN} {m}Expr()");
                                    using (src.Braces)
                                    {
                                        foreach (var declaration in x.EditModeLocalDeclarations)
                                            src.Line.Write(declaration);

                                        src.Line.Write($"return {x.InitExpression};");
                                    }
                                }
                                else
                                {
                                    src.Line.Write($"{Alias.NoInline} {x.TypeFQN} {m}Expr() => {x.InitExpression};");
                                }

                                src.Line.Write($"#nullable enable");

                                src.Line.Write($"if ({m}Utility.EditMode)");
                                using (src.Indent)
                                    src.Line.Write($"return {WithFallback($"{m}Expr()")};"); // in edit mode, always call initializer
                            }

                            src.Line.Write($"return {WithFallback($"{storagePropName}._{m}{x.PropertyName}")}!;");
                        }

                        src.Line.Write($"{Alias.Inline} {@private}set");
                        using (src.Braces)
                        {
                            if (input.Symbols.Has(DEBUG) && x.Flags.Has(NeedsNullCheck))
                            {
                                if (x.Flags.Has(IsUnityObject))
                                    src.Line.Write($"if (!{m}Utility.IsNativeObjectAlive(value))");
                                else
                                    src.Line.Write($"if (value is null)");

                                string typeLabel = x.Flags switch
                                {
                                    _ when x.Flags.Has(IsUnityComponent)        => "component",
                                    _ when x.Flags.Has(IsUnityScriptableObject) => "scriptable object",
                                    _                                           => "object",
                                };

                                using (src.Indent)
                                    src.Line.Write($"{m}Debug.LogError($\"Missing {typeLabel}: {x.TypeDisplayName} '{x.PropertyName}' in {className} '{{this.name}}'\", this);");
                            }

                            src.Line.Write($"{storagePropName}._{m}{x.PropertyName} = value;");
                        }
                    }
                }

                // backing field
                Defer($"internal {x.TypeFQN}{nul} _{m}{x.PropertyName};");
                DeferLinebreak();

                if (x.CleanupExpression is not null)
                {
                    Defer($"internal static readonly global::System.Action<{x.TypeFQN}> _{m}{x.PropertyName}{m}CLEANUP = {x.CleanupExpression};");
                    DeferLinebreak();
                }
            }

            src.Linebreak();
        }

        src.Line.Write(Alias.Hidden);
        src.Line.Write(Alias.ObsoleteInternal);
        src.Line.Write($"partial struct {storageStructName}");
        using (src.Braces)
        {
            foreach (var line in deferredLines)
            {
                if (line is { Length: > 0 })
                    src.Line.Write(line);
                else
                    src.Linebreak();
            }
        }

        src.Linebreak();
        src.Line.Write(Alias.Hidden);
        src.Line.Write(Alias.ObsoleteInternal);
        src.Line.Write($"{@static} {storageStructName} {storagePropName};");

        if (expressions.Any(x => x.Flags.Any(IsCleanupDestroy | IsCleanupDispose) || x.CleanupExpression is not null))
        {
            src.Linebreak();

            if (input.Symbols.Has(UNITY_EDITOR))
            {
                src.Doc?.Write("/// <summary>");
                src.Doc?.Write("/// Executes generated cleanup actions for injected resources.");
                src.Doc?.Write("/// </summary>");
                src.Doc?.Write("/// <remarks>");
                src.Doc?.Write("/// Call this method from your lifecycle teardown (for example <c>OnDestroy</c>).");
                src.Doc?.Write("/// In edit mode this method returns immediately.");
                src.Doc?.Write("/// </remarks>");
            }

            src.Line.Write($"{access}void Cleanup()");
            using (src.Braces)
            {
                src.Line.Write($"if ({m}Utility.EditMode)");
                using (src.Indent)
                    src.Line.Write($"return;");

                foreach (var x in expressions)
                {
                    if (x.Flags.Has(IsCleanupDispose))
                        src.Line.Write($"{x.PropertyName}.Dispose();");

                    if (x.Flags.Has(IsCleanupDestroy))
                        src.Line.Write($"Destroy({x.PropertyName});");

                    if (x.CleanupExpression is not null)
                        src.Line.Write($"{storageStructName}._{m}{x.PropertyName}{m}CLEANUP({x.PropertyName}); // {x.CleanupExpression}");
                }
            }
        }

        foreach (var x in input.ContainingTypeDeclaration)
        {
            src.DecreaseIndent();
            src.Line.Write('}');
        }
    }
}
