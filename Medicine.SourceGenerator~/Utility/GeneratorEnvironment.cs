/// <summary>
/// The environment configuration for a source generator, encapsulating
/// active preprocessor symbols and assembly-level Medicine settings.
/// </summary>
public readonly record struct GeneratorEnvironment(ActivePreprocessorSymbolNames PreprocessorSymbols, MedicineSettings MedicineSettings)
{
    public readonly ActivePreprocessorSymbolNames PreprocessorSymbols = PreprocessorSymbols;
    public readonly MedicineSettings MedicineSettings = MedicineSettings;

    public bool ShouldEmitDocs => PreprocessorSymbols.Has(ActivePreprocessorSymbolNames.DEBUG);
    public bool IsEditor => PreprocessorSymbols.Has(ActivePreprocessorSymbolNames.UNITY_EDITOR);
    public bool IsUnity64OrNewer => PreprocessorSymbols.Has(ActivePreprocessorSymbolNames.UNITY_6000_4_OR_NEWER);
}
