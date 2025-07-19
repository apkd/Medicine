using Microsoft.CodeAnalysis;

readonly record struct MedicineSettings
{
    public readonly bool? MakePublic;
    public readonly int ForceDebug;
    public readonly bool AlwaysTrackInstanceIndices;

    public MedicineSettings(Compilation compilation)
    {
        var args = compilation.Assembly
            .GetAttribute(Constants.MedicineSettingsAttributeFQN)
            .GetAttributeConstructorArguments();

        MakePublic = args.Get("makePublic", true);
        ForceDebug = args.Get("debug", 0);
        AlwaysTrackInstanceIndices = args.Get("alwaysTrackInstanceIndices", false);
    }
}
