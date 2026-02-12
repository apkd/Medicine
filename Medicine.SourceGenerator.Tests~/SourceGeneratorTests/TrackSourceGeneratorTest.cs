static class TrackSourceGeneratorTest
{
    public static readonly DiagnosticTest Case =
        new("Track generator adds more source when [Track] is used", Run);

    static void Run()
        => SourceGeneratorTester.AssertGeneratesMoreSourcesThanBaseline(
            baselineSource: """
using UnityEngine;

sealed partial class TrackGenerationContractComponent : MonoBehaviour { }
""",
            attributedSource: """
using Medicine;
using UnityEngine;

[Track]
sealed partial class TrackGenerationContractComponent : MonoBehaviour { }
""",
            generator: new TrackSourceGenerator()
        );
}
