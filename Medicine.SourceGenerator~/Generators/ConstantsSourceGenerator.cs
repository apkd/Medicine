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

    record struct GeneratorInput : ISourceGeneratorPassData
    {
        public AdditionalText? TagManager { get; init; }
        public EquatableIgnore<DiagnosticDescriptor?> DiagnosticDescriptor { get; init; }
        public GeneratorEnvironment GeneratorEnvironment { get; init; }
        public string Namespace { get; init; }
        public string ClassName { get; init; }
        public string ExtensionsClassName { get; init; }

        public string? SourceGeneratorOutputFilename { get; init; }
        public string? SourceGeneratorError { get; init; }
        public LocationInfo? SourceGeneratorLocation { get; set; }
    }

    void IIncrementalGenerator.Initialize(IncrementalGeneratorInitializationContext init)
    {
        var generatorEnvironment = init.GetGeneratorEnvironment();

        var tagManagerProvider = init.AdditionalTextsProvider
            .Where(x => x.Path.EndsWith("TagManager.asset", Ordinal))
            .Collect();

        var assemblyWithAttributeProvider = init.SyntaxProvider
            .ForAttributeWithMetadataNameEx(
                fullyQualifiedMetadataName: GenerateConstantsAttributeMetadataName,
                predicate: (x, ct) => true,
                transform: (x, ct) =>
                    {
                        var attribute = x.Attributes.First();
                        var constructorArgs = attribute.GetAttributeConstructorArguments(ct);
                        var namespaceName = constructorArgs.Get("namespace", "Medicine") ?? "Medicine";
                        var className = constructorArgs.Get("class", "Constants") ?? "Constants";

                        return new GeneratorInput
                        {
                            SourceGeneratorOutputFilename = "Medicine.Constants.g.cs",
                            SourceGeneratorLocation = attribute.ApplicationSyntaxReference?.GetLocation(),
                            Namespace = namespaceName,
                            ClassName = className,
                            ExtensionsClassName = $"{className}Extensions",
                        };
                    }
            );

        var combinedProvider = assemblyWithAttributeProvider
            .Combine(tagManagerProvider, generatorEnvironment)
            .SelectEx((x, ct) =>
                {
                    var (input, texts, environment) = x;
                    var tagManager = texts.FirstOrDefault();

                    if (tagManager is null)
                        return input with { DiagnosticDescriptor = MED018 };

                    return input with { TagManager = tagManager, GeneratorEnvironment = environment };
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
            context.ReportDiagnostic(Diagnostic.Create(diagnostic, input.SourceGeneratorLocation?.ToLocation()));

        if (input.TagManager?.GetText(context.CancellationToken)?.ToString() is not { Length: > 0 } content)
            return;

        src.ShouldEmitDocs = input.GeneratorEnvironment.ShouldEmitDocs;

        using var r1 = Scratch.RentA<List<string>>(out var tags);
        using var r2 = Scratch.RentB<List<string>>(out var layers);

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

        void EmitBody()
        {
            src.Doc?.Write("/// <summary>");
            src.Doc?.Write("/// Contains generated tag and layer constants based on <c>ProjectSettings/TagManager.asset</c>.");
            src.Doc?.Write("/// </summary>");

            src.Line.Write($"public static partial class {input.ClassName}");
            using (src.Braces)
            {
                src.Doc?.Write("/// <summary>");
                src.Doc?.Write("/// Enumerates Unity tag names as constants.");
                src.Doc?.Write("/// This enum is generated based on the contents of <c>ProjectSettings/TagManager.asset</c>.");
                src.Doc?.Write("/// </summary>");

                src.Line.Write("public enum Tag : uint");
                using (src.Braces)
                    foreach (var (tag, i) in tags.Where(x => x is { Length: > 0 }).Select((x, i) => (x, i)))
                        src.Line.Write($"{tag.Sanitize('@')} = {20000 + i}u,");

                src.Linebreak();

                src.Doc?.Write("/// <summary>");
                src.Doc?.Write("/// Enumerates Unity layer indices as constants.");
                src.Doc?.Write("/// This enum is generated based on the contents of <c>ProjectSettings/TagManager.asset</c>.");
                src.Doc?.Write("/// </summary>");

                src.Line.Write("public enum Layer : uint");
                using (src.Braces)
                    foreach (var (layer, i) in layers.Where(x => x is { Length: > 0 }).Select((x, i) => (x, i)))
                        src.Line.Write($"{HideMaybe(layer)}{layer.Sanitize('@')} = {i:00},");

                src.Linebreak();

                src.Doc?.Write("/// <summary>");
                src.Doc?.Write("/// Enumerates Unity layer bitmasks as constants.");
                src.Doc?.Write("/// This enum is generated based on the contents of <c>ProjectSettings/TagManager.asset</c>.");
                src.Doc?.Write("/// </summary>");
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

            src.Doc?.Write("/// <summary>");
            src.Doc?.Write("/// Provides extension helpers for generated constants.");
            src.Doc?.Write("/// </summary>");
            src.Line.Write(Alias.Hidden);

            src.Line.Write($"public static partial class {input.ExtensionsClassName}");
            using (src.Braces)
            {
                src.Doc?.Write("/// <summary>");
                src.Doc?.Write($"/// Converts a generated <see cref=\"{input.ClassName}.Tag\"/> value to a Unity <see cref=\"UnityEngine.TagHandle\"/>.");
                src.Doc?.Write("/// </summary>");
                src.Line.Write(Alias.Inline);
                src.Line.Write($"public static global::UnityEngine.TagHandle GetHandle(this {input.ClassName}.Tag tag)");
                using (src.Indent)
                    src.Line.Write($"=> global::Unity.Collections.LowLevel.Unsafe.UnsafeUtility.As<{input.ClassName}.Tag, global::UnityEngine.TagHandle>(ref tag);");

                src.Linebreak();

                src.Doc?.Write("/// <inheritdoc cref=\"UnityEngine.GameObject.CompareTag(UnityEngine.TagHandle)\"/>");
                src.Line.Write(Alias.Inline);
                src.Line.Write($"public static bool CompareTag(this global::UnityEngine.GameObject gameObject, {input.ClassName}.Tag tag)");
                using (src.Indent)
                    src.Line.Write($"=> gameObject.CompareTag(tag.GetHandle());");

                src.Linebreak();

                src.Doc?.Write("/// <inheritdoc cref=\"UnityEngine.Component.CompareTag(UnityEngine.TagHandle)\"/>");
                src.Line.Write(Alias.Inline);
                src.Line.Write($"public static bool CompareTag(this global::UnityEngine.Component component, {input.ClassName}.Tag tag)");
                using (src.Indent)
                    src.Line.Write($"=> component.CompareTag(tag.GetHandle());");
            }
        }

        if (input.Namespace is { Length: > 0 })
        {
            src.Line.Write($"namespace {input.Namespace}");
            using (src.Braces)
                EmitBody();
        }
        else
            EmitBody();
    }

    static string HideMaybe(string name)
        => name is ['_', >= '0' and <= '9', >= '0' and <= '9']
            ? "[EditorBrowsable(Never)] "
            : "";
}
