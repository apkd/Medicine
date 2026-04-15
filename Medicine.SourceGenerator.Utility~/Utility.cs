using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

/// <summary>
/// Shared helpers for source-generator and analyzer code.
/// </summary>
public static class Utility
{
    /// <summary>
    /// Diagnostic used when wrapper helpers surface an exception to the compilation.
    /// </summary>
    public static readonly DiagnosticDescriptor ExceptionDiagnosticDescriptor = new(
        id: "MED911",
        title: "Exception",
        messageFormat: "Exception: {0}",
        category: "Exception",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    /// <summary>
    /// Returns a deterministic hint name for an error output file.
    /// </summary>
    /// <param name="location">Source location associated with the error, when available.</param>
    /// <param name="error">Error text to include in the hash.</param>
    /// <returns>A generated hint name for fallback error output.</returns>
    public static string GetErrorOutputFilename(LocationInfo? location, string error)
    {
        string filename = Path.GetFileNameWithoutExtension(location?.FileLineSpan.Path ?? "");
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

    /// <summary>
    /// Returns a deterministic generated hint name for a source output.
    /// </summary>
    /// <param name="filePath">Path of the source file that triggered generation.</param>
    /// <param name="targetNodeName">Target syntax or symbol name that distinguishes this output.</param>
    /// <param name="label">Optional prefix added to the generated hint name.</param>
    /// <param name="additionalNameForHash">Additional text that participates in the hash without appearing in the filename.</param>
    /// <param name="includeFilename">
    /// Whether to include the source file name when it differs from <paramref name="targetNodeName"/>.
    /// </param>
    /// <returns>A stable generated hint name ending in <c>.g.cs</c>.</returns>
    public static string GetOutputFilename(string filePath, string targetNodeName, string label = "", string additionalNameForHash = "", bool includeFilename = true)
    {
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
        string typename;
        {
            var source = targetNodeName.AsSpan();
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

        return includeFilename && fileNameWithoutExtension != typename
            ? $"{label}{fileNameWithoutExtension}.{typename}.{Hash():x8}.g.cs"
            : $"{label}{typename}.{Hash():x8}.g.cs";

        int Hash()
        {
            unchecked
            {
                int hash = 23;

                foreach (char c in fileNameWithoutExtension)
                    hash = hash * 31 + c;

                foreach (char c in targetNodeName)
                    hash = hash * 31 + c;

                foreach (char c in additionalNameForHash)
                    hash = hash * 31 + c;

                foreach (char c in label)
                    hash = hash * 31 + c;

                return hash;
            }
        }
    }

    /// <summary>
    /// Returns the C# modifier text for a Roslyn <see cref="RefKind"/>.
    /// </summary>
    /// <param name="refKind">Reference kind to format.</param>
    /// <returns><c>ref </c>, <c>out </c>, <c>in </c>, or an empty string.</returns>
    public static string AsRefString(this RefKind refKind)
        => refKind switch
        {
            RefKind.None => "",
            RefKind.Ref  => "ref ",
            RefKind.Out  => "out ",
            RefKind.In   => "in ",
            _            => "??? ",
        };

    /// <summary>
    /// Adds a using directive when the document does not already import the namespace.
    /// </summary>
    /// <param name="editor">Document editor to update.</param>
    /// <param name="namespaceName">Namespace to import.</param>
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

    /// <summary>
    /// Rewrites a string into a valid C# identifier.
    /// </summary>
    /// <param name="name">Name to sanitize.</param>
    /// <param name="prepend">Optional character to insert before the identifier.</param>
    /// <returns>A sanitized identifier, or <c>???</c> when <paramref name="name"/> is blank.</returns>
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
    /// Returns the namespace and containing type declarations needed to recreate a type's declaration hierarchy.
    /// </summary>
    /// <param name="memberDeclarationSyntax">Type declaration whose containing hierarchy should be emitted.</param>
    /// <param name="extraInterfaces">
    /// Optional interface clause appended to the first emitted type declaration.
    /// </param>
    /// <returns>Declaration lines ordered from outermost container to innermost type.</returns>
    public static EquatableArray<string> DeconstructTypeDeclaration(
        MemberDeclarationSyntax memberDeclarationSyntax,
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
