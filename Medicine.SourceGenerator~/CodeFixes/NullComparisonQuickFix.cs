using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NullComparisonQuickFix)), Shared]
public sealed class NullComparisonQuickFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; }
        = ImmutableArray.Create(NullComparisonAnalyzer.MED026.Id, NullComparisonAnalyzer.MED027.Id);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

        if (root is null)
            return;

        var diagnostic = context.Diagnostics[0];
        var diagnosticNode = root.FindNode(diagnostic.Location.SourceSpan);
        var targetExpression =
            diagnosticNode as ExpressionSyntax
            ?? diagnosticNode.FirstAncestorOrSelf<BinaryExpressionSyntax>() as ExpressionSyntax
            ?? diagnosticNode.FirstAncestorOrSelf<IsPatternExpressionSyntax>();

        if (targetExpression is null)
            return;

        var methodName = diagnostic.Id == NullComparisonAnalyzer.MED026.Id ? "IsNull" : "IsNotNull";
        var title = diagnostic.Id == NullComparisonAnalyzer.MED026.Id
            ? "Use faster IsNull extension method"
            : "Use faster IsNotNull extension method";

        context.RegisterCodeFix(
            CodeAction.Create(
                title: title,
                createChangedDocument: ct => ApplyFixAsync(context.Document, targetExpression, methodName, ct),
                equivalenceKey: diagnostic.Id
            ),
            diagnostic
        );
    }

    static async Task<Document> ApplyFixAsync(
        Document document,
        ExpressionSyntax targetExpression,
        string methodName,
        CancellationToken ct
    )
    {
        if (await document.GetSemanticModelAsync(ct).ConfigureAwait(false) is not { } model)
            return document;

        if (!TryGetUnityOperand(targetExpression, model, ct, out var unityOperand))
            return document;

        var invocation = CreateInvocation(unityOperand, methodName).WithTriviaFrom(targetExpression);
        var editor = await DocumentEditor.CreateAsync(document, ct).ConfigureAwait(false);

        editor.ReplaceNode(targetExpression, invocation);
        editor.EnsureNamespaceIsImported("Medicine");
        return editor.GetChangedDocument();
    }

    static bool TryGetUnityOperand(ExpressionSyntax expression, SemanticModel model, CancellationToken ct, out ExpressionSyntax unityOperand)
    {
        unityOperand = null!;

        switch (expression)
        {
            case BinaryExpressionSyntax binary when binary.Kind() is SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression:
            {
                if (IsNullLiteral(binary.Left) && IsUnityObjectExpression(binary.Right, model, ct))
                {
                    unityOperand = binary.Right;
                    return true;
                }

                if (IsNullLiteral(binary.Right) && IsUnityObjectExpression(binary.Left, model, ct))
                {
                    unityOperand = binary.Left;
                    return true;
                }

                break;
            }

            case IsPatternExpressionSyntax isPattern:
            {
                var pattern = UnwrapParenthesizedPattern(isPattern.Pattern);
                if (!IsNullPattern(pattern) && !IsNotNullPattern(pattern))
                    return false;

                if (!IsUnityObjectExpression(isPattern.Expression, model, ct))
                    return false;

                unityOperand = isPattern.Expression;
                return true;
            }
        }

        return false;
    }

    static bool IsNullLiteral(ExpressionSyntax expression)
    {
        expression = UnwrapParenthesizedExpression(expression);
        return expression.IsKind(SyntaxKind.NullLiteralExpression);
    }

    static bool IsNullPattern(PatternSyntax pattern)
        => pattern is ConstantPatternSyntax { Expression: LiteralExpressionSyntax { RawKind: (int)SyntaxKind.NullLiteralExpression } };

    static bool IsNotNullPattern(PatternSyntax pattern)
        => pattern is UnaryPatternSyntax { Pattern: var inner }
           && IsNullPattern(UnwrapParenthesizedPattern(inner));

    static ExpressionSyntax UnwrapParenthesizedExpression(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax { Expression: var inner })
            expression = inner;

        return expression;
    }

    static PatternSyntax UnwrapParenthesizedPattern(PatternSyntax pattern)
    {
        while (pattern is ParenthesizedPatternSyntax { Pattern: var inner })
            pattern = inner;

        return pattern;
    }

    static bool IsUnityObjectExpression(ExpressionSyntax expression, SemanticModel model, CancellationToken ct)
    {
        var typeInfo = model.GetTypeInfo(expression, ct);
        var type = typeInfo.Type ?? typeInfo.ConvertedType;
        return IsUnityObjectType(type);
    }

    static bool IsUnityObjectType(ITypeSymbol? typeSymbol)
        => typeSymbol switch
        {
            null or IErrorTypeSymbol
                => false,
            ITypeParameterSymbol typeParameter
                => typeParameter.ConstraintTypes.AsArray().Any(IsUnityObjectType),
            _ => typeSymbol.Is("global::UnityEngine.Object") || typeSymbol.InheritsFrom("global::UnityEngine.Object")
        };

    static InvocationExpressionSyntax CreateInvocation(ExpressionSyntax receiver, string methodName)
    {
        var preparedReceiver = PrepareReceiver(receiver);
        return SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                preparedReceiver,
                SyntaxFactory.IdentifierName(methodName)
            )
        );
    }

    static ExpressionSyntax PrepareReceiver(ExpressionSyntax receiver)
    {
        receiver = receiver.WithoutTrivia();
        return NeedsParentheses(receiver)
            ? SyntaxFactory.ParenthesizedExpression(receiver)
            : receiver;
    }

    static bool NeedsParentheses(ExpressionSyntax receiver)
        => receiver is not
            (
                IdentifierNameSyntax or
                GenericNameSyntax or
                MemberAccessExpressionSyntax or
                ElementAccessExpressionSyntax or
                InvocationExpressionSyntax or
                ThisExpressionSyntax or
                BaseExpressionSyntax or
                ParenthesizedExpressionSyntax
            );
}