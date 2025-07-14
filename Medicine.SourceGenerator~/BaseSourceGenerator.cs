using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

public interface IGeneratorTransformOutputWithContext : IGeneratorTransformOutput
{
    public EquatableIgnore<GeneratorAttributeSyntaxContext> Context { get; set; }
}

public interface IGeneratorTransformOutput
{
    string? SourceGeneratorOutputFilename { get; }
    string? SourceGeneratorError { get; set; }
    EquatableIgnore<Location> SourceGeneratorErrorLocation { get; set; }
}

public abstract class BaseSourceGenerator
{
    const int INDENT_SIZE = 4;
    int indent;

    static readonly DiagnosticDescriptor ExceptionDiagnosticDescriptor = new(
        id: "MED911",
        title: "Exception",
        messageFormat: "'{0}'",
        category: "Exception",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    protected void HandleException<TInput>(SourceProductionContext context, TInput input) where TInput : IGeneratorTransformOutput { }

    protected static Func<GeneratorAttributeSyntaxContext, CancellationToken, TOutput> WrapTransform<TOutput>(
        Func<GeneratorAttributeSyntaxContext, CancellationToken, TOutput> action,
        [CallerArgumentExpression("action")] string? cae = null,
        [CallerFilePath] string? cfp = null,
        [CallerLineNumber] int cln = 0
    )
        where TOutput : IGeneratorTransformOutput, new()
        => (context, ct) =>
        {
            try
            {
                var time = Stopwatch.StartNew();
                var output = action(context, ct);
                output.SourceGeneratorErrorLocation = context.TargetNode.GetLocation();
                float elapsed = (float)time.Elapsed.TotalMilliseconds;
                MedicineMetrics.Reporter?.Report(output.SourceGeneratorOutputFilename, Stat.TransformTimeMs, elapsed);
                return output;
            }
            catch (Exception exception)
            {
                return new() { SourceGeneratorError = $"{exception}\nThrown in: {cae}", SourceGeneratorErrorLocation = context.TargetNode.GetLocation() };
            }
        };

    protected static Func<TInput, CancellationToken, TOutput> WrapTransform<TInput, TOutput>(
        Func<GeneratorAttributeSyntaxContext, CancellationToken, TOutput> action,
        [CallerArgumentExpression("action")] string? cae = null,
        [CallerFilePath] string? cfp = null,
        [CallerLineNumber] int cln = 0
    )
        where TInput : IGeneratorTransformOutputWithContext
        where TOutput : IGeneratorTransformOutput, new()
        => (input, ct) =>
        {
            try
            {
                var time = Stopwatch.StartNew();
                var output = action(input.Context, ct);
                output.SourceGeneratorErrorLocation = input.Context.Value.TargetNode.GetLocation();
                float elapsed = (float)time.Elapsed.TotalMilliseconds;
                MedicineMetrics.Reporter?.Report(output.SourceGeneratorOutputFilename, Stat.TransformTimeMs, elapsed);
                return output;
            }
            catch (Exception exception)
            {
                return new() { SourceGeneratorError = $"{exception}\nThrown in: {cae}", SourceGeneratorErrorLocation = input.Context.Value.TargetNode.GetLocation() };
            }
        };

    protected Action<SourceProductionContext, TInput> WrapGenerateSource<TInput>(
        Action<SourceProductionContext, TInput> action,
        [CallerArgumentExpression("action")] string? cae = null,
        [CallerFilePath] string? cfp = null,
        [CallerLineNumber] int cln = 0
    )
        where TInput : IGeneratorTransformOutput
        => (context, input) =>
        {
            InitializeOutputSourceText();

            string? error = input.SourceGeneratorError;

            if (input.SourceGeneratorOutputFilename is not { Length: > 0 })
            {
                error = "The source generator did not specify an output filename. This is a bug in the source generator.";
                ;
            }
            else if (error is not { Length: > 0 })
            {
                // main path - try to generate the source
                try
                {
                    var time = Stopwatch.StartNew();

                    // invoke generator action
                    action(context, input);

                    float elapsed = (float)time.Elapsed.TotalMilliseconds;
                    MedicineMetrics.Reporter?.Report(input.SourceGeneratorOutputFilename, Stat.SourceGenerationTimeMs, elapsed);
                    AddOutputSourceTextToCompilation(input.SourceGeneratorOutputFilename, context);
                    return;
                }
                catch (Exception exception)
                {
                    error = $"{exception}\nThrown in: {cae}";
                }
            }

            // handle errors/exceptions
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        ExceptionDiagnosticDescriptor,
                        input.SourceGeneratorErrorLocation.Value,
                        error
                    )
                );

                string filename = input.SourceGeneratorOutputFilename ?? GetErrorOutputFilename(input.SourceGeneratorErrorLocation, error);
                AddOutputSourceTextToCompilation(filename, context);
            }
        };

    protected static string GetErrorOutputFilename(Location location, string error)
    {
        string filename = Path.GetFileNameWithoutExtension(location.SourceTree!.FilePath);
        var result = $"{filename}.Exception.{Hash():x8}.g.cs";
        return result;

        int Hash()
        {
            unchecked
            {
                int hash = 23;

                foreach (char c in error)
                    hash = hash * 31 + c;

                foreach (char c in filename)
                    hash = hash * 31 + c;

                return hash;
            }
        }
    }

    protected static string GetOutputFilename(string filePath, string targetFQN, string label)
    {
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
        string typename = new string(targetFQN.Select(x => char.IsLetterOrDigit(x) ? x : '_').ToArray());
        var result = $"{fileNameWithoutExtension}.{typename}.{label}.{Hash():x8}.g.cs";
        return result;

        int Hash()
        {
            unchecked
            {
                int hash = 23;

                foreach (char c in fileNameWithoutExtension)
                    hash = hash * 31 + c;

                foreach (char c in targetFQN)
                    hash = hash * 31 + c;

                return hash;
            }
        }
    }

    protected void InitializeOutputSourceText()
    {
        indent = 0;
        Text.Clear()
            .AppendLine("// <auto-generated/>")
            .AppendLine("#nullable enable");
    }

    void AddOutputSourceTextToCompilation(string filename, SourceProductionContext context)
    {
        string output = Text.AppendLine().ToString();

        if (MedicineMetrics.Reporter is { } reporter)
        {
            int lineCount = 0;
            foreach (var c in output.AsSpan())
                if (c is '\n')
                    lineCount++;

            reporter.Report(filename, Stat.LinesOfCodeGenerated, lineCount);
        }

        Text.Clear();
        indent = 0;
        context.AddSource(filename, SourceText.From(output, Encoding.UTF8));
    }

    public StringBuilder Append(string x)
        => Text.Append(x);

    public StringBuilder Append(char x)
        => Text.Append(x);

    protected void TrimEndWhitespace()
    {
        while (char.IsWhiteSpace(Text[^1]))
            Text.Remove(Text.Length - 1, 1);
    }

    protected void Linebreak()
    {
        if (Text.Length is var n and >= 1)
            if (Text[n - 1] != '\n')
                Text.AppendLine();
    }

    public StringBuilder Text { get; } = new(capacity: 16384);

    public StringBuilder Line
        => Text.AppendLine().Append(' ', indent);

    protected static bool MatchFilePath(ReadOnlySpan<char> filePath, ReadOnlySpan<char> subString1, ReadOnlySpan<char> substring2)
        => filePath.Contains(subString1, StringComparison.Ordinal) ||
           filePath.Contains(substring2, StringComparison.Ordinal);

    protected void OpenBrace()
    {
        Line.Append('{');
        IncreaseIndent();
    }

    protected void CloseBrace()
    {
        DecreaseIndent();
        TrimEndWhitespace();
        Line.Append('}');
    }

    public void IncreaseIndent()
        => indent += INDENT_SIZE;

    public void DecreaseIndent()
        => indent -= INDENT_SIZE;

    protected IndentScope Indent
        => new(this);

    protected BracesScope Braces
        => new(this);

    protected readonly struct IndentScope : IDisposable
    {
        readonly BaseSourceGenerator generator;

        public IndentScope(BaseSourceGenerator generator)
            => (this.generator = generator).IncreaseIndent();

        public void Dispose()
            => generator.DecreaseIndent();
    }

    protected readonly struct BracesScope : IDisposable
    {
        readonly BaseSourceGenerator generator;

        public BracesScope(BaseSourceGenerator generator)
            => (this.generator = generator).OpenBrace();

        public void Dispose()
            => generator.CloseBrace();
    }
}