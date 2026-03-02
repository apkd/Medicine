using Microsoft.CodeAnalysis;

record struct GeneratorAttributeContextInput : ISourceGeneratorPassData
{
    public string? SourceGeneratorOutputFilename { get; init; }
    public string? SourceGeneratorError { get; init; }
    public LocationInfo? SourceGeneratorLocation { get; set; }
    public GeneratorAttributeSyntaxContext Context { get; init; }

    public GeneratorAttributeContextInput(GeneratorAttributeSyntaxContext context, Func<GeneratorAttributeSyntaxContext, string?> outputFilenameFunc)
    {
        Context = context;
        SourceGeneratorLocation = context.TargetNode.GetLocation();
        SourceGeneratorOutputFilename = outputFilenameFunc(context);
    }

    bool IEquatable<GeneratorAttributeContextInput>.Equals(GeneratorAttributeContextInput other)
        => false;
}
