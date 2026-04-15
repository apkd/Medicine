using Microsoft.CodeAnalysis;

/// <summary>
/// Wraps a <see cref="GeneratorAttributeSyntaxContext"/> with pipeline metadata.
/// </summary>
public record struct GeneratorAttributeContextInput : ISourceGeneratorPassData
{
    /// <inheritdoc/>
    public string? SourceGeneratorOutputFilename { get; init; }

    /// <inheritdoc/>
    public string? SourceGeneratorError { get; init; }

    /// <inheritdoc/>
    public LocationInfo? SourceGeneratorLocation { get; set; }

    /// <summary>
    /// Attribute context being processed by the generator pass.
    /// </summary>
    public GeneratorAttributeSyntaxContext Context { get; init; }

    /// <summary>
    /// Creates a pipeline input for the specified attribute context.
    /// </summary>
    /// <param name="context">Attribute context to wrap.</param>
    /// <param name="outputFilenameFunc">
    /// Computes the generated hint name for <paramref name="context"/>.
    /// </param>
    public GeneratorAttributeContextInput(GeneratorAttributeSyntaxContext context, Func<GeneratorAttributeSyntaxContext, string?> outputFilenameFunc)
    {
        Context = context;
        SourceGeneratorLocation = context.TargetNode.GetLocation();
        SourceGeneratorOutputFilename = outputFilenameFunc(context);
    }

    bool IEquatable<GeneratorAttributeContextInput>.Equals(GeneratorAttributeContextInput other)
        => false;
}


