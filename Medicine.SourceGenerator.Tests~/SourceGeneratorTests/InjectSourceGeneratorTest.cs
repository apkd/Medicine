static class InjectSourceGeneratorTest
{
    public static readonly DiagnosticTest Case =
        new("Inject generator adds source when [Inject] is used", Run);

    static void Run()
        => SourceGeneratorTester.AssertGeneratesSource(
            source: """
using Medicine;
using UnityEngine;

sealed partial class InjectGenerationContractComponent : MonoBehaviour
{
    [Inject]
    void Awake()
        => CachedTransform = GetComponent<Transform>();
}
""",
            generator: new InjectSourceGenerator()
        );
}
