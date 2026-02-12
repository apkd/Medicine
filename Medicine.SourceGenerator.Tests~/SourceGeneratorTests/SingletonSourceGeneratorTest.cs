static class SingletonSourceGeneratorTest
{
    public static readonly DiagnosticTest Case =
        new("Track generator adds more source when [Singleton] is used", Run);

    static void Run()
        => SourceGeneratorTester.AssertGeneratesMoreSourcesThanBaseline(
            baselineSource: """
using UnityEngine;

sealed partial class SingletonGenerationContractComponent : MonoBehaviour { }
""",
            attributedSource: """
using Medicine;
using UnityEngine;

[Singleton]
sealed partial class SingletonGenerationContractComponent : MonoBehaviour { }
""",
            generator: new TrackSourceGenerator()
        );
}
