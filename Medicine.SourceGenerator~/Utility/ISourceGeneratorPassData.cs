public interface ISourceGeneratorPassData
{
    string? SourceGeneratorOutputFilename { get; }
    string? SourceGeneratorError { get; init; }
    LocationInfo? SourceGeneratorErrorLocation { get; set; }
}

public static class SourceGeneratorPassDataExtensions
{
    public static (string error, LocationInfo? location)? GetError<T>(this T output) where T : ISourceGeneratorPassData
        => output.SourceGeneratorError is { Length: > 0 } error
            ? (error, output.SourceGeneratorErrorLocation)
            : null;
}
