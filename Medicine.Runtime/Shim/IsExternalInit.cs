// unity 2021 really dislikes not having this class explicitly defined,
// even though we have a source generator to patch it in...

#if !UNITY_2022_1_OR_NEWER
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
#endif