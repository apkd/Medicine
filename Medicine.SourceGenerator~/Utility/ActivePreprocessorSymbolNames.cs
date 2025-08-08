using Microsoft.CodeAnalysis;
using static ActivePreprocessorSymbolNames;

/// <summary>
/// Represents the active preprocessor symbols defined in the current compilation.
/// </summary>
[Flags]
public enum ActivePreprocessorSymbolNames
{
    UNITY_EDITOR = 1 << 0,
    DEBUG = 1 << 1,
}

public static partial class ExtensionMethods
{
    public static ActivePreprocessorSymbolNames GetActivePreprocessorSymbols(this ParseOptions parseOptions)
    {
        var names = parseOptions.PreprocessorSymbolNames.ToArray();
        var result = default(ActivePreprocessorSymbolNames);

        if (names.Contains(nameof(DEBUG)))
            result |= DEBUG;

        if (names.Contains(nameof(UNITY_EDITOR)))
            result |= UNITY_EDITOR;

        return result;
    }

    public static bool Has(this ActivePreprocessorSymbolNames symbol, ActivePreprocessorSymbolNames flag)
        => (symbol & flag) > 0;

    public static void SetForceDebug(this ref ActivePreprocessorSymbolNames symbol, int value)
        => symbol = value switch
        {
            1 => symbol | DEBUG,
            2 => symbol & ~DEBUG,
            _ => symbol,
        };
}