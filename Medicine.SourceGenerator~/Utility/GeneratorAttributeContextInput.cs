using Microsoft.CodeAnalysis;

record struct GeneratorAttributeContextInput : ISourceGeneratorPassData
{
    public string? SourceGeneratorOutputFilename { get; init; }
    public string? SourceGeneratorError { get; init; }
    public LocationInfo? SourceGeneratorErrorLocation { get; set; }
    public GeneratorAttributeSyntaxContext Context { get; init; }

    bool IEquatable<GeneratorAttributeContextInput>.Equals(GeneratorAttributeContextInput other)
        => false;
}
