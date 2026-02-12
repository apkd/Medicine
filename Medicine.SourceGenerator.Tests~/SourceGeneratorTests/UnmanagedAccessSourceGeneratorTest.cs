static class UnmanagedAccessSourceGeneratorTest
{
    public static readonly DiagnosticTest Case =
        new("UnmanagedAccess generator adds more source when [UnmanagedAccess] is used", Run);

    static void Run()
        => SourceGeneratorTester.AssertGeneratesMoreSourcesThanBaseline(
            baselineSource: """
using UnityEngine;

sealed partial class UnmanagedAccessGenerationContractComponent : MonoBehaviour
{
    int counter;
}
""",
            attributedSource: """
using Medicine;
using UnityEngine;

[UnmanagedAccess]
sealed partial class UnmanagedAccessGenerationContractComponent : MonoBehaviour
{
    int counter;
}
""",
            generator: new UnmanagedAccessSourceGenerator()
        );
}
