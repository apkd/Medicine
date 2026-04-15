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
    extension<TSource>(IncrementalValueProvider<TSource> source)
    {
        /// <summary>
        /// Projects each input into zero or more outputs with exception handling and metric reporting.
        /// </summary>
        /// <typeparam name="TResult">Output item type.</typeparam>
        /// <param name="selector">Projection function.</param>
        /// <returns>A provider that emits the projected outputs.</returns>
        public IncrementalValuesProvider<TResult> SelectManyEx<TResult>(Func<TSource, CancellationToken, ImmutableArray<TResult>> selector)
            where TResult : ISourceGeneratorPassData, new()
            => source.SelectMany((input, ct) => TrySelectMany(input, ct, selector));

        /// <summary>
        /// Projects each input into zero or more outputs with exception handling and metric reporting.
        /// </summary>
        /// <typeparam name="TResult">Output item type.</typeparam>
        /// <param name="selector">Projection function.</param>
        /// <returns>A provider that emits the projected outputs.</returns>
        public IncrementalValuesProvider<TResult> SelectManyEx<TResult>(Func<TSource, CancellationToken, IEnumerable<TResult>> selector)
            where TResult : ISourceGeneratorPassData, new()
            => source.SelectMany((input, ct) => TrySelectMany(input, ct, (x, y) => selector(x, y).ToImmutableArray()));

        /// <summary>
        /// Projects each input into a single output with exception handling and metric reporting.
        /// </summary>
        /// <typeparam name="TResult">Output item type.</typeparam>
        /// <param name="selector">Projection function.</param>
        /// <returns>A provider that emits the projected output.</returns>
        public IncrementalValueProvider<TResult> SelectEx<TResult>(Func<TSource, CancellationToken, TResult> selector)
            where TResult : ISourceGeneratorPassData, new()
            => source.Select((input, ct) => TrySelect(input, ct, selector));

        /// <summary>
        /// Combines three incremental providers into a single tuple provider.
        /// </summary>
        /// <typeparam name="T2">Second provider value type.</typeparam>
        /// <typeparam name="T3">Third provider value type.</typeparam>
        /// <param name="provider2">Second provider.</param>
        /// <param name="provider3">Third provider.</param>
        /// <returns>A provider that yields values from all three inputs.</returns>
        public IncrementalValueProvider<(TSource First, T2 Second, T3 Third)> Combine<T2, T3>(IncrementalValueProvider<T2> provider2, IncrementalValueProvider<T3> provider3)
            => source
                .Combine(provider2)
                .Combine(provider3)
                .Select((tuple, token) => (tuple.Left.Left, tuple.Left.Right, tuple.Right));
    }

    extension<TSource>(IncrementalValuesProvider<TSource> source)
    {
        /// <summary>
        /// Projects each input into zero or more outputs with exception handling and metric reporting.
        /// </summary>
        /// <typeparam name="TResult">Output item type.</typeparam>
        /// <param name="selector">Projection function.</param>
        /// <returns>A provider that emits the projected outputs.</returns>
        public IncrementalValuesProvider<TResult> SelectManyEx<TResult>(Func<TSource, CancellationToken, ImmutableArray<TResult>> selector)
            where TResult : ISourceGeneratorPassData, new()
            => source.SelectMany((input, ct) => TrySelectMany(input, ct, selector));

        /// <summary>
        /// Projects each input into zero or more outputs with exception handling and metric reporting.
        /// </summary>
        /// <typeparam name="TResult">Output item type.</typeparam>
        /// <param name="selector">Projection function.</param>
        /// <returns>A provider that emits the projected outputs.</returns>
        public IncrementalValuesProvider<TResult> SelectManyEx<TResult>(Func<TSource, CancellationToken, IEnumerable<TResult>> selector)
            where TResult : ISourceGeneratorPassData, new()
            => source.SelectMany((input, ct) => TrySelectMany(input, ct, (x, y) => selector(x, y).ToImmutableArray()));

        /// <summary>
        /// Projects each input into a single output with exception handling and metric reporting.
        /// </summary>
        /// <typeparam name="TResult">Output item type.</typeparam>
        /// <param name="selector">Projection function.</param>
        /// <returns>A provider that emits the projected output.</returns>
        public IncrementalValuesProvider<TResult> SelectEx<TResult>(Func<TSource, CancellationToken, TResult> selector)
            where TResult : ISourceGeneratorPassData, new()
            => source.Select((input, ct) => TrySelect(input, ct, selector));

        /// <summary>
        /// Combines an incremental sequence with two additional providers into a tuple sequence.
        /// </summary>
        /// <typeparam name="T2">Second provider value type.</typeparam>
        /// <typeparam name="T3">Third provider value type.</typeparam>
        /// <param name="provider2">Second provider.</param>
        /// <param name="provider3">Third provider.</param>
        /// <returns>A provider that yields values from all three inputs.</returns>
        public IncrementalValuesProvider<(TSource First, T2 Second, T3 Third)> Combine<T2, T3>(IncrementalValueProvider<T2> provider2, IncrementalValueProvider<T3> provider3)
            => source
                .Combine(provider2)
                .Combine(provider3)
                .Select((tuple, token) => (tuple.Left.Left, tuple.Left.Right, tuple.Right));


    }

    extension(IncrementalGeneratorInitializationContext init)
    {
        /// <summary>
        /// Registers source generation for a single-value provider using the wrapped error-handling pipeline.
        /// </summary>
        /// <typeparam name="TInput">Input item type.</typeparam>
        /// <param name="source">Source provider to consume.</param>
        /// <param name="action">Generation callback that writes into a <see cref="SourceWriter"/>.</param>
        public void RegisterSourceOutputEx<TInput>(
            IncrementalValueProvider<TInput> source,
            Action<SourceProductionContext, SourceWriter, TInput> action
        ) where TInput : ISourceGeneratorPassData
            => init.RegisterSourceOutput(
                source,
                action: (context, input) => GenerateSource(context, input, action)
            );

        /// <summary>
        /// Registers source generation for a multi-value provider using the wrapped error-handling pipeline.
        /// </summary>
        /// <typeparam name="TInput">Input item type.</typeparam>
        /// <param name="source">Source provider to consume.</param>
        /// <param name="action">Generation callback that writes into a <see cref="SourceWriter"/>.</param>
        public void RegisterSourceOutputEx<TInput>(
            IncrementalValuesProvider<TInput> source,
            Action<SourceProductionContext, SourceWriter, TInput> action
        ) where TInput : ISourceGeneratorPassData
            => init.RegisterSourceOutput(
                source,
                action: (context, input) => GenerateSource(context, input, action)
            );

        /// <summary>
        /// Registers implementation-only source generation for a single-value provider.
        /// </summary>
        /// <typeparam name="TInput">Input item type.</typeparam>
        /// <param name="source">Source provider to consume.</param>
        /// <param name="action">Generation callback that writes into a <see cref="SourceWriter"/>.</param>
        public void RegisterImplementationSourceOutputEx<TInput>(
            IncrementalValueProvider<TInput> source,
            Action<SourceProductionContext, SourceWriter, TInput> action
        ) where TInput : ISourceGeneratorPassData
            => init.RegisterImplementationSourceOutput(
                source,
                action: (context, input) => GenerateSource(context, input, action)
            );

        /// <summary>
        /// Registers implementation-only source generation for a multi-value provider.
        /// </summary>
        /// <typeparam name="TInput">Input item type.</typeparam>
        /// <param name="source">Source provider to consume.</param>
        /// <param name="action">Generation callback that writes into a <see cref="SourceWriter"/>.</param>
        public void RegisterImplementationSourceOutputEx<TInput>(
            IncrementalValuesProvider<TInput> source,
            Action<SourceProductionContext, SourceWriter, TInput> action
        ) where TInput : ISourceGeneratorPassData
            => init.RegisterImplementationSourceOutput(
                source,
                action: (context, input) => GenerateSource(context, input, action)
            );
    }

    extension(SyntaxValueProvider provider)
    {
        /// <summary>
        /// Wraps <see cref="SyntaxValueProvider.ForAttributeWithMetadataName{T}(string, Func{SyntaxNode, CancellationToken, bool}, Func{GeneratorAttributeSyntaxContext, CancellationToken, T})"/>
        /// with exception handling and metric reporting.
        /// </summary>
        /// <typeparam name="TResult">Output item type.</typeparam>
        /// <param name="fullyQualifiedMetadataName">Attribute metadata name to match.</param>
        /// <param name="predicate">Syntax predicate used to prefilter nodes.</param>
        /// <param name="transform">Transform applied to each matching attribute context.</param>
        /// <returns>A provider that emits transformed outputs.</returns>
        public IncrementalValuesProvider<TResult> ForAttributeWithMetadataNameEx<TResult>(
            string fullyQualifiedMetadataName,
            Func<SyntaxNode, CancellationToken, bool> predicate,
            Func<GeneratorAttributeSyntaxContext, CancellationToken, TResult> transform
        ) where TResult : ISourceGeneratorPassData, new()
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
                            SourceGeneratorMetrics.Reporter?.Report(filename, Stat.TransformTimeMs, elapsed);

                        return output;
                    }
                    catch (Exception exception)
                    {
                        return ErrorResult<TResult>(exception, context.TargetNode.GetLocation());
                    }
                }
            );
    }

    static TResult TrySelect<TSource, TResult>(
        TSource input,
        CancellationToken ct,
        Func<TSource, CancellationToken, TResult> selector
    ) where TResult : ISourceGeneratorPassData, new()
    {
        try
        {
            var time = Stopwatch.StartNew();
            var output = selector(input, ct);
            float elapsed = (float)time.Elapsed.TotalMilliseconds;
            if (output is { SourceGeneratorOutputFilename: { Length: > 0 } filename })
                SourceGeneratorMetrics.Reporter?.Report(filename, Stat.TransformTimeMs, elapsed);

            return output;
        }
        catch (Exception exception)
        {
            return ErrorResult<TResult>(exception, input);
        }
    }

    static ImmutableArray<TResult> TrySelectMany<TSource, TResult>(
        TSource input,
        CancellationToken ct,
        Func<TSource, CancellationToken, ImmutableArray<TResult>> selector
    ) where TResult : ISourceGeneratorPassData, new()
    {
        try
        {
            var time = Stopwatch.StartNew();
            var output = selector(input, ct);
            float elapsed = (float)time.Elapsed.TotalMilliseconds;
            if (output.FirstOrDefault() is { SourceGeneratorOutputFilename: { Length: > 0 } filename })
                SourceGeneratorMetrics.Reporter?.Report(filename, Stat.TransformTimeMs, elapsed);

            return output;
        }
        catch (Exception exception)
        {
            return ImmutableArray.Create(ErrorResult<TResult>(exception, input));
        }
    }

    static TResult ErrorResult<TResult>(Exception exception, object? locationSource) where TResult : ISourceGeneratorPassData, new()
        => locationSource switch
        {
            ISourceGeneratorPassData { SourceGeneratorLocation: { IsInSource: true } location }
                => new() { SourceGeneratorError = $"{exception}", SourceGeneratorLocation = location },
            Location location
                => new() { SourceGeneratorError = $"{exception}", SourceGeneratorLocation = new LocationInfo(location) },
            LocationInfo { IsInSource: true } locationInfo
                => new() { SourceGeneratorError = $"{exception}", SourceGeneratorLocation = locationInfo },
            _
                => new() { SourceGeneratorError = $"{exception}" },
        };

    static void GenerateSource<TInput>(
        SourceProductionContext context,
        TInput input,
        Action<SourceProductionContext, SourceWriter, TInput> action
    ) where TInput : ISourceGeneratorPassData
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
                SourceGeneratorMetrics.Reporter?.Report(input.SourceGeneratorOutputFilename, Stat.SourceGenerationTimeMs, elapsed);
                if (writer.IsDirty)
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
            var errorLocation = input.SourceGeneratorLocation?.ToLocation();
            context.ReportDiagnostic(Diagnostic.Create(descriptor: Utility.ExceptionDiagnosticDescriptor, location: errorLocation, messageArgs: error));

            string filename = input.SourceGeneratorOutputFilename ?? Utility.GetErrorOutputFilename(input.SourceGeneratorLocation, error);
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

        if (SourceGeneratorMetrics.Reporter is { } reporter)
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


