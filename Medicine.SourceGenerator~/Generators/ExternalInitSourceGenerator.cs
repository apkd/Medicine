using Microsoft.CodeAnalysis;

[Generator]
public sealed class IsExternalInitGenerator : IIncrementalGenerator
{
    const string isExternalInitSourceCode
        = """
          // <auto-generated/>
          using System.ComponentModel;
          namespace System.Runtime.CompilerServices
          {
              /// <summary>
              /// Reserved to be used by the compiler for tracking metadata.
              /// This class should not be used by developers in source code.
              /// </summary>
              /// <remarks> Enables the use of the `init` and `record` keywords. </remarks>
              /// <seealso href="https://github.com/dotnet/roslyn/issues/45510#issuecomment-694977239"/>
              [EditorBrowsable(EditorBrowsableState.Never)]
              static class IsExternalInit { }
          }
          """;

    const string isExternalInitFQN = "System.Runtime.CompilerServices.IsExternalInit";

    void IIncrementalGenerator.Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterSourceOutput(
            source: context
                .CompilationProvider
                .Select((compilation, _) => compilation.Assembly.GetTypeByMetadataName(isExternalInitFQN) != null),
            action: (sourceContext, isExternalInitDefined) =>
            {
                if (isExternalInitDefined)
                    return;

                sourceContext.AddSource("IsExternalInit.g.cs", isExternalInitSourceCode);
            }
        );
    }
}