/// <summary>
/// Carries source-generation metadata used by the wrapper pipeline helpers.
/// </summary>
public interface ISourceGeneratorPassData
{
    /// <summary>
    /// Generated hint name to use when adding source to the compilation.
    /// </summary>
    string? SourceGeneratorOutputFilename { get; }

    /// <summary>
    /// Captured error text for this pipeline item.
    /// </summary>
    string? SourceGeneratorError { get; init; }

    /// <summary>
    /// Source location associated with <see cref="SourceGeneratorError"/>.
    /// </summary>
    LocationInfo? SourceGeneratorLocation { get; set; }
}

/// <summary>
/// Extension helpers for <see cref="ISourceGeneratorPassData"/>.
/// </summary>
public static class SourceGeneratorPassDataExtensions
{
    /// <summary>
    /// Returns the captured error and location when the output contains an error.
    /// </summary>
    /// <typeparam name="T">Pipeline item type.</typeparam>
    /// <param name="output">Pipeline item to inspect.</param>
    /// <returns>
    /// The stored error payload, or <c>null</c> when the item does not carry an error.
    /// </returns>
    public static (string error, LocationInfo? location)? GetError<T>(this T output) where T : ISourceGeneratorPassData
        => output.SourceGeneratorError is { Length: > 0 } error
            ? (error, output.SourceGeneratorLocation)
            : null;
}
