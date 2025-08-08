using Microsoft.CodeAnalysis;

public interface IGeneratorTransformOutput
{
    string? SourceGeneratorOutputFilename { get; }
    string? SourceGeneratorError { get; init; }
    EquatableIgnore<Location?> SourceGeneratorErrorLocation { get; set; }
}