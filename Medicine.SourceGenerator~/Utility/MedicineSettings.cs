/// <summary>
/// Represents configuration settings extracted from the [MedicineSettings] attribute
/// defined in the given assembly in the current compilation, as well as other compilation parameters.
/// </summary>
public readonly record struct MedicineSettings
{
    public SingletonStrategy SingletonStrategy { get; init; }
    public bool? MakePublic { get; init; }
}
