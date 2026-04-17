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
    UNITY_6000_4_OR_NEWER = 1 << 2,
}

public static partial class ExtensionMethods
{
    /// <summary>
    /// Returns the known active preprocessor symbols for the parse options.
    /// </summary>
    /// <param name="parseOptions">Parse options to inspect.</param>
    /// <returns>The resulting symbol flags.</returns>
    public static ActivePreprocessorSymbolNames GetActivePreprocessorSymbols(this ParseOptions parseOptions)
    {
        var result = default(ActivePreprocessorSymbolNames);

        foreach (var name in parseOptions.PreprocessorSymbolNames)
        {
            result |= name switch
            {
                nameof(DEBUG) => DEBUG,
                nameof(UNITY_EDITOR) => UNITY_EDITOR,
                nameof(UNITY_6000_4_OR_NEWER) => UNITY_6000_4_OR_NEWER,
                _ => 0,
            };
        }

        return result;
    }

    /// <inheritdoc cref="GetActivePreprocessorSymbols(ParseOptions)"/>
    /// <param name="forceDebugValue">
    /// <c>1</c> forces <see cref="ActivePreprocessorSymbolNames.DEBUG"/>, <c>2</c> removes it, and any other value leaves it unchanged.
    /// </param>
    public static ActivePreprocessorSymbolNames GetActivePreprocessorSymbols(this ParseOptions parseOptions, int forceDebugValue)
    {
        var symbol = parseOptions.GetActivePreprocessorSymbols();

        symbol = forceDebugValue switch
        {
            1 => symbol | DEBUG,
            2 => symbol & ~DEBUG,
            _ => symbol,
        };

        return symbol;
    }

    public static bool Has(this ActivePreprocessorSymbolNames symbol, ActivePreprocessorSymbolNames flag)
        => (symbol & flag) > 0;
}
