using Microsoft.CodeAnalysis;

/// <summary>
/// Represents configuration settings extracted from the [MedicineSettings] attribute
/// defined in the given assembly in the current compilation, as well as other compilation parameters.
/// </summary>
readonly record struct MedicineSettings
{
    public readonly ActivePreprocessorSymbolNames PreprocessorSymbolNames;
    public readonly bool? MakePublic;
    public readonly bool AlwaysTrackInstanceIndices;

    public MedicineSettings((Compilation Compilation, ParseOptions ParseOptions) input, CancellationToken ct)
    {
        var args = input.Compilation.Assembly
            .GetAttribute(Constants.MedicineSettingsAttributeFQN)
            .GetAttributeConstructorArguments(ct);

        MakePublic = args.Get("makePublic", true);
        AlwaysTrackInstanceIndices = args.Get("alwaysTrackInstanceIndices", false);
        PreprocessorSymbolNames = input.ParseOptions.GetActivePreprocessorSymbols();
        PreprocessorSymbolNames.SetForceDebug(args.Get("debug", 0));
    }
}