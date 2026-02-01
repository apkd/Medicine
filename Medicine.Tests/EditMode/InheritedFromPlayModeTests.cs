// using inheritance to surface some play mode tests in the edit mode.
// these don't depend on play mode, and running edit mode tests is faster.
// this way we can have "edit mode tests that also run in player tests", covering more scenarios.

namespace InheritedFromPlayMode
{
    public sealed class UnionTests : global::UnionTests { }

    public sealed class LazyTests : global::LazyTests { }

    public sealed class AsSpanUnsafeTests : global::AsSpanUnsafeTests { }

    public sealed class MedicineExtensionsTests : global::MedicineExtensionsTests { }

    public sealed class UnmanagedAccessTests : global::UnmanagedAccessTests { }

    public sealed class PooledListTests : global::PooledListTests { }

    public sealed class FindByTypeTests : global::FindByTypeTests { }

#if MODULE_ZLINQ
    public sealed class WrapValueEnumerableCompileTests : global::WrapValueEnumerableCompileTests { }
#endif
}