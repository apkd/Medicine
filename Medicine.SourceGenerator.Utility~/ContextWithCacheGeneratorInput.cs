// ReSharper disable UnusedAutoPropertyAccessor.Global
using Microsoft.CodeAnalysis;

/// <summary>
/// Carries attribute context data together with an explicit cache checksum.
/// </summary>
public record struct ContextWithCacheGeneratorInput : ISourceGeneratorPassData
{
    /// <inheritdoc/>
    public string? SourceGeneratorOutputFilename { get; init; }

    /// <inheritdoc/>
    public string? SourceGeneratorError { get; init; }

    /// <inheritdoc/>
    public LocationInfo? SourceGeneratorLocation { get; set; }

    /// <summary>
    /// Attribute context excluded from equality so cache invalidation is driven by <see cref="Checksum64ForCache"/>.
    /// </summary>
    public EquatableIgnore<GeneratorAttributeSyntaxContext> Context { get; init; }

    /// <summary>
    /// Value compared by the incremental pipeline to decide whether the cached result is still valid.
    /// </summary>
    public ulong Checksum64ForCache { get; init; }
}
