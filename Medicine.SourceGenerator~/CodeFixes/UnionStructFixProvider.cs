using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using static Constants;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class UnionStructFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; }
        = ImmutableArray.Create("MED019", "MED020", "MED021", "MED022", "MED023", "MED024");

    public override FixAllProvider? GetFixAllProvider()
        => null;

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            string title = diagnostic.Id switch
            {
                "MED019" => "Add nested public interface",
                "MED020" => "Add TypeID field",
                "MED021" => "Implement missing interface",
                "MED022" => "Add [UnionHeader] field",
                "MED023" => "Move [UnionHeader] field to first position",
                "MED024" => "Add [Union] attribute",
                _        => "",
            };

            if (title is not { Length: > 0 })
                continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: title,
                    createChangedDocument: ct => ApplyFixAsync(context.Document, diagnostic, ct),
                    equivalenceKey: diagnostic.Id
                ),
                diagnostic
            );
        }

        return Task.CompletedTask;
    }

    static async Task<Document> ApplyFixAsync(Document document, Diagnostic diagnostic, CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null)
            return document;

        var node = root.FindNode(diagnostic.Location.SourceSpan);
        var editor = await DocumentEditor.CreateAsync(document, ct).ConfigureAwait(false);

        switch (diagnostic.Id)
        {
            case "MED019":
                FixMED019(editor, node);
                break;
            case "MED020":
                FixMED020(editor, node);
                break;
            case "MED021":
                await FixMED021(editor, node, document, ct).ConfigureAwait(false);
                break;
            case "MED022":
                await FixMED022(editor, node, document, ct).ConfigureAwait(false);
                break;
            case "MED023":
                FixMED023(editor, node);
                break;
            case "MED024":
                FixMED024(editor, node);
                break;
        }

        return editor.GetChangedDocument();
    }

    static void FixMED019(DocumentEditor editor, SyntaxNode node)
    {
        if (node is not StructDeclarationSyntax structDecl)
            return;

        var interfaceDecl = SyntaxFactory.InterfaceDeclaration("Interface")
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken))
            .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken));

        editor.InsertMembers(structDecl, 0, [interfaceDecl]);
    }

    static void FixMED020(DocumentEditor editor, SyntaxNode node)
    {
        if (node is not StructDeclarationSyntax structDecl)
            return;

        var variableList = SyntaxFactory.SingletonSeparatedList(SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier("TypeID")));
        var variableDecl = SyntaxFactory.VariableDeclaration(SyntaxFactory.IdentifierName("TypeIDs")).WithVariables(variableList);
        var variableModifiers = SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword));
        var typeIdField = SyntaxFactory.FieldDeclaration(variableDecl).WithModifiers(variableModifiers);

        editor.InsertMembers(structDecl, 0, [typeIdField]);
    }

    static async Task FixMED021(DocumentEditor editor, SyntaxNode node, Document document, CancellationToken ct)
    {
        if (node is not StructDeclarationSyntax structDecl)
            return;

        var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);

        if (semanticModel?.GetDeclaredSymbol(structDecl, ct) is not { } typeSymbol)
            return;

        // Find the header field to get the interface name
        var fields = typeSymbol.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(x => !x.IsStatic)
            .OrderBy(x => x.Locations.FirstOrDefault()?.SourceSpan.Start ?? int.MaxValue)
            .ToArray();

        var headerField = fields.FirstOrDefault(f => f.Type.HasAttribute(UnionHeaderStructAttributeFQN));

        if (headerField?.Type is not INamedTypeSymbol headerType)
            return;

        var targetInterface
            = headerType.GetTypeMembers().FirstOrDefault(x => x.Name == "Interface")
              ?? headerType.GetTypeMembers().FirstOrDefault(x => x.TypeKind == TypeKind.Interface);

        if (targetInterface is null)
            return;

        var interfaceName = targetInterface.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var baseType = SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName(interfaceName));

        if (structDecl.BaseList is null)
        {
            var baseList = SyntaxFactory.BaseList(SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(baseType))
                .WithLeadingTrivia(SyntaxFactory.Space)
                .WithTrailingTrivia(structDecl.Identifier.TrailingTrivia);

            var newStructDecl = structDecl
                .WithIdentifier(structDecl.Identifier.WithTrailingTrivia())
                .WithBaseList(baseList);

            editor.ReplaceNode(structDecl, newStructDecl);
        }
        else
        {
            editor.AddBaseType(structDecl, baseType);
        }
    }

    static async Task FixMED022(DocumentEditor editor, SyntaxNode node, Document document, CancellationToken ct)
    {
        if (node is not StructDeclarationSyntax structDecl)
            return;

        var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);

        if (semanticModel?.GetDeclaredSymbol(structDecl, ct) is not { } typeSymbol)
            return;

        var unionInterface = typeSymbol
            .AllInterfaces
            .FirstOrDefault(i => i.ContainingType is { TypeKind: TypeKind.Struct } x && x.HasAttribute(UnionHeaderStructAttributeFQN)
        );

        string headerTypeName = unionInterface?.ContainingType.Name ?? "Header";

        var variableList = SyntaxFactory.SingletonSeparatedList(SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier("Header")));
        var variableDecl = SyntaxFactory.VariableDeclaration(SyntaxFactory.IdentifierName(headerTypeName)).WithVariables(variableList);
        var variableModifiers = SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword));
        var headerField = SyntaxFactory.FieldDeclaration(variableDecl).WithModifiers(variableModifiers);

        editor.InsertMembers(structDecl, 0, [headerField]);
    }

    static void FixMED023(DocumentEditor editor, SyntaxNode node)
    {
        var fieldDecl = node.AncestorsAndSelf().OfType<FieldDeclarationSyntax>().FirstOrDefault();

        if (fieldDecl?.Parent is not StructDeclarationSyntax structDecl)
            return;

        editor.RemoveNode(fieldDecl);
        editor.InsertMembers(structDecl, 0, [fieldDecl]);
    }

    static void FixMED024(DocumentEditor editor, SyntaxNode node)
    {
        if (node is not StructDeclarationSyntax structDecl)
            return;

        var attribute = SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("Union"));
        var attributeList = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute));

        editor.AddAttribute(structDecl, attributeList);
    }
}