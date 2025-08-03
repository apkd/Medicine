using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static System.StringComparison;
using static ActivePreprocessorSymbolNames;
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
        public string? SourceGeneratorError { get; init; }
        public EquatableIgnore<Location> SourceGeneratorErrorLocation { get; set; }

        public ActivePreprocessorSymbolNames Symbols;
        public string? InjectMethodName;
        public bool IsSealed;
        public bool IsStatic;
        public bool? MakePublic;
        public int ForceDebug;
        public EquatableArray<string> NamespaceImports;
        public EquatableArray<string> ContainingTypeDeclaration;
        public EquatableIgnore<Func<string>> MethodXmlDocId;
        public EquatableIgnore<Func<string>> ClassName;
        public EquatableIgnore<Func<InitExpressionInfo[]>> InitExpressionInfoArrayBuilderFunc = new(() => []);

        // ReSharper disable once NotAccessedField.Local
        public EquatableArray<byte> MethodTextCheckSumForCache;
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
        IsTransient = 1 << 11,
        IsCleanupDispose = 1 << 12,
        IsCleanupDestroy = 1 << 13,
    }

    void IIncrementalGenerator.Initialize(IncrementalGeneratorInitializationContext context)
    {
        var medicineSettings = context.CompilationProvider
            .Combine(context.ParseOptionsProvider)
            .Select((x, ct) => new MedicineSettings(x));

        var syntaxProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: InjectAttributeMetadataName,
                predicate: static (node, _)
                    => node is MethodDeclarationSyntax syntax && !syntax.Modifiers.Any(SyntaxKind.AbstractKeyword),
                transform: WrapTransform(TransformSyntaxContext)
            );

        context.RegisterSourceOutput(
            source: syntaxProvider.Combine(medicineSettings).Select((x, ct) =>
            {
                var (input, settings) = x;
                input.MakePublic ??= settings.MakePublic;
                input.Symbols = settings.PreprocessorSymbolNames;
                return input;
            }),
            action: WrapGenerateSource<GeneratorInput>(GenerateSource)
        );
    }

    static GeneratorInput TransformSyntaxContext(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetNode is not MethodDeclarationSyntax methodDeclaration)
            return default;

        var classDecl = methodDeclaration.FirstAncestorOrSelf<TypeDeclarationSyntax>();

        if (classDecl is null)
            return default;

        var output = new GeneratorInput
        {
            InjectMethodName = methodDeclaration.Identifier.Text,
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

        (output.MakePublic, output.ForceDebug) = context.Attributes.First()
            .GetAttributeConstructorArguments()
            .Select(x => (
                    makePublic: x.Get<bool>("makePublic", null),
                    forceDebug: x.Get("forceDebug", 0)
                )
            );

        output.IsSealed = classDecl.Modifiers.Any(SyntaxKind.SealedKeyword);
        output.IsStatic = methodDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword);

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
                                LambdaExpressionSyntax { Body: ExpressionSyntax body }
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

                        const string name1 = "TrackedInstances<T>.ImmediateEnumerable";
                        const string name2 = "TrackedInstances<T>.StrideEnumerable";
                        if ((isTracked, typeSymbol) is (true, { Name: name1 or name2, ContainingType.TypeArguments: [var containingTypeArg] }))
                            typeSymbolForDisplayName = containingTypeArg;
                        else if ((isTracked, typeSymbol) is (true, INamedTypeSymbol { TypeArguments: [var typeArg] }))
                            typeSymbolForDisplayName = typeArg;

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
                                   // assignment.Right is not BaseObjectCreationExpressionSyntax &&
                                   typeSymbol is not IArrayTypeSymbol &&
                                   !flags.Has(IsOptional) &&
                                   !flags.Has(IsTransient) &&
                                   !isSingleton &&
                                   !isTracked
                        );

                        flags.Set(
                            IsUnityObject,
                            value: typeSymbol.InheritsFrom("global::UnityEngine.Object")
                        );

                        flags.Set(
                            IsUnityComponent,
                            value: typeSymbol.InheritsFrom("global::UnityEngine.Component")
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

        string storageSuffix = input.InjectMethodName is "Awake" ? "" : $"For{input.InjectMethodName}";
        string storagePropName = $"{m}MedicineInternal{storageSuffix}";
        string storageStructName = $"{m}MedicineInternalBackingStorage{storageSuffix}";

        string methodXmlDocId = input.MethodXmlDocId.Value();
        string className = input.ClassName.Value();

        var deferredLines = new List<string>();

        void Defer(string line)
            => deferredLines.Add(line);

        void DeferLinebreak()
            => deferredLines.Add("");

        Line.Write("#pragma warning disable CS0628 // New protected member declared in sealed type");
        Line.Write("#pragma warning disable CS0108 // Member hides inherited member; missing new keyword");
        Line.Write("#pragma warning disable CS0618 // Type or member is obsolete");
        Linebreak();

        Line.Write(Alias.UsingInline);
        Line.Write(Alias.UsingUtility);
        Line.Write(Alias.UsingFind);
        Line.Write(Alias.UsingStorage);
        Line.Write(Alias.UsingDebug);

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
            Line.Write(@using);

        Linebreak();

        foreach (var x in input.ContainingTypeDeclaration)
        {
            Line.Write(x);
            Line.Write('{');
            IncreaseIndent();
        }

        foreach (var x in expressions)
        {
            string nul = x.Flags.Has(IsValueType) ? "" : "?";
            string opt = x.Flags.Has(IsOptional) ? "?" : "";
            string exc = x.Flags.Has(IsOptional) ? "" : "!";

            void AppendInjectionDeclaredIn()
            {
                Line.Write($"/// <injected>");
                Line.Write($"/// Injection declared on line {x.Location.Value.GetLineSpan().StartLinePosition.Line + 1} in <see cref=\"{methodXmlDocId}\"/>.");
                Line.Write($"/// </injected>");
            }

            void OpenListAndAppendOptionalDescription(string nullIf)
            {
                if (x.Flags.Has(IsOptional))
                    Line.Write($"/// This code-generated property is marked as <c>.Optional()</c>:");
                else if (x.Flags.Has(NeedsNullCheck))
                    Line.Write($"/// This code-generated property is checked for <c>null</c>:");

                Line.Write($"/// <list type=\"bullet\">");
                if (x.Flags.Has(IsOptional))
                {
                    Line.Write($"/// <item>This property will <b>silently</b> return <c>null</c> if {nullIf}.</item>");
                    Line.Write($"/// <item>Remove <c>.Optional()</c> from the end of the assignment to re-enable the null check + error log. </item>");
                }
                else if (x.Flags.Has(NeedsNullCheck))
                {
                    Line.Write($"/// <item>This property <b>will log an error</b> and return <c>null</c> if {nullIf}.</item>");
                    Line.Write($"/// <item>Append <c>.Optional()</c> at the end of the assignment to suppress the null check + error log. </item>");
                }
                else if (x.Flags.Has(IsArray))
                {
                    Line.Write($"/// <item>This property will never return <c>null</c> - will always fall back to an empty array.</item>");
                }
            }

            // handle unrecognized types
            if (x.TypeFQN is null)
            {
                Line.Write($"/// <summary>");
                Line.Write($"/// <p><b>The source generator was unable to determine the type of this property.</b></p>");
                Line.Write($"/// <p>Make sure that the assignment expression is correct, and that it isn't referring to other code-generated properties.</p>");
                Line.Write($"/// </summary>");
                AppendInjectionDeclaredIn();
                Line.Write($"{@static}object {x.PropertyName};");
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
                if (input.Symbols.Has(UNITY_EDITOR))
                {
                    Line.Write($"/// <summary> Provides access to the active <see cref=\"{x.TypeXmlDocId}\"/> singleton instance. </summary>");
                    Line.Write($"/// <remarks>");
                    OpenListAndAppendOptionalDescription(nullIf: "the singleton instance could not be found");
                    Line.Write($"/// </list>");
                    Line.Write($"/// Additional notes:");
                    Line.Write($"/// <inheritdoc cref=\"{x.TypeFQN}.Instance\"/>");
                    Line.Write($"/// </remarks>");
                    AppendInjectionDeclaredIn();
                }

                if (!x.Flags.Has(IsOptional))
                    Line.Write("[global::System.Diagnostics.CodeAnalysis.AllowNull, global::JetBrains.Annotations.CanBeNull]");

                Line.Write($"{access}{@static}{x.TypeFQN}{opt} {x.PropertyName}");
                using (Braces)
                {
                    // always get a fresh singleton instance
                    if (x.Flags.Has(NeedsNullCheck))
                    {
                        // check that the singleton exists and if not, log error
                        Line.Write("get");
                        using (Braces)
                        {
                            Line.Write($"var instance = {x.TypeFQN}.Instance;");
                            Line.Write($"if (!{m}Utility.IsNativeObjectAlive(instance))");
                            using (Indent)
                                Line.Write($"{m}Debug.LogError($\"No registered singleton instance: {x.TypeDisplayName}\");");

                            Line.Write($"return instance{exc};");
                        }
                    }
                    else
                    {
                        // just return
                        Line.Write($"{Alias.Inline} get => {x.TypeFQN}.Instance;");
                    }

                    // discard the assign - only need this for type resolution
                    Line.Write($"{Alias.Inline} set {{ }}");
                }
            }
            // handle tracked classes
            else if (x.Flags.Has(IsTracked))
            {
                if (input.Symbols.Has(UNITY_EDITOR))
                {
                    Line.Write($"/// <inheritdoc cref=\"{x.TypeFQN}.Instances\"/>");
                    AppendInjectionDeclaredIn();
                }

                Line.Write($"{access}{@static}global::Medicine.Internal.TrackedInstances<{x.TypeFQN}> {x.PropertyName}");
                using (Braces)
                {
                    // we can save some memory by omitting the backing field when the struct is a static accessor with no stored state
                    Line.Write($"{Alias.Inline} get => default;");

                    // discard the assign - only need this for type resolution
                    Line.Write($"{Alias.Inline} set {{ }}");
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

                if (input.Symbols.Has(UNITY_EDITOR))
                {
                    Line.Write($"/// <summary> Cached <see cref=\"{x.TypeXmlDocId}\"/> {label}.");
                    Line.Write($"/// <br/>Initialized from expression: <c>{x.InitExpression.HtmlEncode()}</c></summary>");
                    Line.Write($"/// <remarks>");
                    OpenListAndAppendOptionalDescription(nullIf: "the component could not be found");
                    if (x.TypeFQN?.StartsWith("global::Medicine.Internal.ComponentEnumerable<", Ordinal) is true)
                        Line.Write($"/// <item> This struct lazily enumerates all components of the given type.</item>");
                    else if (x.TypeFQN?.StartsWith("global::Medicine.Internal.ComponentsInSceneEnumerable<", Ordinal) is true)
                        Line.Write($"/// <item> This struct lazily enumerates all components of the given type that exist in the given scene.</item>");
                    else if (x.Flags.Has(IsLazy))
                        Line.Write($"/// <item> This property lazily evaluates the given expression.</item>");

                    Line.Write($"/// </list>");

                    Line.Write($"/// </remarks>");
                    AppendInjectionDeclaredIn();
                }

                if (!x.Flags.Has(IsOptional))
                    Line.Write("[global::System.Diagnostics.CodeAnalysis.AllowNull, global::JetBrains.Annotations.CanBeNull]");

                if (x.Flags.Has(IsValueType))
                {
                    Line.Write($"{access}{@static}ref {x.TypeFQN}{opt} {x.PropertyName}");
                    using (Braces)
                    {
                        Line.Write($"{Alias.Inline} get");
                        using (Braces)
                        {
                            if (input.Symbols.Has(UNITY_EDITOR) && !x.Flags.Has(IsDisposable))
                            {
                                Line.Write($"{Alias.NoInline} void {m}Expr() => {storagePropName}._{m}{x.PropertyName} = {x.InitExpression};");
                                Line.Write($"if ({m}Utility.EditMode)");
                                using (Indent)
                                    Line.Write($"{m}Expr();"); // in edit mode, always call initializer
                            }

                            Line.Write($"return ref {storagePropName}._{m}{x.PropertyName};");
                        }
                    }
                }
                else
                {
                    Line.Write($"{access}{@static}{x.TypeFQN}{opt} {x.PropertyName}");
                    using (Braces)
                    {
                        Line.Write($"{Alias.Inline} get");
                        using (Braces)
                        {
                            if (input.Symbols.Has(UNITY_EDITOR) && !x.Flags.Has(IsDisposable))
                            {
                                Line.Write($"{Alias.NoInline} {x.TypeFQN}{opt} {m}Expr() => {x.InitExpression};");
                                Line.Write($"if ({m}Utility.EditMode)");
                                using (Indent)
                                    Line.Write($"return {m}Expr();"); // in edit mode, always call initializer
                            }

                            Line.Write($"return {storagePropName}._{m}{x.PropertyName}!;");
                        }

                        Line.Write($"{Alias.Inline} {@private}set");
                        using (Braces)
                        {
                            if (input.Symbols.Has(DEBUG) && x.Flags.Has(NeedsNullCheck))
                            {
                                if (x.Flags.Has(IsUnityObject))
                                    Line.Write($"if (!{m}Utility.IsNativeObjectAlive(value))");
                                else
                                    Line.Write($"if (value is null)");

                                string typeLabel = x.Flags switch
                                {
                                    _ when x.Flags.Has(IsUnityComponent)        => "component",
                                    _ when x.Flags.Has(IsUnityScriptableObject) => "scriptable object",
                                    _                                           => "object",
                                };

                                using (Indent)
                                    Line.Write($"{m}Debug.LogError($\"Missing {typeLabel}: {x.TypeDisplayName} '{x.PropertyName}' in {className} '{{this.name}}'\", this);");
                            }

                            Line.Write($"{storagePropName}._{m}{x.PropertyName} = value;");
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

        Line.Write(Alias.Hidden);
        Line.Write(Alias.ObsoleteInternal);
        Line.Write($"partial struct {storageStructName}");
        using (Braces)
        {
            foreach (var line in deferredLines)
            {
                if (line is { Length: > 0 })
                    Line.Write(line);
                else
                    Linebreak();
            }
        }

        Linebreak();
        Line.Write(Alias.Hidden);
        Line.Write(Alias.ObsoleteInternal);
        Line.Write($"{storageStructName} {storagePropName};");

        if (expressions.Any(x => x.Flags.Any(IsCleanupDestroy | IsCleanupDispose) || x.CleanupExpression is not null))
        {
            Linebreak();
            Line.Write($"{access}void Cleanup()");
            using (Braces)
            {
                Line.Write($"if ({m}Utility.EditMode)");
                using (Indent)
                    Line.Write($"return;");

                foreach (var x in expressions)
                {
                    if (x.Flags.Has(IsCleanupDispose))
                        Line.Write($"{x.PropertyName}.Dispose();");

                    if (x.Flags.Has(IsCleanupDestroy))
                        Line.Write($"Destroy({x.PropertyName});");

                    if (x.CleanupExpression is not null)
                        Line.Write($"{storageStructName}._{m}{x.PropertyName}{m}CLEANUP({x.PropertyName}); // {x.CleanupExpression}");
                }
            }
        }

        foreach (var x in input.ContainingTypeDeclaration)
        {
            DecreaseIndent();
            Line.Write('}');
        }
    }
}