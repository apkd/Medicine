using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

// ReSharper disable UnusedMember.Global

/// <summary>
/// These extensions wrap various <see cref="IncrementalValueProviderExtensions"/>,
/// <see cref="IncrementalGeneratorInitializationContext"/> and <see cref="SyntaxValueProvider"/>
/// methods so that we can have more control over the generator lifecycle.
/// This lets us catch exceptions and handle errors and measure the time it takes to run the generator stages
/// and wrap the code generation process in a simpler API based on <see cref="SourceWriter"/>.
/// </summary>
public static class SourceGeneratorExtensions
{
    public static IncrementalValueProvider<TResult> SelectEx<TSource, TResult>(this IncrementalValueProvider<TSource> source, Func<TSource, CancellationToken, TResult> selector)
        where TResult : IGeneratorTransformOutput, new()
        => source.Select((input, ct) => TrySelect(input, ct, selector));

    public static IncrementalValuesProvider<TResult> SelectEx<TSource, TResult>(this IncrementalValuesProvider<TSource> source, Func<TSource, CancellationToken, TResult> selector)
        where TResult : IGeneratorTransformOutput, new()
        => source.Select((input, ct) => TrySelect(input, ct, selector));

    public static IncrementalValuesProvider<TResult> SelectManyEx<TSource, TResult>(this IncrementalValueProvider<TSource> source, Func<TSource, CancellationToken, ImmutableArray<TResult>> selector)
        where TResult : IGeneratorTransformOutput, new()
        => source.SelectMany((input, ct) => TrySelectMany(input, ct, selector));

    public static IncrementalValuesProvider<TResult> SelectManyEx<TSource, TResult>(this IncrementalValueProvider<TSource> source, Func<TSource, CancellationToken, IEnumerable<TResult>> selector)
        where TResult : IGeneratorTransformOutput, new()
        => source.SelectMany((input, ct) => TrySelectMany(input, ct, (x, y) => selector(x, y).ToImmutableArray()));

    public static IncrementalValuesProvider<TResult> SelectManyEx<TSource, TResult>(this IncrementalValuesProvider<TSource> source, Func<TSource, CancellationToken, ImmutableArray<TResult>> selector)
        where TResult : IGeneratorTransformOutput, new()
        => source.SelectMany((input, ct) => TrySelectMany(input, ct, selector));

    public static IncrementalValuesProvider<TResult> SelectManyEx<TSource, TResult>(this IncrementalValuesProvider<TSource> source, Func<TSource, CancellationToken, IEnumerable<TResult>> selector)
        where TResult : IGeneratorTransformOutput, new()
        => source.SelectMany((input, ct) => TrySelectMany(input, ct, (x, y) => selector(x, y).ToImmutableArray()));

    public static void RegisterSourceOutputEx<TInput>(
        this IncrementalGeneratorInitializationContext init,
        IncrementalValueProvider<TInput> source,
        Action<SourceProductionContext, SourceWriter, TInput> action
    ) where TInput : IGeneratorTransformOutput
        => init.RegisterSourceOutput(
            source,
            action: (context, input) => GenerateSource(context, input, action)
        );

    public static void RegisterSourceOutputEx<TInput>(
        this IncrementalGeneratorInitializationContext init,
        IncrementalValuesProvider<TInput> source,
        Action<SourceProductionContext, SourceWriter, TInput> action
    ) where TInput : IGeneratorTransformOutput
        => init.RegisterSourceOutput(
            source,
            action: (context, input) => GenerateSource(context, input, action)
        );

    public static void RegisterImplementationSourceOutputEx<TInput>(
        this IncrementalGeneratorInitializationContext init,
        IncrementalValueProvider<TInput> source,
        Action<SourceProductionContext, SourceWriter, TInput> action
    ) where TInput : IGeneratorTransformOutput
        => init.RegisterSourceOutput(
            source,
            action: (context, input) => GenerateSource(context, input, action)
        );

    public static void RegisterImplementationSourceOutputEx<TInput>(
        this IncrementalGeneratorInitializationContext init,
        IncrementalValuesProvider<TInput> source,
        Action<SourceProductionContext, SourceWriter, TInput> action
    ) where TInput : IGeneratorTransformOutput
        => init.RegisterSourceOutput(
            source,
            action: (context, input) => GenerateSource(context, input, action)
        );

    public static IncrementalValuesProvider<TResult> ForAttributeWithMetadataNameEx<TResult>(
        this SyntaxValueProvider provider,
        string fullyQualifiedMetadataName,
        Func<SyntaxNode, CancellationToken, bool> predicate,
        Func<GeneratorAttributeSyntaxContext, CancellationToken, TResult> transform
    )
        where TResult : IGeneratorTransformOutput, new()
        => provider.ForAttributeWithMetadataName<TResult>(
            fullyQualifiedMetadataName,
            predicate,
            transform: (context, ct) =>
            {
                try
                {
                    var time = Stopwatch.StartNew();
                    var output = transform(context, ct);
                    float elapsed = (float)time.Elapsed.TotalMilliseconds;
                    if (output is { SourceGeneratorOutputFilename: { Length: > 0 } filename })
                        MedicineMetrics.Reporter?.Report(filename, Stat.TransformTimeMs, elapsed);

                    return output;
                }
                catch (Exception exception)
                {
                    return ErrorResult<TResult>(exception, context.TargetNode.GetLocation());
                }
            }
        );

    static TResult TrySelect<TSource, TResult>(TSource input, CancellationToken ct, Func<TSource, CancellationToken, TResult> selector)
        where TResult : IGeneratorTransformOutput, new()
    {
        try
        {
            var time = Stopwatch.StartNew();
            var output = selector(input, ct);
            float elapsed = (float)time.Elapsed.TotalMilliseconds;
            if (output is { SourceGeneratorOutputFilename: { Length: > 0 } filename })
                MedicineMetrics.Reporter?.Report(filename, Stat.TransformTimeMs, elapsed);

            return output;
        }
        catch (Exception exception)
        {
            return ErrorResult<TResult>(exception, input);
        }
    }

    static ImmutableArray<TResult> TrySelectMany<TSource, TResult>(TSource input, CancellationToken ct, Func<TSource, CancellationToken, ImmutableArray<TResult>> selector)
        where TResult : IGeneratorTransformOutput, new()
    {
        try
        {
            var time = Stopwatch.StartNew();
            var output = selector(input, ct);
            float elapsed = (float)time.Elapsed.TotalMilliseconds;
            if (output.FirstOrDefault() is { SourceGeneratorOutputFilename: { Length: > 0 } filename })
                MedicineMetrics.Reporter?.Report(filename, Stat.TransformTimeMs, elapsed);

            return output;
        }
        catch (Exception exception)
        {
            return ImmutableArray.Create(ErrorResult<TResult>(exception, input));
        }
    }

    static TResult ErrorResult<TResult>(Exception exception, object? locationSource) where TResult : IGeneratorTransformOutput, new()
        => locationSource switch
        {
            IGeneratorTransformOutput { SourceGeneratorErrorLocation: { } location } output when location.IsInSource
                => new() { SourceGeneratorError = $"{exception}", SourceGeneratorErrorLocation = location },
            Location location
                => new() { SourceGeneratorError = $"{exception}", SourceGeneratorErrorLocation = new LocationInfo(location) },
            LocationInfo locationInfo when locationInfo.IsInSource
                => new() { SourceGeneratorError = $"{exception}", SourceGeneratorErrorLocation = locationInfo },
            _
                => new() { SourceGeneratorError = $"{exception}" },
        };

    static void GenerateSource<TInput>(
        SourceProductionContext context,
        TInput input,
        Action<SourceProductionContext, SourceWriter, TInput> action
    ) where TInput : IGeneratorTransformOutput
    {
        using var writer = new SourceWriter();
        string? error = input.SourceGeneratorError;

        // we try to provide the output path even in case of errors
        if (error is not { Length: > 0 } && input.SourceGeneratorOutputFilename is not { Length: > 0 })
            error = "No output filename specified. This is a bug in the source generator.";

        if (error is not { Length: > 0 })
        {
            // main path - try to generate the source
            try
            {
                var time = Stopwatch.StartNew();

                // invoke generator action
                action(context, writer, input);

                float elapsed = (float)time.Elapsed.TotalMilliseconds;
                MedicineMetrics.Reporter?.Report(input.SourceGeneratorOutputFilename, Stat.SourceGenerationTimeMs, elapsed);
                AddOutputSourceTextToCompilation(input.SourceGeneratorOutputFilename!, context, writer);
                return;
            }
            catch (Exception exception)
            {
                error = $"{exception}";
            }
        }

        // handle errors/exceptions
        try
        {
            var errorLocation = input.SourceGeneratorErrorLocation?.ToLocation();
            context.ReportDiagnostic(Diagnostic.Create(descriptor: Utility.ExceptionDiagnosticDescriptor, location: errorLocation, messageArgs: error));

            string filename = input.SourceGeneratorOutputFilename ?? Utility.GetErrorOutputFilename(input.SourceGeneratorErrorLocation, error);
            AddOutputSourceTextToCompilation(filename, context, writer);
        }
        catch (Exception exception)
        {
            context.ReportDiagnostic(Diagnostic.Create(descriptor: Utility.ExceptionDiagnosticDescriptor, location: Location.None, messageArgs: $"{error}\n{exception}"));
        }
    }

    static void AddOutputSourceTextToCompilation(string filename, SourceProductionContext context, SourceWriter writer)
    {
        string output = writer.ToString();

        if (MedicineMetrics.Reporter is { } reporter)
        {
            int lineCount = 0;
            foreach (var c in output.AsSpan())
                if (c is '\n')
                    lineCount++;

            reporter.Report(filename, Stat.LinesOfCodeGenerated, lineCount);
        }

        context.AddSource(filename, SourceText.From(output, Encoding.UTF8));
    }
}
