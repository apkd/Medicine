using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

public static class Utility
{
    public static readonly DiagnosticDescriptor ExceptionDiagnosticDescriptor = new(
        id: "MED911",
        title: "Exception",
        messageFormat: "Exception: {0}",
        category: "Exception",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static string GetErrorOutputFilename(LocationInfo? location, string error)
    {
        string filename = Path.GetFileNameWithoutExtension(location?.FileLineSpan.Path);
        if (string.IsNullOrEmpty(filename))
            filename = "Unknown";
        string result = $"{filename}.Exception.{Hash():x8}.g.cs";
        return result;

        int Hash()
        {
            unchecked
            {
                int hash = 23;

                foreach (char c in error)
                    hash = hash * 31 + c;

                foreach (char c in filename)
                    hash = hash * 31 + c;

                return hash;
            }
        }
    }

    public static string GetOutputFilename(string filePath, string targetFQN, string label, string shadowTargetFQN = "", bool includeFilename = true)
    {
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
        string typename;
        {
            var source = targetFQN.AsSpan();
            var sanitized = source.Length <= 1024
                ? stackalloc char[source.Length]
                : new char[source.Length];

            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                sanitized[i] = char.IsLetterOrDigit(c) ? c : '_';
            }

            typename = sanitized.ToString();
        }

        var result = includeFilename
            ? $"{fileNameWithoutExtension}.{typename}.{label}.{Hash():x8}.g.cs"
            : $"{typename}.{label}.{Hash():x8}.g.cs";

        ;

        return result;

        int Hash()
        {
            unchecked
            {
                int hash = 23;

                foreach (char c in fileNameWithoutExtension)
                    hash = hash * 31 + c;

                foreach (char c in targetFQN)
                    hash = hash * 31 + c;

                foreach (char c in shadowTargetFQN)
                    hash = hash * 31 + c;

                return hash;
            }
        }
    }

    public static string AsRefString(this RefKind refKind)
        => refKind switch
        {
            RefKind.None => "",
            RefKind.Ref  => "ref ",
            RefKind.Out  => "out ",
            RefKind.In   => "in ",
            _            => "??? ",
        };

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

    /// <summary> Sanitizes an input string for use as a C# identifier. </summary>
    /// <param name="prependAt">If true, prepend '@' to the identifier to make it a verbatim identifier.</param>
    public static string Sanitize(this string name, char prepend = '\0')
    {
        if (string.IsNullOrWhiteSpace(name))
            return "???";

        var nameSpan = name.AsSpan();
        Span<char> span = stackalloc char[nameSpan.Length + (prepend is '\0' ? 0 : 1)];
        int i = 0;

        if (prepend is not '\0')
            span[i++] = prepend;

        // first char
        {
            char c = nameSpan[0];
            if (char.IsLetter(c) || c is '_')
                span[i++] = c;
            else
                span[i++] = '_';
        }

        // remaining chars
        foreach (var c in nameSpan[1..])
        {
            if (char.IsLetterOrDigit(c) || c is '_')
                span[i++] = c;
            else
                span[i++] = '_';
        }

        return span.Equals(nameSpan, StringComparison.Ordinal)
            ? name
            : span.ToString();
    }

    /// <summary>
    /// Deconstructs a type declaration and retrieves its namespace and parent type hierarchy.
    /// </summary>
    public static EquatableArray<string> DeconstructTypeDeclaration(
        MemberDeclarationSyntax memberDeclarationSyntax,
        SemanticModel semanticModel,
        CancellationToken ct,
        string? extraInterfaces = null
    )
    {
        IEnumerable<string> Walk(MemberDeclarationSyntax? syntax)
        {
            bool NavigateToParent()
                => (syntax = syntax?.Parent as MemberDeclarationSyntax)?.Kind()
                    is SyntaxKind.NamespaceDeclaration
                    or SyntaxKind.FileScopedNamespaceDeclaration
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

                // string FormatConstraintClauses(SyntaxList<TypeParameterConstraintClauseSyntax> constraintClauses)
                //     => string.Join(" ", constraintClauses.Select(x => x.WithFullyQualitiedReferences(semanticModel, ct)));

                string? line = syntax switch
                {
                    BaseNamespaceDeclarationSyntax x => $"namespace {x.Name}",
                    TypeDeclarationSyntax x          => $"partial {x.Keyword.ValueText} {x.Identifier}{x.TypeParameterList}{colon}{interfaces}",
                    _                                => null,
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
