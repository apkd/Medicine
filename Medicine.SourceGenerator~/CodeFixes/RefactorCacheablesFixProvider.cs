using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using System.Collections.Immutable;
using System.Composition;
using static System.StringComparison;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RefactorCacheablesFixProvider)), Shared]
public class RefactorCacheablesFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds { get; }
        = ImmutableArray.Create(RefactorRefsAnalyzer.MED007.Id, RefactorRefsAnalyzer.MED008.Id, RefactorRefsAnalyzer.MED009.Id);

    public sealed override FixAllProvider? GetFixAllProvider() => null;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;
        var diagnosticNode = root?
            .FindNode(diagnosticSpan)
            .DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault();

        if (diagnosticNode is null)
            return;

        if (diagnostic.Id is "MED009")
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Cache component reference (add Awake() above other methods)",
                    createChangedDocument: ct => CacheComponentReferenceAsync(context.Document, diagnosticNode, NewMethodPlacement.AboveFirstMethod, ct),
                    equivalenceKey: "CacheComponentUse"
                ),
                diagnostic
            );

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Cache component reference (add Awake() at the top of the class)",
                    createChangedDocument: ct => CacheComponentReferenceAsync(context.Document, diagnosticNode, NewMethodPlacement.TopOfClass, ct),
                    equivalenceKey: "CacheComponentUse"
                ),
                diagnostic
            );
        }
        else if (diagnostic.Id is "MED007")
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Cache component reference",
                    createChangedDocument: ct => CacheComponentReferenceAsync(context.Document, diagnosticNode, default, ct),
                    equivalenceKey: "CacheComponentUse"
                ),
                diagnostic
            );
        }
        else if (diagnostic.Id is "MED008")
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Use existing cached component reference",
                    createChangedDocument: ct => CacheComponentReferenceAsync(context.Document, diagnosticNode, default, ct),
                    equivalenceKey: "CacheComponentUse"
                ),
                diagnostic
            );
        }
    }

    enum NewMethodPlacement
    {
        TopOfClass,
        AboveFirstMethod,
    }

    static async Task<Document> CacheComponentReferenceAsync(
        Document document,
        InvocationExpressionSyntax invocationExpr,
        NewMethodPlacement placement,
        CancellationToken ct
    )
    {
        var editor = await DocumentEditor.CreateAsync(document, ct).ConfigureAwait(false);
        var generator = editor.Generator;

        if (await document.GetSemanticModelAsync(ct) is not { } semanticModel)
            return document;

        if (invocationExpr.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault() is not { } classDecl)
            return document;

        if (semanticModel.GetDeclaredSymbol(classDecl, ct) is not { } classSymbol)
            return document;

        if (semanticModel.GetSymbolInfo(invocationExpr, ct).Symbol is not IMethodSymbol methodSymbol)
            return document;

        if (GetCalledMethodType(methodSymbol, invocationExpr, semanticModel, ct) is not { } componentTypeSymbol)
            return document;

        bool isMultiple = methodSymbol.ReturnType is IArrayTypeSymbol;
        string propertyName = isMultiple ? Pluralize(componentTypeSymbol.Name) : componentTypeSymbol.Name;

        if (methodSymbol.Name.Contains("InChildren"))
            propertyName = $"Child{propertyName}";
        else if (methodSymbol.Name.Contains("InParent"))
            propertyName = $"Parent{propertyName}";

        var propertyAssignmentExpression = (ExpressionSyntax)generator.AssignmentStatement(
            generator.IdentifierName(propertyName),
            invocationExpr.WithoutTrivia()
        );

        AttributeData? GetInjectAttribute(SyntaxNode declSyntax)
        {
            if (semanticModel.GetDeclaredSymbol(declSyntax, ct) is { } symbol)
                if (symbol.GetAttributes().FirstOrDefault(x => x.AttributeClass.Is(Constants.InjectAttributeFQN)) is { } attribute)
                    return attribute;

            return null;
        }

        AttributeData? existingInjectAttribute = null;

        var methodToPatch
            = classDecl.Members
                  .OfType<MethodDeclarationSyntax>()
                  .FirstOrDefault(x => (existingInjectAttribute ??= GetInjectAttribute(x)) is not null) ??
              classDecl.DescendantNodes()
                  .OfType<LocalFunctionStatementSyntax>()
                  .FirstOrDefault(x => (existingInjectAttribute ??= GetInjectAttribute(x)) is not null) as SyntaxNode ??
              classDecl.Members
                  .OfType<MethodDeclarationSyntax>()
                  .FirstOrDefault(x => x.Identifier.Text == "Awake" && !x.ParameterList.Parameters.Any());

        var propertyAssignmentStatement = generator.ExpressionStatement(propertyAssignmentExpression);

        var newOrPatchedMethod = GenerateOrPatchMethod();

        if (methodToPatch is not null)
            editor.ReplaceNode(methodToPatch, newOrPatchedMethod);
        else
        {
            var firstMethod = placement is NewMethodPlacement.AboveFirstMethod
                ? classDecl.Members.OfType<MethodDeclarationSyntax>().FirstOrDefault()
                : null;

            if (firstMethod is not null)
                editor.InsertBefore(firstMethod, newOrPatchedMethod);
            else
                editor.InsertMembers(classDecl, 0, [newOrPatchedMethod]);
        }

        editor.ReplaceNode(
            invocationExpr,
            generator.IdentifierName(propertyName).WithTriviaFrom(invocationExpr)
        );

        if (!classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
            editor.SetModifiers(classDecl, DeclarationModifiers.From(classSymbol).WithPartial(true));

        SyntaxNode GenerateOrPatchMethod()
        {
            SyntaxNode method;

            if (methodToPatch is null)
            {
                method = generator.MethodDeclaration(name: "Awake");
                method = generator.AddAttributes(method, generator.Attribute("Inject"));
            }
            else
            {
                method = methodToPatch switch
                {
                    MethodDeclarationSyntax m => m.WithReturnType(m.ReturnType),
                    LocalFunctionStatementSyntax l => l.WithReturnType(l.ReturnType),
                    _ => methodToPatch
                };

                if (existingInjectAttribute is null)
                {
                    method = generator.AddAttributes(method, generator.Attribute("Inject"));
                    editor.EnsureNamespaceIsImported("Medicine");
                }
            }

            BlockSyntax? body = method switch
            {
                MethodDeclarationSyntax m => m.Body,
                LocalFunctionStatementSyntax l => l.Body,
                _ => null
            };

            ArrowExpressionClauseSyntax? expressionBody = method switch
            {
                MethodDeclarationSyntax m => m.ExpressionBody,
                LocalFunctionStatementSyntax l => l.ExpressionBody,
                _ => null
            };

            if (body is null && expressionBody is not null)
            {
                var newBody = SyntaxFactory.Block(SyntaxFactory.ExpressionStatement(expressionBody.Expression));
                method = method switch
                {
                    MethodDeclarationSyntax m => m.WithBody(newBody).WithExpressionBody(null).WithSemicolonToken(default).WithAdditionalAnnotations(Formatter.Annotation),
                    LocalFunctionStatementSyntax l => l.WithBody(newBody).WithExpressionBody(null).WithSemicolonToken(default).WithAdditionalAnnotations(Formatter.Annotation),
                    _ => method
                };
                body = newBody;
            }

            if (body is null)
                return method;

            var existingPropertyAssignment = body.Statements
                .OfType<ExpressionStatementSyntax>()
                .Select(st => st.Expression as AssignmentExpressionSyntax)
                .FirstOrDefault(a => a is { Right: var right } && SyntaxFactory.AreEquivalent(right, invocationExpr));

            if (existingPropertyAssignment is not null)
            {
                if (existingPropertyAssignment.Left is IdentifierNameSyntax id)
                    propertyName = id.Identifier.Text;

                return method;
            }

            var existingPropertyNames = body.Statements
                .OfType<ExpressionStatementSyntax>()
                .Select(st => st.Expression as AssignmentExpressionSyntax)
                .Where(a => a is not null)
                .Select(a => a!.Left)
                .OfType<IdentifierNameSyntax>()
                .Select(id => id.Identifier.Text)
                .ToImmutableHashSet();

            if (existingPropertyNames.Contains(propertyName))
            {
                var i = 1;
                var baseName = propertyName;
                do
                {
                    propertyName = $"{baseName}{i++}";
                } while (existingPropertyNames.Contains(propertyName));

                propertyAssignmentExpression
                    = (ExpressionSyntax)generator.AssignmentStatement(
                        generator.IdentifierName(propertyName),
                        invocationExpr.WithoutTrivia()
                    );

                propertyAssignmentStatement = generator.ExpressionStatement(propertyAssignmentExpression);
            }

            var patchedBody = body.AddStatements((StatementSyntax)propertyAssignmentStatement);
            return method switch
            {
                MethodDeclarationSyntax m => m.WithBody(patchedBody),
                LocalFunctionStatementSyntax l => l.WithBody(patchedBody),
                _ => method
            };
        }

        return editor.GetChangedDocument();
    }

    static ITypeSymbol? GetCalledMethodType(IMethodSymbol methodSymbol, InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken token)
    {
        if (methodSymbol is { IsGenericMethod: true, TypeArguments.Length: > 0 })
            return methodSymbol.TypeArguments.First();

        if (invocation.ArgumentList.Arguments.Count > 0 && invocation.ArgumentList.Arguments.First().Expression is TypeOfExpressionSyntax typeOfExpr)
            return model.GetSymbolInfo(typeOfExpr.Type, token).Symbol as ITypeSymbol;

        return null;
    }

    static string Pluralize(string name)
    {
        if (!name.EndsWith("y", Ordinal))
        {
            return
                name.EndsWith("s", Ordinal)
                || name.EndsWith("x", Ordinal)
                || name.EndsWith("z", Ordinal)
                || name.EndsWith("ch", Ordinal)
                || name.EndsWith("sh", Ordinal)
                    ? $"{name}es"
                    : $"{name}s";
        }

        // ReSharper disable once StringLiteralTypo
        if (name.Length > 1)
            if (!"aeiou".Contains(char.ToLower(name[^2])))
                return $"{name[..^1]}ies";

        return $"{name}s";
    }
}