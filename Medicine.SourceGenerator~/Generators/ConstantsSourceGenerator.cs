using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using static System.StringComparison;
using static Constants;

[Generator]
public sealed class ConstantsSourceGenerator : BaseSourceGenerator, IIncrementalGenerator
{
    record struct GeneratorInput : IGeneratorTransformOutput
    {
        public AdditionalText TagManager { get; init; }
        public string? SourceGeneratorOutputFilename { get; init; }
        public string? SourceGeneratorError { get; init; }
        public EquatableIgnore<Location> SourceGeneratorErrorLocation { get; set; }
    }

    void IIncrementalGenerator.Initialize(IncrementalGeneratorInitializationContext init)
    {
        var assemblyAttributeProvider = init.CompilationProvider
            .Select((x, ct) => x.Assembly.HasAttribute(GenerateConstantsAttributeFQN));

        var tagManagerProvider = init.AdditionalTextsProvider
            .Where(x => x.Path.EndsWith("TagManager.asset", Ordinal));

        init.RegisterSourceOutput(
            source: tagManagerProvider.Combine(assemblyAttributeProvider),
            action: (context, source) =>
            {
                var (tagManagerAsset, hasAttribute) = source;

                if (!hasAttribute)
                    return;

                var input = new GeneratorInput
                {
                    TagManager = tagManagerAsset,
                    SourceGeneratorOutputFilename = "Medicine.Constants.g.cs",
                };

                WrapGenerateSource<GeneratorInput>(GenerateSource).Invoke(context, input);
            }
        );
    }

    void GenerateSource(SourceProductionContext context, GeneratorInput input)
    {
        if (input.TagManager.GetText(context.CancellationToken)?.ToString() is not { Length: > 0 } content)
            return;

        var tags = new List<string>(capacity: 16);
        var layers = new List<string>(capacity: 32);

        // parse TagManager.asset
        {
            List<string>? current = null;

            var contentSpan = content.AsSpan();
            var tagsSpan = "tags:".AsSpan();
            var layersSpan = "layers:".AsSpan();
            var dashSpan = "-".AsSpan();

            while (!contentSpan.IsEmpty)
            {
                int nextEOL = contentSpan.IndexOf('\n');
                ReadOnlySpan<char> line;

                if (nextEOL == -1)
                {
                    line = contentSpan;
                    contentSpan = ReadOnlySpan<char>.Empty;
                }
                else
                {
                    line = contentSpan[..nextEOL];
                    contentSpan = contentSpan[(nextEOL + 1)..];
                }

                line = line.Trim();

                if (line.StartsWith(tagsSpan, Ordinal))
                    current = tags;
                else if (line.StartsWith(layersSpan, Ordinal))
                    current = layers;
                else if (line.StartsWith(dashSpan, Ordinal))
                    current?.Add(line[1..].Trim().ToString() is { Length: > 0 } key ? key : $"_{current.Count:00}");
                else if (!line.IsEmpty)
                    current = null;
            }
        }


        Line.Write("using System.ComponentModel;");
        Line.Write("using static System.ComponentModel.EditorBrowsableState;");
        Line.Write(Alias.UsingInline);
        Linebreak();

        Line.Write("namespace Medicine");
        using (Braces)
        {
            Line.Write("public static partial class Constants");
            using (Braces)
            {
                Line.Write("public enum Tag : uint");
                using (Braces)
                    foreach (var (tag, i) in tags.Where(x => x is { Length: > 0 }).Select((x, i) => (x, i)))
                        Line.Write($"{Sanitize(tag)} = {20000 + i}u,");

                Linebreak();
                Line.Write("public enum Layer : uint");
                using (Braces)
                    foreach (var (layer, i) in layers.Where(x => x is { Length: > 0 }).Select((x, i) => (x, i)))
                        Line.Write($"{HideMaybe(layer)}{Sanitize(layer)} = {i:00},");

                Linebreak();
                Line.Write("[System.Flags]");
                Line.Write("public enum LayerMask : uint");
                using (Braces)
                {
                    Line.Write("None = 0,");
                    Line.Write("All = 0xffffffff,");
                    foreach (var (layer, i) in layers.Select((x, i) => (x, i)))
                        Line.Write($"{HideMaybe(layer)}{Sanitize(layer)} = 1u << {i:00},");
                }
            }

            Linebreak();

            Line.Write(Alias.Hidden);
            Line.Write("public static partial class ConstantsExtensions");
            using (Braces)
            {
                Line.Write(Alias.Inline);
                Line.Write($"public static global::UnityEngine.TagHandle GetHandle(this Constants.Tag tag)");
                using (Indent)
                    Line.Write($"=> global::Unity.Collections.LowLevel.Unsafe.UnsafeUtility.As<Constants.Tag, global::UnityEngine.TagHandle>(ref tag);");

                Linebreak();

                Line.Write(Alias.Inline);
                Line.Write($"public static bool CompareTag(this global::UnityEngine.GameObject gameObject, Constants.Tag tag)");
                using (Indent)
                    Line.Write($"=> gameObject.CompareTag(tag.GetHandle());");

                Linebreak();

                Line.Write(Alias.Inline);
                Line.Write($"public static bool CompareTag(this global::UnityEngine.Component component, Constants.Tag tag)");
                using (Indent)
                    Line.Write($"=> component.CompareTag(tag.GetHandle());");
            }
        }
    }

    static string HideMaybe(string name)
    {
        if (name.Length is 3)
            if (name[0] is '_')
                if (char.IsDigit(name[1]))
                    if (char.IsDigit(name[2]))
                        return "[EditorBrowsable(Never)] ";
        return "";
    }

    static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "???";

        Span<char> span = stackalloc char[name.Length + 1];
        int i = 0;

        // prepend @
        {
            span[i++] = '@';
        }

        // first char
        {
            char c = name[0];
            if (char.IsLetter(c) || c == '_')
                span[i++] = c;
            else
                span[i++] = '_';
        }

        // remaining chars
        foreach (var c in name.AsSpan()[1..])
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                span[i++] = c;
            else
                span[i++] = '_';
        }

        return span.ToString();
    }
}