using System.Collections.Immutable;
using System.Composition;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

#pragma warning disable RS2008

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DisallowReadonlyFieldAnalyzer : DiagnosticAnalyzer
{
    static readonly DiagnosticDescriptor Rule = new(
        id: "MED015",
        title: "Disallowed readonly field",
        messageFormat: "This field must not be readonly to ensure that the stored mutable struct works correctly.",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }
        = ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeField, SyntaxKind.FieldDeclaration);
    }

    static void AnalyzeField(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not FieldDeclarationSyntax fieldDeclaration)
            return;

        if (!fieldDeclaration.Modifiers.Any(SyntaxKind.ReadOnlyKeyword))
            return;

        var typeInfo = context.SemanticModel.GetTypeInfo(fieldDeclaration.Declaration.Type);

        if (typeInfo is not { Type: INamedTypeSymbol { TypeKind: TypeKind.Struct } type })
            return;

        var hasAttribute = type.GetAttributes() is { Length: > 0 } attributes &&
                    attributes.Any(x => x is { AttributeClass: { ContainingNamespace.Name: "Medicine", Name: "DisallowReadonlyAttribute" } });

        if (hasAttribute)
            context.ReportDiagnostic(Diagnostic.Create(Rule, fieldDeclaration.GetLocation()));
    }

    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CodeFix))] [Shared]
    [SuppressMessage("ReSharper", "HeapView.BoxingAllocation")]
    public sealed class CodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds { get; }
            = ImmutableArray.Create(Rule.Id);

        public override FixAllProvider GetFixAllProvider()
            => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            if (root?.FindNode(context.Diagnostics[0].Location.SourceSpan) is not FieldDeclarationSyntax node)
                return;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Remove readonly modifier",
                    equivalenceKey: "RemoveReadonly",
                    createChangedDocument: ct =>
                    {
                        var modifiers = node.Modifiers;
                        modifiers = modifiers.Remove(modifiers.First(x => x.IsKind(SyntaxKind.ReadOnlyKeyword)));
                        root = root.ReplaceNode(node, node.WithModifiers(modifiers).WithTriviaFrom(node));
                        return Task.FromResult(context.Document.WithSyntaxRoot(root));
                    }
                ),
                context.Diagnostics[0]
            );
        }
    }
}