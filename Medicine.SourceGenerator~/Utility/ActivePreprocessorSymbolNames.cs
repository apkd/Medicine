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
        var result = default(ActivePreprocessorSymbolNames);

        foreach (var name in parseOptions.PreprocessorSymbolNames)
        {
            result |= name switch
            {
                nameof(DEBUG) => DEBUG,
                nameof(UNITY_EDITOR) => UNITY_EDITOR,
                _ => 0,
            };
        }

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
