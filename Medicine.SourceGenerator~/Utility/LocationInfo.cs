using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

readonly record struct LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public Location ToLocation()
        => Location.Create(FilePath, TextSpan, LineSpan);

    public bool IsValid => this is
    {
        FilePath: { Length : > 0 },
        TextSpan: { IsEmpty: false },
        LineSpan: { Start.Line: not 0 } or { End.Line: not 0 },
    };

    public LocationInfo(Location? location)
        : this(location?.SourceTree?.FilePath ?? "", location?.SourceSpan ?? default, location?.GetLineSpan().Span ?? default) { }
}
