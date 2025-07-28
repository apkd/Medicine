#nullable enable

using System.ComponentModel;
using Microsoft.CodeAnalysis;
using static ActiveProprocessorSymbolNames;

[System.Flags]
public enum ActiveProprocessorSymbolNames
{
    UNITY_EDITOR = 1 << 0,
    DEBUG = 1 << 1,
}

public static partial class ExtensionMethods
{
    public static ActiveProprocessorSymbolNames GetActivePreprocessorSymbols(this SemanticModel semanticModel)
    {
        var names = semanticModel.SyntaxTree.Options.PreprocessorSymbolNames.ToArray();
        var result = default(ActiveProprocessorSymbolNames);

        if (names.Contains(nameof(DEBUG)))
            result |= DEBUG;

        if (names.Contains(nameof(UNITY_EDITOR)))
            result |= UNITY_EDITOR;

        return result;
    }

    public static bool Has(this ActiveProprocessorSymbolNames symbol, ActiveProprocessorSymbolNames flag)
        => (symbol & flag) > 0;

    public static void SetForceDebug(this ref ActiveProprocessorSymbolNames symbol, int value)
        => symbol = value switch
        {
            1 => symbol | DEBUG,
            2 => symbol & ~DEBUG,
            _ => symbol,
        };
}