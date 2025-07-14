using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static System.StringComparison;
using static Constants;
using static InjectionSourceGenerator.ExpressionFlags;
using static Microsoft.CodeAnalysis.SymbolDisplayFormat;
using CompilationUnitSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax;
using IdentifierNameSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax;
using MemberAccessExpressionSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax;

static class InjectionSourceGeneratorExtensions
{
    public static bool Has(this InjectionSourceGenerator.ExpressionFlags flags, InjectionSourceGenerator.ExpressionFlags value)
        => (flags & value) == value;

    public static bool Any(this InjectionSourceGenerator.ExpressionFlags flags, InjectionSourceGenerator.ExpressionFlags value)
        => (flags & value) > 0;

    public static void Set(this ref InjectionSourceGenerator.ExpressionFlags flags, InjectionSourceGenerator.ExpressionFlags flag, bool value)
        => flags = value ? flags | flag : flags & ~flag;
}

[Generator]
public sealed class InjectionSourceGenerator : BaseSourceGenerator, IIncrementalGenerator
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

    record struct GeneratorInput() : IGeneratorTransformOutput
    {
        public string? SourceGeneratorOutputFilename { get; init; }
        public string? SourceGeneratorError { get; set; }
        public EquatableIgnore<Location> SourceGeneratorErrorLocation { get; set; }

        public bool IsUnityEditorCompile;
        public bool IsDebugCompile;
        public bool IsSealed;
        public bool IsStatic;
        public bool MakePublic;
        public EquatableArray<string> NamespaceImports;
        public EquatableArray<string> ContainingTypeDeclaration;
        public EquatableArray<byte> MethodTextCheckSumForCache;
        public EquatableIgnore<Func<string>> MethodXmlDocId;
        public EquatableIgnore<Func<string>> ClassName;
        public EquatableIgnore<Func<InitExpressionInfo[]>> InitExpressionInfoArrayBuilderFunc = new(() => []);
    }

    record struct InitExpressionInfo
    {
        public string PropertyName;
        public string InitExpression;
        public string? TypeFQN;
        public string TypeDisplayName;
        public string? CleanupExpression;
        public string? TypeXmlDocId;
        public EquatableIgnore<Location> Location;
        public ExpressionFlags Flags;
    }

    [Flags]
    public enum ExpressionFlags : uint
    {
        IsSingleton = 1 << 0,
        IsTracked = 1 << 1,
        NeedsNullCheck = 1 << 2,
        IsDisposable = 1 << 3,
        IsArray = 1 << 4,
        IsLazy = 1 << 5,
        IsValueType = 1 << 6,
        IsOptional = 1 << 7,
        IsTransient = 1 << 8,
        IsCleanupDispose = 1 << 9,
        IsCleanupDestroy = 1 << 10,
    }

    void IIncrementalGenerator.Initialize(IncrementalGeneratorInitializationContext context)
        => context.RegisterSourceOutput(
            context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    fullyQualifiedMetadataName: InjectAttributeMetadataName,
                    predicate: static (node, _)
                        => node is MethodDeclarationSyntax syntax && !syntax.Modifiers.Any(SyntaxKind.AbstractKeyword),
                    transform: WrapTransform(TransformSyntaxContext)
                ),
            action: WrapGenerateSource<GeneratorInput>(GenerateSource)
        );

    static GeneratorInput TransformSyntaxContext(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetNode is not MethodDeclarationSyntax methodDeclaration)
            return default;

        var classDecl = methodDeclaration.FirstAncestorOrSelf<TypeDeclarationSyntax>();

        if (classDecl is null)
            return default;

        var output = new GeneratorInput
        {
            SourceGeneratorOutputFilename = GetOutputFilename(
                filePath: classDecl.Identifier.ValueText,
                targetFQN: methodDeclaration.Identifier.ValueText,
                label: InjectAttributeName
            ),
            ContainingTypeDeclaration = Utility.DeconstructTypeDeclaration(classDecl),
            MethodTextCheckSumForCache = methodDeclaration.GetText().GetChecksum().AsArray(),
        };

        if (context.SemanticModel.GetDeclaredSymbol(classDecl, ct) is not { TypeKind: TypeKind.Class } classSymbol)
            return output;

        if (context.TargetNode.SyntaxTree.GetRoot(ct) is CompilationUnitSyntax compilationUnit)
        {
            var list = new List<string>();

            foreach (var usingDirectiveSyntax in compilationUnit.Usings)
                list.Add(usingDirectiveSyntax.ToString());

            output.NamespaceImports = list.ToArray();
        }

        output.ClassName = new(() => context.SemanticModel
            .GetDeclaredSymbol(classDecl, ct)
            ?.ToMinimalDisplayString(context.SemanticModel, methodDeclaration.SpanStart, MinimallyQualifiedFormat) ?? ""
        );
        output.MethodXmlDocId = new(() => context.TargetSymbol.GetDocumentationCommentId() ?? methodDeclaration.Identifier.ValueText);

        output.IsSealed = classDecl.Modifiers.Any(SyntaxKind.SealedKeyword);
        output.IsStatic = methodDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword);
        output.MakePublic = context.Attributes.FirstOrDefault()?.ConstructorArguments.Any(x => x.Value is true) is true;
        output.IsUnityEditorCompile = context.SemanticModel.IsDefined("UNITY_EDITOR");
        output.IsDebugCompile = context.SemanticModel.IsDefined("DEBUG");

        // defer the expensive calls to the source gen phase
        output.InitExpressionInfoArrayBuilderFunc = new(() =>
            methodDeclaration
                .DescendantNodes(x => x is MethodDeclarationSyntax or BlockSyntax or ArrowExpressionClauseSyntax or ExpressionStatementSyntax)
                .OfType<AssignmentExpressionSyntax>()
                .Where(x => x.Left is IdentifierNameSyntax && context.SemanticModel.GetSymbolInfo(x.Left, ct).Symbol is null)
                .Select(assignment =>
                    {
                        var typeSymbol = context.SemanticModel.GetTypeInfo(assignment.Right, ct).Type;
                        var typeSymbolForDisplayName = typeSymbol;
                        var flags = default(ExpressionFlags);
                        var expression = assignment.Right.ToString().Replace("\r", "").Replace("\n", "").AsSpan().Trim();

                        if (assignment.Right is ImplicitObjectCreationExpressionSyntax { ArgumentList.Arguments: [{ Expression: { } expr }, ..] })
                        {
                            if (GetDelegateReturnType(expr) is { } exprType)
                            {
                                string lazyExpr = exprType.IsValueType ? "Medicine.LazyVal`1" : "Medicine.LazyRef`1";
                                typeSymbol = context.SemanticModel.Compilation.GetTypeByMetadataName(lazyExpr)?.Construct(exprType);
                                typeSymbolForDisplayName = exprType;
                                flags.Set(IsLazy, true);
                            }
                        }
                        else if (assignment.Right is LambdaExpressionSyntax or MemberAccessExpressionSyntax or GenericNameSyntax or NameSyntax)
                        {
                            if (GetDelegateReturnType(assignment.Right) is { } exprType)
                            {
                                typeSymbol = context.SemanticModel.Compilation.GetTypeByMetadataName("System.Func`1")?.Construct(exprType);
                                typeSymbolForDisplayName = exprType;
                                flags.Set(IsTransient, true);
                            }
                        }

                        if (typeSymbol is { IsStatic: true })
                            typeSymbol = null;

                        ITypeSymbol? GetDelegateReturnType(ExpressionSyntax delegateExpression)
                            => delegateExpression switch
                            {
                                LambdaExpressionSyntax { Body: InvocationExpressionSyntax body }
                                    => context.SemanticModel.GetTypeInfo(body, ct).Type,
                                LambdaExpressionSyntax { Body: BlockSyntax body }
                                    => body.DescendantNodes()
                                        .OfType<ReturnStatementSyntax>()
                                        .Select(x => x.Expression)
                                        .Where(x => x is not null)
                                        .Select(x => context.SemanticModel.GetTypeInfo(x!, ct).Type)
                                        .FirstOrDefault(x => x is not null),
                                MemberAccessExpressionSyntax or GenericNameSyntax or NameSyntax
                                    => context.SemanticModel.GetSymbolInfo(delegateExpression, ct)
                                        .CandidateSymbols
                                        .OfType<IMethodSymbol>()
                                        .FirstOrDefault(x => x.Parameters is not { Length: > 0 })
                                        ?.ReturnType,
                                _ => null,
                            };

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

                        bool MatchOptionWithExpression(ref ReadOnlySpan<char> expression, out string argument, ReadOnlySpan<char> pattern)
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

                        bool IsStaticPropertyAccess(ExpressionSyntax expression, string classAttribute, string propertyName)
                            => expression is MemberAccessExpressionSyntax
                               {
                                   Expression: IdentifierNameSyntax classIdentifier,
                                   Name.Identifier.ValueText: { Length: > 0 } propertyNameIdentifierText,
                               }
                               && propertyNameIdentifierText == propertyName
                               && context.SemanticModel.GetSymbolInfo(classIdentifier, ct).Symbol is ITypeSymbol classTypeSymbol
                               && classTypeSymbol.HasAttribute(classAttribute);

                        string? cleanupExpression = null;

                        while (true)
                        {
                            if (MatchOption(ref expression, IsOptional, ".Optional()".AsSpan()))
                                continue;

                            if (MatchOptionWithExpression(ref expression, out string cleanup, "Cleanup".AsSpan()))
                            {
                                cleanupExpression = cleanup;
                                continue;
                            }

                            break;
                        }

                        bool isSingleton = IsStaticPropertyAccess(assignment.Right, SingletonAttributeFQN, "Instance");
                        bool isTracked = IsStaticPropertyAccess(assignment.Right, TrackAttributeFQN, "Instances");
                        flags.Set(IsSingleton, isSingleton);
                        flags.Set(IsTracked, isTracked);

                        if (isSingleton || isTracked)
                        {
                            if (assignment.Right is MemberAccessExpressionSyntax memberAccessExpressionSyntax)
                                if (context.SemanticModel.GetSymbolInfo(memberAccessExpressionSyntax.Expression, ct).Symbol is ITypeSymbol accessedExpressionSymbol)
                                    typeSymbol = typeSymbolForDisplayName = accessedExpressionSymbol;
                        }

                        if ((isTracked, typeSymbol) is (true, { Name: "TrackedInstances<T>.WithImmediateCopy", ContainingType.TypeArguments: [var trackedInstancesType] }))
                            typeSymbolForDisplayName = trackedInstancesType;
                        else if ((isTracked, typeSymbol) is (true, INamedTypeSymbol { TypeArguments: [var first2] }))
                            typeSymbolForDisplayName = first2;

                        if (isSingleton)
                        {
                            var first = assignment.Right
                                .DescendantNodesAndSelf()
                                .FirstOrDefault(n =>
                                    n is MemberAccessExpressionSyntax
                                    {
                                        Expression: IdentifierNameSyntax singletonClassIdentifier,
                                        Name.Identifier.ValueText: "Instance",
                                    }
                                );

                            if (first is MemberAccessExpressionSyntax memberAccessExpressionSyntax)
                                if (context.SemanticModel.GetTypeInfo(memberAccessExpressionSyntax.Expression, ct).Type is { } symbol)
                                    typeSymbol = symbol;
                        }
                        else if (isTracked)
                        {
                            var first = assignment.Right
                                .DescendantNodesAndSelf()
                                .FirstOrDefault(n =>
                                    n is MemberAccessExpressionSyntax
                                    {
                                        Expression: IdentifierNameSyntax singletonClassIdentifier,
                                        Name.Identifier.ValueText: "Instances",
                                    }
                                );

                            if (first is MemberAccessExpressionSyntax memberAccessExpressionSyntax)
                                if (context.SemanticModel.GetTypeInfo(memberAccessExpressionSyntax.Expression, ct).Type is { } symbol)
                                    typeSymbol = symbol;
                        }

                        string typeDisplayName
                            = typeSymbolForDisplayName?.ToDisplayString(MinimallyQualifiedFormat) ?? "";

                        string? typeFQN
                            = typeSymbol
                                .GetSafeSymbolName(context.SemanticModel, assignment.Right.SpanStart) is { Length : > 0 } name
                                ? name.TrimEnd('?')
                                : null;

                        flags.Set(
                            NeedsNullCheck,
                            value: typeSymbol?.IsReferenceType is true &&
                                   assignment.Right is not BaseObjectCreationExpressionSyntax &&
                                   typeSymbol is not IArrayTypeSymbol &&
                                   !flags.Has(IsOptional) &&
                                   !flags.Has(IsTransient) &&
                                   !isSingleton &&
                                   !isTracked
                        );

                        flags.Set(
                            flag: IsOptional,
                            value: flags.Has(IsOptional) &&
                                   !flags.Has(IsArray) &&
                                   !flags.Has(IsTransient)
                        );

                        flags.Set(IsValueType, typeSymbol?.IsValueType is true);
                        flags.Set(IsArray, typeSymbol is IArrayTypeSymbol);
                        flags.Set(IsDisposable, typeSymbol.HasInterface("global::System.IDisposable"));

                        return new InitExpressionInfo
                        {
                            PropertyName = assignment.Left.ToString().Trim(),
                            InitExpression = expression.MakeString(),
                            CleanupExpression = cleanupExpression,
                            TypeFQN = typeFQN,
                            TypeDisplayName = typeDisplayName,
                            TypeXmlDocId = typeSymbol?.GetDocumentationCommentId(),
                            Location = assignment.GetLocation(),
                            Flags = flags,
                        };
                    }
                )
                .ToArray()
        );

        return output;
    }

    void GenerateSource(SourceProductionContext context, GeneratorInput input)
    {
        var expressions = input.InitExpressionInfoArrayBuilderFunc.Value();

        foreach (var group in expressions.GroupBy(x => x.PropertyName).Where(x => x.Count() > 1))
        foreach (var x in group)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    descriptor: MED006,
                    location: x.Location,
                    group.Key
                )
            );
        }

        string methodXmlDocId = input.MethodXmlDocId.Value();
        string className = input.ClassName.Value();

        var deferredLines = new List<string>();
        void Defer(string line)
            => deferredLines.Add(line);
        void DeferLinebreak()
            => deferredLines.Add("");

        Line.Append("#pragma warning disable CS0628 // New protected member declared in sealed type");
        Line.Append("#pragma warning disable CS0108 // Member hides inherited member; missing new keyword");
        Line.Append("#pragma warning disable CS0618 // Type or member is obsolete");
        Linebreak();

        Line.Append(Alias.UsingInline);
        Line.Append(Alias.UsingUtility);
        Line.Append(Alias.UsingFind);
        Line.Append(Alias.UsingStorage);
        Line.Append(Alias.UsingDebug);

        string access = input switch
        {
            { MakePublic: true } => "public ",
            { IsSealed: false }  => "protected ",
            _                    => "",
        };

        string @private = access is "" ? "" : "private ";
        string @static = input.IsStatic ? "static " : "";

        Linebreak();
        foreach (var @using in input.NamespaceImports)
            Line.Append(@using);

        Linebreak();

        foreach (var x in input.ContainingTypeDeclaration)
        {
            Line.Append(x);
            Line.Append('{');
            IncreaseIndent();
        }

        foreach (var x in expressions)
        {
            string nul = x.Flags.Has(IsValueType) ? "" : "?";
            string opt = x.Flags.Has(IsOptional) ? "?" : "";
            string exc = x.Flags.Has(IsOptional) ? "" : "!";

            void AppendInjectionDeclaredIn()
            {
                Line.Append($"/// <injected>");
                Line.Append($"/// Injection declared on line {x.Location.Value.GetLineSpan().StartLinePosition.Line + 1} in <see cref=\"{methodXmlDocId}\"/>.");
                Line.Append($"/// </injected>");
            }

            void OpenListAndAppendOptionalDescription(string nullIf)
            {
                if (x.Flags.Has(IsOptional))
                    Line.Append($"/// This code-generated property is marked as <c>.Optional()</c>:");
                else if (x.Flags.Has(NeedsNullCheck))
                    Line.Append($"/// This code-generated property is checked for <c>null</c>:");

                Line.Append($"/// <list type=\"bullet\">");
                if (x.Flags.Has(IsOptional))
                {
                    Line.Append($"/// <item>This property will <b>silently</b> return <c>null</c> if {nullIf}.</item>");
                    Line.Append($"/// <item>Remove <c>.Optional()</c> from the end of the assignment to re-enable the null check + error log. </item>");
                }
                else if (x.Flags.Has(NeedsNullCheck))
                {
                    Line.Append($"/// <item>This property <b>will log an error</b> and return <c>null</c> if {nullIf}.</item>");
                    Line.Append($"/// <item>Append <c>.Optional()</c> at the end of the assignment to suppress the null check + error log. </item>");
                }
                else if (x.Flags.Has(IsArray))
                {
                    Line.Append($"/// <item>This property will never return <c>null</c> - will always fall back to an empty array.</item>");
                }
            }

            // handle unrecognized types
            if (x.TypeFQN is null)
            {
                Line.Append($"/// <summary>");
                Line.Append($"/// <p><b>The source generator was unable to determine the type of this property.</b></p>");
                Line.Append($"/// <p>Make sure that the assignment expression is correct, and that it isn't referring to other code-generated properties.</p>");
                Line.Append($"/// </summary>");
                AppendInjectionDeclaredIn();
                Line.Append($"{@static}object {x.PropertyName};");
                Linebreak();

                context.ReportDiagnostic(
                    Diagnostic.Create(
                        descriptor: MED014,
                        location: x.Location,
                        x.PropertyName
                    )
                );
            }
            // handle singleton classes
            else if (x.Flags.Has(IsSingleton))
            {
                if (input.IsUnityEditorCompile)
                {
                    Line.Append($"/// <summary> Provides access to the active <see cref=\"{x.TypeXmlDocId}\"/> singleton instance. </summary>");
                    Line.Append($"/// <remarks>");
                    OpenListAndAppendOptionalDescription(nullIf: "the singleton instance could not be found");
                    Line.Append($"/// </list>");
                    Line.Append($"/// Additional notes:");
                    Line.Append($"/// <inheritdoc cref=\"{x.TypeFQN}.Instance\"/>");
                    Line.Append($"/// </remarks>");
                    AppendInjectionDeclaredIn();
                }

                if (!x.Flags.Has(IsOptional))
                    Line.Append("[global::System.Diagnostics.CodeAnalysis.AllowNull, global::JetBrains.Annotations.CanBeNull]");

                Line.Append($"{access}{@static}{x.TypeFQN}{opt} {x.PropertyName}");
                using (Braces)
                {
                    // always get a fresh singleton instance
                    if (x.Flags.Has(NeedsNullCheck))
                    {
                        // check that the singleton exists and if not, log error
                        Line.Append("get");
                        using (Braces)
                        {
                            Line.Append($"var instance = {x.TypeFQN}.Instance;");
                            Line.Append($"if (!{m}Utility.IsNativeObjectAlive(instance))");
                            using (Indent)
                                Line.Append($"{m}Debug.LogError($\"No registered singleton instance: {x.TypeDisplayName}\");");

                            Line.Append($"return instance{exc};");
                        }
                    }
                    else
                    {
                        // just return
                        Line.Append($"{Alias.Inline} get => {x.TypeFQN}.Instance;");
                    }

                    // discard the assign - only need this for type resolution
                    Line.Append($"{Alias.Inline} set {{ }}");
                }
            }
            // handle tracked classes
            else if (x.Flags.Has(IsTracked))
            {
                if (input.IsUnityEditorCompile)
                {
                    Line.Append($"/// <inheritdoc cref=\"{x.TypeFQN}.Instances\"/>");
                    AppendInjectionDeclaredIn();
                }

                Line.Append($"{access}{@static}global::Medicine.Internal.TrackedInstances<{x.TypeFQN}> {x.PropertyName}");
                using (Braces)
                {
                    // we can save some memory by omitting the backing field when the struct is a static accessor with no stored state
                    Line.Append($"{Alias.Inline} get => default;");

                    // discard the assign - only need this for type resolution
                    Line.Append($"{Alias.Inline} set {{ }}");
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

                if (input.IsUnityEditorCompile)
                {
                    Line.Append($"/// <summary> Cached <see cref=\"{x.TypeXmlDocId}\"/> {label}.");
                    Line.Append($"/// <br/>Initialized from expression: <c>{x.InitExpression.HtmlEncode()}</c></summary>");
                    Line.Append($"/// <remarks>");
                    OpenListAndAppendOptionalDescription(nullIf: "the component could not be found");
                    if (x.TypeFQN?.StartsWith("global::Medicine.Internal.ComponentEnumerable<", Ordinal) is true)
                        Line.Append($"/// <item> This struct lazily enumerates all components of the given type.</item>");
                    else if (x.TypeFQN?.StartsWith("global::Medicine.Internal.ComponentsInSceneEnumerable<", Ordinal) is true)
                        Line.Append($"/// <item> This struct lazily enumerates all components of the given type that exist in the given scene.</item>");
                    else if (x.Flags.Has(IsLazy))
                        Line.Append($"/// <item> This property lazily evaluates the given expression.</item>");

                    Line.Append($"/// </list>");

                    Line.Append($"/// </remarks>");
                    AppendInjectionDeclaredIn();
                }

                if (!x.Flags.Has(IsOptional))
                    Line.Append("[global::System.Diagnostics.CodeAnalysis.AllowNull, global::JetBrains.Annotations.CanBeNull]");

                if (x.Flags.Has(IsValueType))
                {
                    Line.Append($"{access}{@static}ref {x.TypeFQN}{opt} {x.PropertyName}");
                    using (Braces)
                    {
                        Line.Append($"{Alias.Inline} get");
                        using (Braces)
                        {
                            if (input.IsUnityEditorCompile && !x.Flags.Has(IsDisposable))
                            {
                                Line.Append($"{Alias.NoInline} void {m}Expr() => {m}MedicineInternal._{m}{x.PropertyName} = {x.InitExpression};");
                                Line.Append($"if ({m}Utility.EditMode)");
                                using (Indent)
                                    Line.Append($"{m}Expr();"); // in edit mode, always call initializer
                            }

                            Line.Append($"return ref {m}MedicineInternal._{m}{x.PropertyName};");
                        }
                    }
                }
                else
                {
                    Line.Append($"{access}{@static}{x.TypeFQN}{opt} {x.PropertyName}");
                    using (Braces)
                    {
                        Line.Append($"{Alias.Inline} get");
                        using (Braces)
                        {
                            if (input.IsUnityEditorCompile && !x.Flags.Has(IsDisposable))
                            {
                                Line.Append($"{Alias.NoInline} {x.TypeFQN}{opt} {m}Expr() => {x.InitExpression};");
                                Line.Append($"if ({m}Utility.EditMode)");
                                using (Indent)
                                    Line.Append($"return {m}Expr();"); // in edit mode, always call initializer
                            }

                            Line.Append($"return {m}MedicineInternal._{m}{x.PropertyName}!;");
                        }

                        Line.Append($"{Alias.Inline} {@private}set");
                        using (Braces)
                        {
                            if (input.IsDebugCompile && x.Flags.Has(NeedsNullCheck))
                            {
                                Line.Append($"if (!{m}Utility.IsNativeObjectAlive(value))");
                                using (Indent)
                                    Line.Append($"{m}Debug.LogError($\"Missing component: {x.TypeDisplayName} in {className} '{{this.name}}'\", this);");
                            }

                            Line.Append($"{m}MedicineInternal._{m}{x.PropertyName} = value;");
                        }
                    }
                }

                // backing field
                Defer($"internal {@static}{x.TypeFQN}{nul} _{m}{x.PropertyName};");
                DeferLinebreak();

                if (x.CleanupExpression is not null)
                {
                    Defer($"internal static readonly global::System.Action<{x.TypeFQN}> _{m}{x.PropertyName}{m}CLEANUP = {x.CleanupExpression};");
                    DeferLinebreak();
                }
            }

            Linebreak();
        }

        Line.Append(Alias.Hidden);
        Line.Append(Alias.ObsoleteInternal);
        Line.Append($"partial struct {m}MedicineInternalBackingStorage");
        using (Braces)
        {
            foreach (var line in deferredLines)
            {
                if (line is { Length: >0})
                    Line.Append(line);
                else
                    Linebreak();
            }
        }

        Linebreak();
        Line.Append(Alias.Hidden);
        Line.Append(Alias.ObsoleteInternal);
        Line.Append($"{m}MedicineInternalBackingStorage {m}MedicineInternal;");

        if (expressions.Any(x => x.Flags.Any(IsCleanupDestroy | IsCleanupDispose) || x.CleanupExpression is not null))
        {
            Linebreak();
            Line.Append($"{access}void Cleanup()");
            using (Braces)
            {
                Line.Append($"if ({m}Utility.EditMode)");
                using (Indent)
                    Line.Append($"return;");

                foreach (var x in expressions)
                {
                    if (x.Flags.Has(IsCleanupDispose))
                        Line.Append(x.PropertyName).Append(".Dispose();");

                    if (x.Flags.Has(IsCleanupDestroy))
                        Line.Append($"Destroy({x.PropertyName});");

                    if (x.CleanupExpression is not null)
                        Line.Append($"{m}MedicineInternalBackingStorage._{m}{x.PropertyName}{m}CLEANUP({x.PropertyName}); // {x.CleanupExpression}");
                }
            }
        }

        foreach (var x in input.ContainingTypeDeclaration)
        {
            DecreaseIndent();
            Line.Append('}');
        }
    }
}