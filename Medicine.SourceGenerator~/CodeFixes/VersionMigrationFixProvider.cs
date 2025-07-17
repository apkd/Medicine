using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis.Formatting;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(VersionMigrationFixProvider)), Shared]
public sealed class VersionMigrationFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds
        => ImmutableArray.Create(VersionMigrationAnalyzer.MED012.Id);

    public override FixAllProvider? GetFixAllProvider() => null;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var diagnostic = context.Diagnostics.First();
        var property = root?
            .FindNode(diagnostic.Location.SourceSpan)
            .FirstAncestorOrSelf<PropertyDeclarationSyntax>();

        if (property is null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Migrate property to the new injection syntax",
                createChangedDocument: ct => RefactorAsync(context.Document, property, ct),
                equivalenceKey: VersionMigrationAnalyzer.MED012.Id
            ),
            diagnostic
        );
    }

    static async Task<Document> RefactorAsync(Document document, PropertyDeclarationSyntax property, CancellationToken ct)
    {
        var editor = await DocumentEditor.CreateAsync(document, ct).ConfigureAwait(false);
        var generator = editor.Generator;
        var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);

        if (semanticModel is null)
            return document;

        if (semanticModel.GetDeclaredSymbol(property, ct) is not { } propSymbol)
            return document;

        if (property.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault() is not { } classDecl)
            return document;

        if (semanticModel.GetDeclaredSymbol(classDecl, ct) is not { } classSymbol)
            return document;

        var propName = propSymbol.Name;
        var typeSymbol = propSymbol.Type;
        var isArray = typeSymbol is IArrayTypeSymbol;

        bool injectFromChildren = propSymbol.HasAttribute(a => a.Contains("FromChildren"));
        bool injectFromParents = propSymbol.HasAttribute(a => a.Contains("FromParents"));
        bool injectAll = propSymbol.HasAttribute(a => a.Contains("All"));
        bool injectSingle = propSymbol.HasAttribute(a => a.Contains("Single"));
        bool injectLazy = propSymbol.HasAttribute(a => a.Contains("Lazy"));

        bool optional = propSymbol.GetAttributes().SelectMany(a => a.NamedArguments).Any(kvp => kvp is { Key: "Optional", Value.Value: true });
        bool includeInactive = propSymbol.GetAttributes().SelectMany(a => a.NamedArguments).Any(kvp => kvp is { Key: "IncludeInactive", Value.Value: true });

        string plural = isArray ? "s" : "";
        string optionalCall = optional ? ".Optional()" : "";
        string argList = includeInactive ? "includeInactive: true" : "";
        string componentTypeName = isArray
            ? ((IArrayTypeSymbol)typeSymbol).ElementType.ToMinimalDisplayString(semanticModel, NullableFlowState.None, property.GetLocation().SourceSpan.Start)
            : typeSymbol.ToMinimalDisplayString(semanticModel, NullableFlowState.None, property.GetLocation().SourceSpan.Start);

        string retrievalExpString = (injectFromChildren, injectFromParents, injectAll, injectSingle, injectLazy, isArray) switch
        {
            { injectFromChildren: true, injectLazy: true, isArray: true }     => $"gameObject.EnumerateComponentsInChildren<{componentTypeName}>({argList}){optionalCall}",
            { injectFromParents: true, injectLazy: true, isArray: true }      => $"gameObject.EnumerateComponentsInParents<{componentTypeName}>({argList}){optionalCall}",
            { injectFromChildren: true, injectLazy: true } when argList is "" => $"new(GetComponent{plural}InChildren<{componentTypeName}>)",
            { injectFromParents: true, injectLazy: true } when argList is ""  => $"new(GetComponent{plural}InParent<{componentTypeName}>)",
            { injectFromChildren: true, injectLazy: true }                    => $"new(() => GetComponent{plural}InChildren<{componentTypeName}>({argList})",
            { injectFromParents: true, injectLazy: true }                     => $"new(() => GetComponent{plural}InParent<{componentTypeName}>({argList})",
            { injectFromChildren: true }                                      => $"GetComponent{plural}InChildren<{componentTypeName}>({argList}){optionalCall}",
            { injectFromParents: true }                                       => $"GetComponent{plural}InParent<{componentTypeName}>({argList}){optionalCall}",
            { injectAll: true }                                               => $"{componentTypeName}.Instances",
            { injectSingle: true }                                            => $"{componentTypeName}.Instance{optionalCall}",
            { injectLazy: true } when argList is ""                           => $"new(GetComponent{plural}<{componentTypeName}>)",
            { injectLazy: true }                                              => $"new(() => GetComponent{plural}<{componentTypeName}>({argList})",
            _                                                                 => $"GetComponent{plural}<{componentTypeName}>({argList}){optionalCall}",
        };

        var retrievalExpr = SyntaxFactory.ParseExpression(retrievalExpString);

        if (propSymbol.IsStatic)
        {
            editor.RemoveNode(property);

            var comparer = SymbolEqualityComparer.Default;
            var identifiers = editor.OriginalRoot.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Where(id => comparer.Equals(semanticModel.GetSymbolInfo(id, ct).Symbol, propSymbol))
                .ToArray();

            foreach (var idNode in identifiers)
            {
                var freshExpr = SyntaxFactory.ParseExpression(retrievalExpString).WithTriviaFrom(idNode);

                switch (idNode.Parent)
                {
                    case MemberAccessExpressionSyntax ma when ma.Name == idNode:
                        editor.ReplaceNode(ma, freshExpr.WithTriviaFrom(ma));
                        break;

                    case MemberAccessExpressionSyntax ma when ma.Expression == idNode:
                        editor.ReplaceNode(idNode, freshExpr);
                        break;

                    default:
                        editor.ReplaceNode(idNode, freshExpr);
                        break;
                }
            }

            EnsureUsing(editor, "Medicine");
            return editor.GetChangedDocument();
        }

        AttributeData? existingInjectAttribute = null;

        AttributeData? GetInjectAttribute(MethodDeclarationSyntax m)
            => semanticModel.GetDeclaredSymbol(m, ct)
                ?
                .GetAttributes()
                .FirstOrDefault(a => a.AttributeClass.Is(Constants.InjectAttributeFQN));

        var methodDecl = classDecl.Members.OfType<MethodDeclarationSyntax>()
                             .FirstOrDefault(m => (existingInjectAttribute ??= GetInjectAttribute(m)) is not null)
                         ?? classDecl.Members.OfType<MethodDeclarationSyntax>()
                             .FirstOrDefault(m => m.Identifier.Text == "Awake" && !m.ParameterList.Parameters.Any());

        if (methodDecl is null)
        {
            var propAssign = generator.ExpressionStatement(
                generator.AssignmentStatement(
                    generator.IdentifierName(propName),
                    retrievalExpr
                )
            );

            var newAwake = (MethodDeclarationSyntax)generator.MethodDeclaration("Awake", statements: [propAssign]);
            newAwake = (MethodDeclarationSyntax)generator.AddAttributes(newAwake, generator.Attribute("Inject"));
            editor.InsertMembers(classDecl, 0, [newAwake]);
        }
        else
        {
            var originalMethod = methodDecl;

            if (!methodDecl.HasAttribute(a => a is "Inject" or "InjectAttribute" or "Medicine.Inject" or "Medicine.InjectAttribute"))
                methodDecl = (MethodDeclarationSyntax)generator.AddAttributes(methodDecl, generator.Attribute("Inject"));

            var propAssign = generator.ExpressionStatement(
                generator.AssignmentStatement(
                    generator.IdentifierName(propName),
                    retrievalExpr
                )
            );

            if (methodDecl.Body is null && methodDecl.ExpressionBody is not null)
            {
                methodDecl = methodDecl.WithBody(
                        SyntaxFactory.Block(SyntaxFactory.ExpressionStatement(methodDecl.ExpressionBody.Expression)))
                    .WithExpressionBody(null)
                    .WithSemicolonToken(default)
                    .WithAdditionalAnnotations(Formatter.Annotation);
            }

            methodDecl = methodDecl.WithBody(methodDecl.Body?.AddStatements((StatementSyntax)propAssign));

            editor.ReplaceNode(originalMethod, methodDecl);
        }

        // No need to rewrite property references for direct access

        editor.RemoveNode(property);
        if (!classDecl.Modifiers.Any(SyntaxKind.PartialKeyword))
            editor.SetModifiers(classDecl, DeclarationModifiers.From(classSymbol).WithPartial(true));

        EnsureUsing(editor, "Medicine");
        return editor.GetChangedDocument();
    }

    static void EnsureUsing(DocumentEditor editor, string @namespace)
    {
        if (editor.OriginalRoot is CompilationUnitSyntax compilationUnitSyntax)
        {
            if (compilationUnitSyntax.Usings.Count == 0)
                editor.InsertBefore(compilationUnitSyntax.Members.First(), SyntaxFactory.UsingDirective(SyntaxFactory.IdentifierName(@namespace)));
            else if (compilationUnitSyntax.Usings.All(u => u.Name.ToString() != @namespace))
                editor.InsertAfter(compilationUnitSyntax.Usings.Last(), SyntaxFactory.UsingDirective(SyntaxFactory.IdentifierName(@namespace)));
        }
    }
}