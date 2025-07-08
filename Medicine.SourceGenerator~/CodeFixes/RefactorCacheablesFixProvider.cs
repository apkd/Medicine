using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using System.Collections.Immutable;
using System.Composition;
using static System.StringComparison;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RefactorCacheablesFixProvider)), Shared]
public class RefactorCacheablesFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds { get; }
        = ImmutableArray.Create(RefactorRefsAnalyzer.MED007.Id, RefactorRefsAnalyzer.MED008.Id);

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

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Cache component reference",
                createChangedDocument: ct => CacheComponentReferenceAsync(context.Document, diagnosticNode, ct),
                equivalenceKey: "CacheComponentUse"
            ),
            diagnostic
        );
    }

    static async Task<Document> CacheComponentReferenceAsync(Document document, InvocationExpressionSyntax invocationExpr, CancellationToken ct)
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

        AttributeData? GetInjectAttribute(MethodDeclarationSyntax method)
        {
            if (semanticModel.GetDeclaredSymbol(method, ct) is { } symbol)
                if (symbol.GetAttributes().FirstOrDefault(x => x.AttributeClass.Is(Constants.InjectAttributeFQN)) is { } attribute)
                    return attribute;

            return null;
        }

        AttributeData? existingInjectAttribute = null;

        var methodToPatch
            = classDecl.Members
                  .OfType<MethodDeclarationSyntax>()
                  .FirstOrDefault(x => (existingInjectAttribute ??= GetInjectAttribute(x)) is not null) ??
              classDecl.Members
                  .OfType<MethodDeclarationSyntax>()
                  .FirstOrDefault(x => x.Identifier.Text == "Awake" && !x.ParameterList.Parameters.Any());

        var propertyAssignmentStatement = generator.ExpressionStatement(propertyAssignmentExpression);

        var newOrPatchedMethod = GenerateOrPatchMethod();

        if (methodToPatch is not null)
            editor.ReplaceNode(methodToPatch, newOrPatchedMethod);
        else
            editor.InsertMembers(classDecl, 0, [newOrPatchedMethod]);

        editor.ReplaceNode(
            invocationExpr,
            generator.IdentifierName(propertyName).WithTriviaFrom(invocationExpr)
        );

        if (!classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
            editor.SetModifiers(classDecl, DeclarationModifiers.From(classSymbol).WithPartial(true));

        MethodDeclarationSyntax GenerateOrPatchMethod()
        {
            MethodDeclarationSyntax method;

            if (methodToPatch is null)
            {
                method = (MethodDeclarationSyntax)generator.MethodDeclaration(name: "Awake");
                method = (MethodDeclarationSyntax)generator.AddAttributes(method, generator.Attribute("Inject"));
            }
            else
            {
                method = methodToPatch.WithReturnType(methodToPatch.ReturnType);
            }

            if (existingInjectAttribute is null)
            {
                generator.AddAttributes(method, generator.Attribute("Inject"));
                editor.EnsureNamespaceIsImported("Medicine");
            }

            if (method.Body is null)
                return method;

            var existingPropertyAssignment = method.Body.Statements
                .OfType<ExpressionStatementSyntax>()
                .Select(st => st.Expression as AssignmentExpressionSyntax)
                .FirstOrDefault(a => a is { Right: var right } && SyntaxFactory.AreEquivalent(right, invocationExpr));

            if (existingPropertyAssignment is not null)
            {
                if (existingPropertyAssignment.Left is IdentifierNameSyntax id)
                    propertyName = id.Identifier.Text;

                return method;
            }

            var existingPropertyNames = method.Body.Statements
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

            return method.WithBody(method.Body.AddStatements((StatementSyntax)propertyAssignmentStatement));
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

    // Removed initializer utility methods as they are no longer needed with direct property assignments
}