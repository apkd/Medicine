#if !MODULE_ZLINQ // piggybacking on ZLinq to detect if System.Runtime.CompilerServices.Unsafe is available
#nullable enable
using System.Runtime.CompilerServices;
using static System.Runtime.CompilerServices.MethodImplOptions;

namespace Medicine.Internal
{
    static class MedicineUnsafeShim
    {
        [MethodImpl(AggressiveInlining)]
        internal static void SkipInit<T>(out T value)
            => value = default!; // what can you do?...
    }
}
#endif