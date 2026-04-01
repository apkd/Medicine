// ReSharper disable UnusedAutoPropertyAccessor.Global
using Microsoft.CodeAnalysis;

public record struct ContextWithCacheGeneratorInput : ISourceGeneratorPassData
{
    public string? SourceGeneratorOutputFilename { get; init; }
    public string? SourceGeneratorError { get; init; }
    public LocationInfo? SourceGeneratorLocation { get; set; }
    public EquatableIgnore<GeneratorAttributeSyntaxContext> Context { get; init; }
    public ulong Checksum64ForCache { get; init; }
}
