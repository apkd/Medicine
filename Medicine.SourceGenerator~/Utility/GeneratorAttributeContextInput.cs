using Microsoft.CodeAnalysis;

record struct GeneratorAttributeContextInput : IGeneratorTransformOutput
{
    public string? SourceGeneratorOutputFilename { get; init; }
    public string? SourceGeneratorError { get; init; }
    public LocationInfo? SourceGeneratorErrorLocation { get; set; }
    public EquatableIgnore<GeneratorAttributeSyntaxContext> Context { get; init; }
}
