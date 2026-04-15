using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

/// <summary>
/// Value type for storing the properties of a <see cref="Location"/>.
/// </summary>
public readonly record struct LocationInfo
{
    /// <summary>
    /// File and line span for the location.
    /// </summary>
    public FileLinePositionSpan FileLineSpan { get; }

    /// <summary>
    /// Absolute character span within the source text.
    /// </summary>
    public TextSpan SourceSpan { get; }

    /// <summary>
    /// Indicates whether this location refers to source text.
    /// </summary>
    public bool IsInSource { get; }

    /// <summary>
    /// Captures the source-backed information from a Roslyn <see cref="Location"/>.
    /// </summary>
    /// <param name="location">Location to snapshot.</param>
    public LocationInfo(Location location)
    {
        FileLineSpan = location.GetLineSpan();
        SourceSpan = location.SourceSpan;
        IsInSource = location.IsInSource;
    }

    /// <summary>
    /// Converts a nullable Roslyn <see cref="Location"/> into a nullable <see cref="LocationInfo"/>.
    /// </summary>
    /// <param name="location">Location to convert.</param>
    /// <returns>
    /// A snapshot of <paramref name="location"/>, or <c>null</c> when <paramref name="location"/> is <c>null</c>.
    /// </returns>
    public static implicit operator LocationInfo?(Location? location)
        => location != null ? new LocationInfo(location) : (LocationInfo?)null;

    /// <summary>
    /// Converts a Roslyn <see cref="Location"/> into a <see cref="LocationInfo"/>.
    /// </summary>
    /// <param name="location">Location to convert.</param>
    /// <returns>A snapshot of <paramref name="location"/>.</returns>
    public static implicit operator LocationInfo(Location location)
        => new(location);

    /// <summary>
    /// Recreates a Roslyn <see cref="Location"/> from this snapshot.
    /// </summary>
    /// <returns>
    /// A source-backed location, or <see cref="Location.None"/> when <see cref="IsInSource"/> is <c>false</c>.
    /// </returns>
    public Location ToLocation()
        => IsInSource
            ? Location.Create(FileLineSpan.Path, SourceSpan, FileLineSpan.Span)
            : Location.None;
}
