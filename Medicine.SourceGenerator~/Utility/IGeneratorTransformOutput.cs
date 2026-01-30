public interface IGeneratorTransformOutput
{
    string? SourceGeneratorOutputFilename { get; }
    string? SourceGeneratorError { get; init; }
    LocationInfo? SourceGeneratorErrorLocation { get; set; }
}

public static class GeneratorTransformOutputExtensions
{
    public static (string error, LocationInfo? location)? GetError<T>(this T output) where T : IGeneratorTransformOutput
        => output.SourceGeneratorError is { Length: > 0 } error
            ? (output.SourceGeneratorError, output.SourceGeneratorErrorLocation)
            : null;
}
