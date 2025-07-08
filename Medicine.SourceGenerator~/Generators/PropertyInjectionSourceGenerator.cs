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

    record struct GeneratorInput(
        string SourceGeneratorOutputFilename,
        string SourceGeneratorError,
        CacheIgnore<List<string>> SourceGeneratorDiagnostics,
        string ClassName,
        bool IsUnityEditorCompile,
        bool IsDebugCompile,
        bool IsSealed,
        bool IsStatic,
        bool MakePublic,
        EquatableArray<string> NamespaceImports,
        EquatableArray<string> ContainingTypeDeclaration,
        // ReSharper disable once NotAccessedPositionalProperty.Local
        string MethodRawStringForCache,
        CacheIgnore<Func<InitExpressionInfo[]>?> InitExpressionInfoArrayBuilderFunc
    ) : IGeneratorInput;

    record struct InitExpressionInfo(
        string PropertyName,
        string InitExpression,
        string? TypeFQN,
        string TypeDisplayName,
        CacheIgnore<Location> Location,
        ExpressionFlags Flags
    );

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

        var containingClass = methodDeclaration.FirstAncestorOrSelf<TypeDeclarationSyntax>();

        if (containingClass is null)
            return default;

        var output = new GeneratorInput
        {
            SourceGeneratorOutputFilename = GetOutputFilename(
                filePath: containingClass.Identifier.ToString(),
                targetFQN: context.TargetSymbol.ToDisplayString(FullyQualifiedFormat),
                label: InjectAttributeName
            ),
            ContainingTypeDeclaration = Utility.DeconstructTypeDeclaration(containingClass),
            MethodRawStringForCache = methodDeclaration.ToString(),
        };

        if (context.SemanticModel.GetDeclaredSymbol(containingClass, ct) is not { TypeKind: TypeKind.Class } classSymbol)
            return output;

        if (context.TargetNode.SyntaxTree.GetRoot(ct) is CompilationUnitSyntax compilationUnit)
        {
            output.NamespaceImports
                = compilationUnit
                    .Usings
                    .Select(x => x.ToString())
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToArray();
        }

        output.ClassName = classSymbol.ToMinimalDisplayString(context.SemanticModel, methodDeclaration.SpanStart, MinimallyQualifiedFormat);
        output.IsSealed = classSymbol.IsSealed;
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

                        if (assignment.Right is ImplicitObjectCreationExpressionSyntax { ArgumentList.Arguments: [{ Expression: { } expr }, ..] })
                        {
                            ITypeSymbol? exprType = expr switch
                            {
                                LambdaExpressionSyntax { Body: { } body }
                                    => context.SemanticModel.GetTypeInfo(body, ct).Type,
                                MemberAccessExpressionSyntax or GenericNameSyntax or NameSyntax
                                    => context.SemanticModel.GetSymbolInfo(expr, ct)
                                        .CandidateSymbols
                                        .OfType<IMethodSymbol>()
                                        .FirstOrDefault(x => x.Parameters is not { Length: > 0 })
                                        .ReturnType,
                                _ => null,
                            };

                            if (exprType != null)
                            {
                                string lazyExpr = exprType.IsValueType ? "Medicine.LazyVal`1" : "Medicine.LazyRef`1";
                                typeSymbol = context.SemanticModel.Compilation.GetTypeByMetadataName(lazyExpr)?.Construct(exprType);
                                typeSymbolForDisplayName = exprType;
                                flags.Set(IsLazy, true);
                            }
                        }

                        if (typeSymbol is { IsStatic: true })
                        {
                            typeSymbol = null;
                        }

                        var expression = assignment.Right.ToString().AsSpan().Trim();

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

                        while (true)
                        {
                            if (MatchOption(ref expression, IsOptional, ".Optional()".AsSpan()))
                                continue;

                            if (MatchOption(ref expression, IsTransient, ".Transient()".AsSpan()))
                                continue;

                            if (MatchOption(ref expression, IsCleanupDispose, ".CleanupDispose()".AsSpan()))
                                continue;

                            if (MatchOption(ref expression, IsCleanupDestroy, ".CleanupDestroy()".AsSpan()))
                                continue;

                            break;
                        }

                        string trimmedExpression = expression.MakeString();

                        bool isSingleton = assignment.Right.DescendantNodesAndSelf()
                            .Count(n => n is MemberAccessExpressionSyntax
                                {
                                    Expression: IdentifierNameSyntax singletonClassIdentifier,
                                    Name.Identifier.ValueText: "Instance",
                                } && context.SemanticModel.GetSymbolInfo(singletonClassIdentifier, ct).Symbol is ITypeSymbol symbol
                                  && symbol.HasAttribute(SingletonAttributeFQN)
                            ) is 1;

                        bool isTracked = !isSingleton && assignment.Right.DescendantNodesAndSelf()
                            .Count(n => n is MemberAccessExpressionSyntax
                                {
                                    Expression: IdentifierNameSyntax trackedClassIdentifier,
                                    Name.Identifier.ValueText: "Instances",
                                } && context.SemanticModel.GetSymbolInfo(trackedClassIdentifier, ct).Symbol is ITypeSymbol symbol
                                  && symbol.HasAttribute(TrackAttributeFQN)
                            ) is 1;

                        if ((isTracked, typeSymbol) is (true, { Name: "TrackedInstances<T>.WithImmediateCopy", ContainingType.TypeArguments: [var trackedInstancesType] }))
                            typeSymbolForDisplayName = trackedInstancesType;
                        else if ((isTracked, typeSymbol) is (true, INamedTypeSymbol { TypeArguments: [var first2] }))
                            typeSymbolForDisplayName = first2;

                        flags.Set(IsSingleton, isSingleton);
                        flags.Set(IsTracked, isTracked);

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
                                   !isSingleton &&
                                   !isTracked
                        );

                        flags.Set(IsValueType, typeSymbol?.IsValueType is true);
                        flags.Set(IsArray, typeSymbol is IArrayTypeSymbol);
                        flags.Set(IsOptional, flags.Has(IsOptional) && !flags.Has(IsArray));
                        flags.Set(IsDisposable, typeSymbol.HasInterface("global::System.IDisposable"));

                        return new InitExpressionInfo(
                            PropertyName: assignment.Left.ToString(),
                            InitExpression: trimmedExpression,
                            TypeFQN: typeFQN,
                            TypeDisplayName: typeDisplayName,
                            Location: assignment.GetLocation(),
                            Flags: flags
                        );
                    }
                )
                .ToArray()
        );

        return output;
    }

    void GenerateSource(SourceProductionContext context, GeneratorInput input)
    {
        var expressions = input.InitExpressionInfoArrayBuilderFunc.Value?.Invoke() ?? [];

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

        Line.Append("#pragma warning disable CS0628 // New protected member declared in sealed type");
        Line.Append("#pragma warning disable CS0108 // Member hides inherited member; missing new keyword");
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
            string nul = x.Flags.Has(IsOptional) ? "?" : "";
            string exc = x.Flags.Has(IsOptional) ? "" : "!";

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

            // handle singleton classes
            if (x.TypeFQN is null)
            {
                Line.Append($"/// <summary>");
                Line.Append($"/// <p><b>The source generator was unable to determine the type of this property.</b></p>");
                Line.Append($"/// <p>Make sure that the assignment expression is correct, and that it isn't referring to other code-generated properties.</p>");
                Line.Append($"/// </summary>");
                Line.Append($"{@static}object {x.PropertyName};");

                context.ReportDiagnostic(
                    Diagnostic.Create(
                        descriptor: MED014,
                        location: x.Location,
                        x.PropertyName
                    )
                );
            }
            else if (x.Flags.Has(IsSingleton))
            {
                if (input.IsUnityEditorCompile)
                {
                    Line.Append($"/// <summary> Provides access to the active <c>{x.TypeDisplayName.HtmlEncode()}</c> singleton instance. </summary>");
                    Line.Append($"/// <remarks>");
                    OpenListAndAppendOptionalDescription(nullIf: "the singleton instance could not be found");
                    Line.Append($"/// </list>");
                    Line.Append($"/// Additional notes:");
                    Line.Append($"/// <inheritdoc cref=\"{x.TypeFQN}.Instance\"/>");
                    Line.Append($"/// </remarks>");
                }

                if (!x.Flags.Has(IsOptional))
                    Line.Append("[global::System.Diagnostics.CodeAnalysis.AllowNull, global::JetBrains.Annotations.CanBeNull]");

                Line.Append($"{access}{@static}{x.TypeFQN}{nul} {x.PropertyName}");
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
                    Line.Append($"/// <inheritdoc cref=\"{x.TypeFQN}.Instances\"/>");

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
                    Line.Append($"/// <summary> Cached <c>{x.TypeDisplayName.HtmlEncode()}</c> {label}.");
                    Line.Append($"/// <br/>Initialized from expression: <c>{x.InitExpression.HtmlEncode()}</c> </summary>");
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
                }

                if (!x.Flags.Has(IsOptional))
                    Line.Append("[global::System.Diagnostics.CodeAnalysis.AllowNull, global::JetBrains.Annotations.CanBeNull]");

                if (x.Flags.Has(IsTransient))
                {
                    Line.Append($"{access}{@static}{x.TypeFQN}{nul} {x.PropertyName}");
                    using (Braces)
                        Line.Append($"{Alias.Inline} get => {x.InitExpression}");
                }

                if (x.Flags.Has(IsValueType))
                {
                    Line.Append($"{access}{@static}ref {x.TypeFQN}{nul} {x.PropertyName}");
                    using (Braces)
                    {
                        Line.Append($"{Alias.Inline} get");
                        using (Braces)
                        {
                            if (input.IsUnityEditorCompile)
                            {
                                Line.Append($"{Alias.NoInline} void {m}Init() => _{m}{x.PropertyName} = {x.InitExpression};");
                                Line.Append($"if ({m}Utility.EditMode)");
                                using (Indent)
                                    Line.Append($"{m}Init();"); // in edit mode, always call initializer
                            }

                            Line.Append($"return ref _{m}{x.PropertyName};");
                        }
                    }
                }
                else
                {
                    Line.Append($"{access}{@static}{x.TypeFQN}{nul} {x.PropertyName}");
                    using (Braces)
                    {
                        Line.Append($"{Alias.Inline} get");
                        using (Braces)
                        {
                            if (input.IsUnityEditorCompile)
                            {
                                Line.Append($"{Alias.NoInline} {x.TypeFQN}{nul} {m}Init() => {x.InitExpression};");
                                Line.Append($"if ({m}Utility.EditMode)");
                                using (Indent)
                                    Line.Append($"return {m}Init();"); // in edit mode, always call initializer
                            }

                            Line.Append($"return _{m}{x.PropertyName};");
                        }

                        Line.Append($"{Alias.Inline} {@private}set");
                        using (Braces)
                        {
                            if (input.IsDebugCompile && x.Flags.Has(NeedsNullCheck))
                            {
                                Line.Append($"if (!{m}Utility.IsNativeObjectAlive(value))");
                                using (Indent)
                                    Line.Append($"{m}Debug.LogError($\"Missing component: {x.TypeDisplayName} in {input.ClassName} '{{this.name}}'\", this);");
                            }

                            Line.Append($"_{m}{x.PropertyName} = value!;");
                        }
                    }
                }

                if (!x.Flags.Has(IsTransient))
                {
                    // backing field
                    Line.Append(Alias.Hidden);
                    Line.Append($"{@static}{x.TypeFQN} _{m}{x.PropertyName};");
                }
            }

            Linebreak();
        }

        if (expressions.Any(x => x.Flags.Any(IsCleanupDestroy | IsCleanupDispose)))
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