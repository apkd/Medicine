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
    EquatableIgnoreList<string>? SourceGeneratorDiagnostics { get; set; }
    string? SourceGeneratorError { get; set; }
}

public abstract class BaseSourceGenerator
{
    const int INDENT_SIZE = 4;
    int indent;
    static readonly ConcurrentDictionary<(string, string, int), int> invocationCounter = new();

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
                int count = invocationCounter.AddOrUpdate((output.SourceGeneratorOutputFilename!, cfp!, cln), 1, (k, v) => ++v);
#if DEBUG
                output.SourceGeneratorDiagnostics ??= [];
                output.SourceGeneratorDiagnostics.Add($"// Transform [{count}]: {time.Elapsed.TotalMilliseconds:0.00}ms");
#endif
                return output;
            }
            catch (Exception exception)
            {
                return new() { SourceGeneratorError = $"{exception}\nThrown in: {cae}" };
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
                int count = invocationCounter.AddOrUpdate((output.SourceGeneratorOutputFilename!, cfp!, cln), 1, (k, v) => ++v);
#if DEBUG
                output.SourceGeneratorDiagnostics ??= [];
                output.SourceGeneratorDiagnostics.Add($"// Transform [{count}]: {time.Elapsed.TotalMilliseconds:0.00}ms");
#endif
                return output;
            }
            catch (Exception exception)
            {
                return new() { SourceGeneratorError = $"{exception}\nThrown in: {cae}" };
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
            if (input.SourceGeneratorError is not { Length: > 0 } error)
            {
                if (input.SourceGeneratorOutputFilename is not { Length: > 0 })
                    return;

                try
                {
                    var time = Stopwatch.StartNew();
                    action(context, input);
                    int count = invocationCounter.AddOrUpdate((input.SourceGeneratorOutputFilename!, cfp!, cln), 1, (k, v) => ++v);
#if DEBUG
                    input.SourceGeneratorDiagnostics ??= [];
                    input.SourceGeneratorDiagnostics.Add($"// {cae!}[{count}]: {time.Elapsed.TotalMilliseconds:0.00}ms");

                    Line.Append("// Diagnostics:");

                    foreach (var line in input.SourceGeneratorDiagnostics)
                        Line.Append(line);

                    Line.Append($"// HashCode: {input.GetHashCode()}");
#endif

                    context.AddSource(input.SourceGeneratorOutputFilename!, FinalizeOutputSourceText());
                    return;
                }
                catch (Exception exception)
                {
                    error = $"{exception}\nThrown in: {cae}";
                }
            }

#if DEBUG
            foreach (var line in error.Split('\n', '\r').Where(x => x is { Length: > 0 }))
                Line.Append("#error ").Append(line);
#else
            _ = error;
#endif

            context.AddSource(input.SourceGeneratorOutputFilename ?? "Exception.g.cs", FinalizeOutputSourceText());
        };

    protected static string GetOutputFilename(string filePath, string targetFQN, string label)
    {
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
        string typename = new string(targetFQN.Select(x => char.IsLetterOrDigit(x) ? x : '_').ToArray());
        var result = $"{fileNameWithoutExtension}.{typename}.{label}.{Hash():x8}.g.cs";
        Debug.WriteLine(result);
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

    protected SourceText FinalizeOutputSourceText()
    {
        string output = Text.AppendLine().ToString();
        Text.Clear();
        indent = 0;
        return SourceText.From(output, Encoding.UTF8);
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