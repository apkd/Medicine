using Microsoft.CodeAnalysis;

readonly record struct MedicineSettings
{
    public readonly ActivePreprocessorSymbolNames PreprocessorSymbolNames;
    public readonly bool? MakePublic;
    public readonly bool AlwaysTrackInstanceIndices;

    public MedicineSettings((Compilation Compilation, ParseOptions ParseOptions) input)
    {
        var args = input.Compilation.Assembly
            .GetAttribute(Constants.MedicineSettingsAttributeFQN)
            .GetAttributeConstructorArguments();

        MakePublic = args.Get("makePublic", true);
        AlwaysTrackInstanceIndices = args.Get("alwaysTrackInstanceIndices", false);
        PreprocessorSymbolNames = input.ParseOptions.GetActivePreprocessorSymbols();
        PreprocessorSymbolNames.SetForceDebug(args.Get("debug", 0));
    }
}