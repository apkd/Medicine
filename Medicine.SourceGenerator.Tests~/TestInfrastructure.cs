using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

readonly record struct DiagnosticTest(string Name, Action Run);
readonly record struct GeneratorRunResult(
    Diagnostic[] GeneratorDiagnostics,
    Diagnostic[] CompilationDiagnostics,
    int GeneratedSourceCount
);

static class DiagnosticTester
{
    static readonly DiagnosticAnalyzer[] analyzers = CreateAnalyzers();

    public static void AssertEmitted(string diagnosticId, string source)
    {
        RoslynHarness.AssertContainsDiagnostic(
            GetDiagnostics(source),
            id: diagnosticId,
            because: "the dedicated repro snippet should emit this diagnostic"
        );
    }

    static Diagnostic[] GetDiagnostics(string source)
    {
        var compilation = RoslynHarness.CreateCompilation(Stubs.Core, source);
        var analyzerDiag = RoslynHarness.RunAnalyzers(compilation, analyzers);

        var generatorRun = RoslynHarness.RunGenerators(
            compilation: compilation,
            incrementalGenerators: [new InjectSourceGenerator(), new TrackSourceGenerator()]
        );

        var allDiagnostics = new List<Diagnostic>(
            analyzerDiag.Length + generatorRun.GeneratorDiagnostics.Length + generatorRun.CompilationDiagnostics.Length
        );
        allDiagnostics.AddRange(analyzerDiag);
        allDiagnostics.AddRange(generatorRun.GeneratorDiagnostics);
        allDiagnostics.AddRange(generatorRun.CompilationDiagnostics.Where(x => x.Id.StartsWith("MED", StringComparison.Ordinal)));
        return [.. allDiagnostics];
    }

    static DiagnosticAnalyzer[] CreateAnalyzers()
    {
        var assembly = typeof(OptionalUsageAnalyzer).Assembly;
        return assembly.GetTypes()
            .Where(type =>
                type is
                {
                    IsAbstract: false,
                    ContainsGenericParameters: false,
                } &&
                typeof(DiagnosticAnalyzer).IsAssignableFrom(type) &&
                type.GetCustomAttributes(typeof(DiagnosticAnalyzerAttribute), inherit: false).Length > 0
            )
            .Select(type => (DiagnosticAnalyzer)Activator.CreateInstance(type, nonPublic: true)!)
            .ToArray();
    }
}

static class SourceGeneratorTester
{
    public static void AssertGeneratesSource(
        string source,
        IIncrementalGenerator generator,
        AdditionalText[]? additionalTexts = null
    )
    {
        var run = Run(source, generator, additionalTexts);
        AssertNoGeneratorException(run);

        if (run.GeneratedSourceCount > 0)
            return;

        throw new InvalidOperationException(
            "Expected generator to add at least one source file." + Environment.NewLine +
            $"Actual diagnostics:{Environment.NewLine}{RoslynHarness.FormatDiagnostics(GetAllDiagnostics(run))}"
        );
    }

    public static void AssertGeneratesMoreSourcesThanBaseline(
        string baselineSource,
        string attributedSource,
        IIncrementalGenerator generator,
        AdditionalText[]? additionalTexts = null
    )
    {
        var baseline = Run(baselineSource, generator, additionalTexts);
        var attributed = Run(attributedSource, generator, additionalTexts);

        AssertNoGeneratorException(baseline);
        AssertNoGeneratorException(attributed);

        if (attributed.GeneratedSourceCount > baseline.GeneratedSourceCount)
            return;

        throw new InvalidOperationException(
            $"Expected attributed source to generate more files than baseline. Baseline={baseline.GeneratedSourceCount}, attributed={attributed.GeneratedSourceCount}.{Environment.NewLine}" +
            $"Actual diagnostics:{Environment.NewLine}{RoslynHarness.FormatDiagnostics(GetAllDiagnostics(attributed))}"
        );
    }

    public static void AssertEmittedDiagnostic(
        string diagnosticId,
        string source,
        IIncrementalGenerator generator,
        AdditionalText[]? additionalTexts = null
    )
    {
        var run = Run(source, generator, additionalTexts);
        var diagnostics = GetAllDiagnostics(run);

        RoslynHarness.AssertContainsDiagnostic(
            diagnostics,
            id: diagnosticId,
            because: "the dedicated repro snippet should emit this diagnostic"
        );

        if (diagnosticId.Equals("MED911", StringComparison.Ordinal))
            return;

        RoslynHarness.AssertDoesNotContainDiagnostic(
            diagnostics,
            id: "MED911",
            because: "happy-path source generator contracts should not rely on exception diagnostics"
        );
    }

    static GeneratorRunResult Run(string source, IIncrementalGenerator generator, AdditionalText[]? additionalTexts)
    {
        var compilation = RoslynHarness.CreateCompilation(Stubs.Core, source);
        return RoslynHarness.RunGenerators(
            compilation: compilation,
            incrementalGenerators: [generator],
            additionalTexts: additionalTexts ?? []
        );
    }

    static Diagnostic[] GetAllDiagnostics(GeneratorRunResult run)
    {
        var diagnostics = new List<Diagnostic>(run.GeneratorDiagnostics.Length + run.CompilationDiagnostics.Length);
        diagnostics.AddRange(run.GeneratorDiagnostics);
        diagnostics.AddRange(run.CompilationDiagnostics.Where(x => x.Id.StartsWith("MED", StringComparison.Ordinal)));
        return [.. diagnostics];
    }

    static void AssertNoGeneratorException(GeneratorRunResult run)
        => RoslynHarness.AssertDoesNotContainDiagnostic(
            diagnostics: GetAllDiagnostics(run),
            id: "MED911",
            because: "happy-path source generator contracts should not rely on exception diagnostics"
        );
}

static class SourceGeneratorFactory
{
    static readonly Type[] generatorTypes = typeof(InjectSourceGenerator).Assembly.GetTypes();

    public static IIncrementalGenerator Create(string generatorTypeName)
    {
        var type = generatorTypes.FirstOrDefault(x => x.Name.Equals(generatorTypeName, StringComparison.Ordinal));
        if (type is null)
            throw new InvalidOperationException($"Could not find source generator type '{generatorTypeName}'.");

        if (!typeof(IIncrementalGenerator).IsAssignableFrom(type))
            throw new InvalidOperationException($"Type '{generatorTypeName}' does not implement IIncrementalGenerator.");

        return (IIncrementalGenerator)Activator.CreateInstance(type, nonPublic: true)!;
    }
}

static class RoslynHarness
{
    static readonly CSharpParseOptions parseOptions = CSharpParseOptions.Default
        .WithLanguageVersion(LanguageVersion.Preview)
        .WithPreprocessorSymbols("MEDICINE_EXTENSIONS_LIB");

    static readonly CSharpCompilationOptions compilationOptions = new(OutputKind.DynamicallyLinkedLibrary);

    static readonly MetadataReference[] defaultReferences = BuildDefaultReferences();

    public static CSharpCompilation CreateCompilation(params string[] sourceFiles)
        => CSharpCompilation.Create(
            assemblyName: $"Medicine.SourceGenerator.ContractTests.{Guid.NewGuid():N}",
            syntaxTrees: sourceFiles
                .Select((x, i) =>
                    CSharpSyntaxTree.ParseText(
                        text: SourceText.From(x, Encoding.UTF8),
                        options: parseOptions,
                        path: $"Input{i + 1}.cs"
                    )
                ),
            references: defaultReferences,
            options: compilationOptions
        );

    public static Diagnostic[] RunAnalyzers(CSharpCompilation compilation, params DiagnosticAnalyzer[] analyzers)
        => compilation
            .WithAnalyzers([.. analyzers])
            .GetAnalyzerDiagnosticsAsync()
            .GetAwaiter()
            .GetResult()
            .ToArray();

    public static GeneratorRunResult RunGenerators(
        CSharpCompilation compilation,
        params IIncrementalGenerator[] incrementalGenerators
    ) => RunGenerators(compilation, incrementalGenerators, []);

    public static GeneratorRunResult RunGenerators(
        CSharpCompilation compilation,
        IIncrementalGenerator[] incrementalGenerators,
        AdditionalText[] additionalTexts
    )
    {
        var wrappedGenerators = incrementalGenerators
            .Select(static generator => generator.AsSourceGenerator())
            .ToArray();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: wrappedGenerators,
            additionalTexts: additionalTexts,
            parseOptions: parseOptions
        );

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out _
        );

        var runResult = driver.GetRunResult();
        return new(
            GeneratorDiagnostics: runResult.Diagnostics.ToArray(),
            CompilationDiagnostics: outputCompilation.GetDiagnostics().ToArray(),
            GeneratedSourceCount: runResult.Results.Sum(static x => x.GeneratedSources.Length)
        );
    }

    public static void AssertContainsDiagnostic(
        Diagnostic[] diagnostics,
        string id,
        string because
    )
    {
        foreach (var diagnostic in diagnostics)
            if (diagnostic.Id.Equals(id, StringComparison.Ordinal))
                return;

        throw new InvalidOperationException(
            $"Expected '{id}' because {because}.{Environment.NewLine}" +
            $"Actual:{Environment.NewLine}{FormatDiagnostics(diagnostics)}"
        );
    }

    public static void AssertDoesNotContainDiagnostic(
        Diagnostic[] diagnostics,
        string id,
        string because
    )
    {
        foreach (var diagnostic in diagnostics)
            if (diagnostic.Id.Equals(id, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Did not expect '{id}' because {because}.{Environment.NewLine}" +
                    $"Actual:{Environment.NewLine}{FormatDiagnostics(diagnostics)}"
                );
    }

    public static AdditionalText AdditionalText(string path, string content)
        => new InlineAdditionalText(path, content);

    public static string FormatDiagnostics(Diagnostic[] diagnostics)
        => diagnostics.Length != 0
            ? string.Join(
                Environment.NewLine,
                diagnostics.Select(static diagnostic =>
                {
                    var location = diagnostic.Location.GetLineSpan();
                    var path = string.IsNullOrWhiteSpace(location.Path) ? "<no-path>" : location.Path;
                    int line = location.StartLinePosition.Line + 1;
                    int column = location.StartLinePosition.Character + 1;
                    return $"{diagnostic.Id} {diagnostic.Severity} {path}:{line}:{column} {diagnostic.GetMessage()}";
                })
            )
            : "<none>";

    sealed class InlineAdditionalText(string path, string content) : AdditionalText
    {
        readonly SourceText text = SourceText.From(content, Encoding.UTF8);

        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default)
            => text;
    }

    static MetadataReference[] BuildDefaultReferences()
    {
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(tpa))
            throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES is not available.");

        var paths = tpa.Split([Path.PathSeparator], StringSplitOptions.RemoveEmptyEntries);
        var references = new MetadataReference[paths.Length];
        var index = 0;
        foreach (var path in paths)
            references[index++] = MetadataReference.CreateFromFile(path);

        return references;
    }
}
