using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

public static class Utility
{
    public static readonly DiagnosticDescriptor DebugDiagnosticDescriptor = new(
        id: "MED999",
        title: "Debug",
        messageFormat: "'{0}'",
        category: "Debug",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    public static unsafe string MakeString(this ReadOnlySpan<char> chars)
    {
        var ptr = (char*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(chars));
        return new(ptr, 0, chars.Length);
    }

    public static Diagnostic DebugDiagnostic(Location location, string msg)
        => Diagnostic.Create(DebugDiagnosticDescriptor, location, msg);

    public static void EnsureNamespaceIsImported(this DocumentEditor editor, string namespaceName)
    {
        if (!editor.OriginalDocument.TryGetSyntaxRoot(out var root))
            return;

        if (root is not CompilationUnitSyntax compilationUnit)
            return;

        var usings = compilationUnit.Usings;

        if (usings.Any(x => x.Name.ToString() == namespaceName))
            return;

        var usingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.IdentifierName(namespaceName));
        editor.InsertAfter(usings.Last(), usingDirective);
    }

    public static EquatableArray<string> DeconstructTypeDeclaration(MemberDeclarationSyntax memberDeclarationSyntax, string? extraInterfaces = null)
    {
        IEnumerable<string> Walk(MemberDeclarationSyntax? syntax)
        {
            bool NavigateToParent()
                => (syntax = syntax?.Parent as MemberDeclarationSyntax)?.Kind()
                    is SyntaxKind.NamespaceDeclaration
                    or SyntaxKind.ClassDeclaration
                    or SyntaxKind.StructDeclaration
                    or SyntaxKind.RecordDeclaration;

            string colon = extraInterfaces is not null
                ? " : "
                : "";

            bool firstDeclaration = true;

            do
            {
                var interfaces = firstDeclaration
                    ? extraInterfaces ?? ""
                    : "";

                string? line = syntax switch
                {
                    NamespaceDeclarationSyntax x => $"namespace {x.Name}",
                    TypeDeclarationSyntax x      => $"partial {x.Keyword.ValueText} {x.Identifier}{x.TypeParameterList}{colon}{interfaces} {x.ConstraintClauses}",
                    _                            => null,
                };

                if (line is not null)
                    yield return line;

                firstDeclaration = false;
            } while (NavigateToParent());
        }

        var result = Walk(memberDeclarationSyntax).ToArray();
        Array.Reverse(result);
        return result;
    }
}