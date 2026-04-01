using Microsoft.CodeAnalysis;

public static class MedicineGeneratorEnvironmentExtensions
{
    extension<TSource>(IncrementalValuesProvider<TSource> source)
    {
        public IncrementalValuesProvider<(TSource Values, GeneratorEnvironment Environment)> CombineWithGeneratorEnvironment(IncrementalGeneratorInitializationContext context)
            => source.Combine(context.GetGeneratorEnvironment());
    }

    extension(IncrementalGeneratorInitializationContext init)
    {
        public IncrementalValueProvider<GeneratorEnvironment> GetGeneratorEnvironment()
            => init
                .CompilationProvider
                .Combine(init.ParseOptionsProvider)
                .Select((x, ct) =>
                    {
                        var args = x.Left.Assembly
                            .GetAttribute(Constants.MedicineSettingsAttributeFQN)
                            .GetAttributeConstructorArguments(ct);

                        return new GeneratorEnvironment(
                            PreprocessorSymbols: x.Right.GetActivePreprocessorSymbols(forceDebugValue: args.Get("debug", 0)),
                            MedicineSettings: new()
                            {
                                MakePublic = args.Get("makePublic", true),
                                SingletonStrategy = args.Get("singletonStrategy", SingletonStrategy.Replace),
                            }
                        );
                    }
                );
    }
}
