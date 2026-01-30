using Microsoft.CodeAnalysis;
using static System.StringComparison;
using static Constants;

[Generator]
public sealed class ConstantsSourceGenerator : IIncrementalGenerator
{
    static readonly DiagnosticDescriptor MED018 = new(
        id: nameof(MED018),
        title: $"No TagManager.asset file provided.",
        messageFormat: $"TagManager.asset not found. Your project isn't configured for constants generation.",
        category: "Medicine",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    record struct GeneratorInput : IGeneratorTransformOutput
    {
        public AdditionalText? TagManager { get; init; }
        public EquatableIgnore<DiagnosticDescriptor?> DiagnosticDescriptor { get; init; }

        public string? SourceGeneratorOutputFilename { get; init; }
        public string? SourceGeneratorError { get; init; }
        public LocationInfo? SourceGeneratorErrorLocation { get; set; }
    }

    void IIncrementalGenerator.Initialize(IncrementalGeneratorInitializationContext init)
    {
        var tagManagerProvider = init.AdditionalTextsProvider
            .Where(x => x.Path.EndsWith("TagManager.asset", Ordinal))
            .Collect();

        var assemblyWithAttributeProvider = init.SyntaxProvider
            .ForAttributeWithMetadataNameEx(
                fullyQualifiedMetadataName: GenerateConstantsAttributeMetadataName,
                predicate: (x, ct) => true,
                transform: (x, ct) => new GeneratorInput
                {
                    SourceGeneratorOutputFilename = "Medicine.Constants.g.cs",
                    SourceGeneratorErrorLocation = x.Attributes.First().ApplicationSyntaxReference is { } syntaxRef
                                                   && syntaxRef.GetLocation() is { } location
                        ? new LocationInfo(location)
                        : null,
                }
            );

        var combinedProvider = assemblyWithAttributeProvider
            .Combine(tagManagerProvider)
            .SelectEx((x, ct) =>
                {
                    var input = x.Left;
                    var texts = x.Right;
                    var tagManager = texts.FirstOrDefault();

                    if (tagManager is null)
                        return input with { DiagnosticDescriptor = MED018 };

                    return input with { TagManager = tagManager };
                }
            );

        init.RegisterSourceOutputEx(
            source: combinedProvider,
            action: GenerateSource
        );
    }

    static void GenerateSource(SourceProductionContext context, SourceWriter src, GeneratorInput input)
    {
        if (input.DiagnosticDescriptor.Value is { } diagnostic)
            context.ReportDiagnostic(Diagnostic.Create(diagnostic, input.SourceGeneratorErrorLocation?.ToLocation()));

        if (input.TagManager?.GetText(context.CancellationToken)?.ToString() is not { Length: > 0 } content)
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

        src.Line.Write("using System.ComponentModel;");
        src.Line.Write("using static System.ComponentModel.EditorBrowsableState;");
        src.Line.Write(Alias.UsingInline);
        src.Linebreak();

        src.Line.Write("namespace Medicine");
        using (src.Braces)
        {
            src.Line.Write("public static partial class Constants");
            using (src.Braces)
            {
                src.Line.Write("public enum Tag : uint");
                using (src.Braces)
                    foreach (var (tag, i) in tags.Where(x => x is { Length: > 0 }).Select((x, i) => (x, i)))
                        src.Line.Write($"{tag.Sanitize('@')} = {20000 + i}u,");

                src.Linebreak();
                src.Line.Write("public enum Layer : uint");
                using (src.Braces)
                    foreach (var (layer, i) in layers.Where(x => x is { Length: > 0 }).Select((x, i) => (x, i)))
                        src.Line.Write($"{HideMaybe(layer)}{layer.Sanitize('@')} = {i:00},");

                src.Linebreak();
                src.Line.Write("[System.Flags]");
                src.Line.Write("public enum LayerMask : uint");
                using (src.Braces)
                {
                    src.Line.Write("None = 0,");
                    src.Line.Write("All = 0xffffffff,");
                    foreach (var (layer, i) in layers.Select((x, i) => (x, i)))
                        src.Line.Write($"{HideMaybe(layer)}{layer.Sanitize('@')} = 1u << {i:00},");
                }
            }

            src.Linebreak();

            src.Line.Write(Alias.Hidden);
            src.Line.Write("public static partial class ConstantsExtensions");
            using (src.Braces)
            {
                src.Line.Write(Alias.Inline);
                src.Line.Write($"public static global::UnityEngine.TagHandle GetHandle(this Constants.Tag tag)");
                using (src.Indent)
                    src.Line.Write($"=> global::Unity.Collections.LowLevel.Unsafe.UnsafeUtility.As<Constants.Tag, global::UnityEngine.TagHandle>(ref tag);");

                src.Linebreak();

                src.Line.Write(Alias.Inline);
                src.Line.Write($"public static bool CompareTag(this global::UnityEngine.GameObject gameObject, Constants.Tag tag)");
                using (src.Indent)
                    src.Line.Write($"=> gameObject.CompareTag(tag.GetHandle());");

                src.Linebreak();

                src.Line.Write(Alias.Inline);
                src.Line.Write($"public static bool CompareTag(this global::UnityEngine.Component component, Constants.Tag tag)");
                using (src.Indent)
                    src.Line.Write($"=> component.CompareTag(tag.GetHandle());");
            }
        }
    }

    static string HideMaybe(string name)
        => name is ['_', >= '0' and <= '9', >= '0' and <= '9']
            ? "[EditorBrowsable(Never)] "
            : "";
}
