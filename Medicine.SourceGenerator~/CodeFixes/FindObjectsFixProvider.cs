using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
sealed class FindObjectsFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; }
        = ImmutableArray.Create(FindObjectsAnalyzer.MED011.Id, FindObjectsAnalyzer.MED013.Id);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

        if (root is null)
            return;

        var diagnostic = context.Diagnostics.First();
        if (root.FindNode(diagnostic.Location.SourceSpan) is not InvocationExpressionSyntax invocation)
            return;

        var genericNameSyntax = invocation.Expression switch
        {
            GenericNameSyntax x                                        => x,
            MemberAccessExpressionSyntax { Name: GenericNameSyntax x } => x,
            _                                                          => null,
        };

        static bool IsDirectlyEnumerated(InvocationExpressionSyntax inv)
            => inv.Parent switch
            {
                // foreach
                ForEachStatementSyntax foreachStmt
                    when foreachStmt.Expression == inv
                    => true,
                // linq chains
                MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax }
                    => true,
                _ => false,
            };

        if (genericNameSyntax is not { TypeArgumentList.Arguments: [{ } typeSyntax] })
            return;

        if (diagnostic.Id == FindObjectsAnalyzer.MED013.Id)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Use direct singleton access",
                    createChangedDocument: ct => ReplaceWith_T_PropertyAccess_Async(context.Document, invocation, typeSyntax, "Instance", ct),
                    equivalenceKey: nameof(FindObjectsAnalyzer.MED013)
                ),
                diagnostic
            );

            return;
        }

        if (diagnostic.Id == FindObjectsAnalyzer.MED011.Id)
        {
            var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);

            if (model is null)
                return;

            var typeSymbol = model.GetTypeInfo(typeSyntax).Type;

            if (typeSymbol is null)
                return;

            var isTracked = typeSymbol?.HasAttribute(Constants.TrackAttributeFQN) is true;

            bool IsIncludeInactive()
                => invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression
                    is LiteralExpressionSyntax { Token.ValueText: "true" }
                    or MemberAccessExpressionSyntax { Name.Identifier.Text: "Include" };

            if (isTracked && !IsIncludeInactive())
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: $"Use {typeSymbol?.Name}.Instances",
                        createChangedDocument: ct => ReplaceWith_T_PropertyAccess_Async(context.Document, invocation, typeSyntax, "Instances", ct),
                        equivalenceKey: nameof(FindObjectsAnalyzer)
                    ),
                    diagnostic
                );
            }
            else
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: $"Use 'Find.ObjectsByType<{typeSymbol!.Name}>()'",
                        createChangedDocument: ct => ReplaceWith_Find_ObjectsByType(context.Document, invocation, typeSyntax, model, ct),
                        equivalenceKey: nameof(FindObjectsAnalyzer)
                    ),
                    diagnostic
                );

                if (IsDirectlyEnumerated(invocation))
                {
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            title: $"Use non-allocating enumerator 'Find.ComponentsInScene<{typeSymbol!.Name}>(gameObject.scene)'",
                            createChangedDocument: ct => ReplaceWith_Find_ComponentsInScene(context.Document, invocation, IsIncludeInactive(), typeSyntax, model, ct),
                            equivalenceKey: nameof(FindObjectsAnalyzer)
                        ),
                        diagnostic
                    );
                }
            }
        }
    }

    static async Task<Document> ReplaceWith_T_PropertyAccess_Async(
        Document document,
        InvocationExpressionSyntax oldInvoke,
        TypeSyntax typeArg,
        string propertyName,
        CancellationToken token
    )
    {
        var newExpression =
            SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    typeArg.WithoutTrivia(),
                    SyntaxFactory.IdentifierName(propertyName)
                )
                .WithTriviaFrom(oldInvoke);

        var root = await document.GetSyntaxRootAsync(token).ConfigureAwait(false);
        return document.WithSyntaxRoot(root!.ReplaceNode(oldInvoke, newExpression));
    }

    static async Task<Document> ReplaceWith_Find_ObjectsByType(
        Document doc,
        InvocationExpressionSyntax oldCall,
        TypeSyntax tArg,
        SemanticModel semanticModel,
        CancellationToken ct
    )
    {
        static bool IsBool(ITypeSymbol? t) => t?.SpecialType == SpecialType.System_Boolean;
        static bool IsSort(ITypeSymbol? t) => t?.Name is "FindObjectsSortMode";
        static bool IsInactiveEnum(ITypeSymbol? t) => t?.Name is "FindObjectsInactive";

        static MemberAccessExpressionSyntax InactiveInclude() =>
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName("FindObjectsInactive"),
                SyntaxFactory.IdentifierName("Include")
            );

        ArgumentSyntax? includeArg = null;
        ArgumentSyntax? sortArg = null;

        foreach (var a in oldCall.ArgumentList.Arguments)
        {
            var argumentType = semanticModel.GetTypeInfo(a.Expression).ConvertedType;

            if (IsBool(argumentType))
            {
                if (semanticModel.GetConstantValue(a.Expression) is { HasValue: true, Value: false })
                    continue;

                includeArg = a;
                continue;
            }

            if (IsSort(argumentType))
            {
                if (semanticModel.GetConstantValue(a.Expression) is { HasValue: true, Value: 0 })
                    continue;

                sortArg = a;
                continue;
            }

            if (IsInactiveEnum(argumentType))
            {
                var cv = semanticModel.GetConstantValue(a.Expression);

                if (cv.HasValue)
                {
                    var include = cv.Value switch
                    {
                        int integer  => integer != 0,
                        bool boolean => boolean,
                        string str   => str == "Include",
                        _            => Convert.ToInt32(cv.Value) != 0,
                    };

                    includeArg = SyntaxFactory.Argument(
                            SyntaxFactory.LiteralExpression(
                                include ? SyntaxKind.TrueLiteralExpression
                                    : SyntaxKind.FalseLiteralExpression
                            )
                        )
                        .WithTriviaFrom(a);
                }
                else
                {
                    var binary = SyntaxFactory.BinaryExpression(
                        SyntaxKind.EqualsExpression,
                        a.Expression.WithoutTrivia(),
                        InactiveInclude()
                    );

                    includeArg = SyntaxFactory.Argument(binary).WithTriviaFrom(a);
                }
            }
        }

        if (includeArg is null && sortArg is not null && sortArg.NameColon is null)
        {
            sortArg = sortArg.WithNameColon(
                SyntaxFactory.NameColon(SyntaxFactory.IdentifierName("sortMode"))
            );
        }

        var finalArgs = new SeparatedSyntaxList<ArgumentSyntax>();
        if (includeArg is not null)
            finalArgs = finalArgs.Add(includeArg);

        if (sortArg is not null)
            finalArgs = finalArgs.Add(sortArg);

        var newCall = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName("Find"),
                    SyntaxFactory.GenericName("ObjectsByType")
                        .WithTypeArgumentList(
                            SyntaxFactory.TypeArgumentList(
                                SyntaxFactory.SingletonSeparatedList(tArg.WithoutTrivia())
                            )
                        )
                ),
                SyntaxFactory.ArgumentList(finalArgs)
            )
            .WithTriviaFrom(oldCall);

        var root = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        return doc.WithSyntaxRoot(root!.ReplaceNode(oldCall, newCall));
    }

    static async Task<Document> ReplaceWith_Find_ComponentsInScene(
        Document document,
        InvocationExpressionSyntax oldInvocation,
        bool isIncludeInactive,
        TypeSyntax typeArgument,
        SemanticModel semanticModel,
        CancellationToken cancellationToken
    )
    {
        var sceneAccess = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            SyntaxFactory.IdentifierName("gameObject"),
            SyntaxFactory.IdentifierName("scene")
        );

        var argumentList = isIncludeInactive
            ? SyntaxFactory.ArgumentList(
                SyntaxFactory.SeparatedList(
                    [
                        SyntaxFactory.Argument(sceneAccess),
                        SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)),
                    ]
                )
            )
            : SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(sceneAccess)));

        var genericName = SyntaxFactory.GenericName("ComponentsInScene")
            .WithTypeArgumentList(
                SyntaxFactory.TypeArgumentList(
                    SyntaxFactory.SingletonSeparatedList(typeArgument.WithoutTrivia())
                )
            );

        var newMemberAccess = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            SyntaxFactory.IdentifierName("Find"),
            genericName
        );

        var newInvocation = SyntaxFactory.InvocationExpression(newMemberAccess, argumentList)
            .WithTriviaFrom(oldInvocation);

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var newRoot = root!.ReplaceNode(oldInvocation, newInvocation);

        return document.WithSyntaxRoot(newRoot);
    }
}