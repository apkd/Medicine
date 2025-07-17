using System.Runtime.CompilerServices;
using System.Text;

public readonly record struct LongGenericTypeName(BaseSourceGenerator SourceGenerator, string? TypeName)
{
}

#pragma warning disable CS9113
[InterpolatedStringHandler]
public readonly struct StringBuilderInterpolatedString(int literalLength, int formattedCount, StringBuilder stringBuilder)
{
    public void AppendLiteral(string value)
        => stringBuilder.Append(value);

    public void AppendFormatted(string? value)
        => stringBuilder.Append(value);

    public void AppendFormatted(int value)
        => stringBuilder.Append(value);

    public void AppendFormatted(int value, string format)
        => stringBuilder.Append(value.ToString(format));

    public void AppendFormatted(long value)
        => stringBuilder.Append(value);

    public void AppendFormatted(long value, string format)
        => stringBuilder.Append(value.ToString(format));

    public void AppendFormatted(float value)
        => stringBuilder.Append(value);

    public void AppendFormatted(float value, string format)
        => stringBuilder.Append(value.ToString(format));

    public void AppendFormatted(double value)
        => stringBuilder.Append(value);

    public void AppendFormatted(double value, string format)
        => stringBuilder.Append(value.ToString(format));

    public void AppendFormatted(char value)
        => stringBuilder.Append(value);

    public void AppendFormatted(bool value)
        => stringBuilder.Append(value);

    public void AppendFormatted(in LongGenericTypeName value)
    {
        var (sourceGenerator, typeName) = value;

        if (typeName is null)
            return;

        int tuple = 0;

        foreach (char ch in typeName.AsSpan())
        {
            switch (ch)
            {
                case '<':
                    sourceGenerator.Text.Append(ch);
                    sourceGenerator.IncreaseIndent();
                    sourceGenerator.Line.Append("");
                    break;

                case '>':
                    sourceGenerator.Text.Append(ch);
                    sourceGenerator.DecreaseIndent();
                    break;

                case ',':
                    sourceGenerator.Text.Append(ch);
                    sourceGenerator.Line.Append("");
                    break;

                case ' ':
                    if (tuple > 0)
                        goto default;

                    continue;

                case '(':
                    tuple += 1;
                    goto default;

                case ')':
                    tuple -= 1;
                    goto default;

                default:
                    sourceGenerator.Text.Append(ch);
                    break;
            }
        }
    }
}