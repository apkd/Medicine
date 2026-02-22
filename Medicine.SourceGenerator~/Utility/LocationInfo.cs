using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

public readonly record struct LocationInfo
{
    public FileLinePositionSpan FileLineSpan { get; }
    public TextSpan SourceSpan { get; }
    public bool IsInSource { get; }

    public LocationInfo(Location location)
    {
        FileLineSpan = location.GetLineSpan();
        SourceSpan = location.SourceSpan;
        IsInSource = location.IsInSource;
    }

    public static implicit operator LocationInfo?(Location? location)
        => location != null ? new LocationInfo(location) : (LocationInfo?)null;

    public static implicit operator LocationInfo(Location location)
        => new(location);

    public Location ToLocation()
        => IsInSource
            ? Location.Create(FileLineSpan.Path, SourceSpan, FileLineSpan.Span)
            : Location.None;
}
