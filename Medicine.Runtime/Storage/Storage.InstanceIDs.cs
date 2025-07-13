using System.ComponentModel;
using Unity.Collections;
using static System.ComponentModel.EditorBrowsableState;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedTypeParameter

namespace Medicine.Internal
{
    public static partial class Storage
    {
        [EditorBrowsable(Never)]
        public static class InstanceIDs<T> where T : class
        {
            public static NativeList<int> List;

            public static void Initialize()
            {
                if (List.IsCreated)
                    return;

#if UNITY_2023_1_OR_NEWER
                List = new(initialCapacity: 8, Allocator.Domain);
#else
                List = new(initialCapacity: 8, Allocator.Persistent);
#if UNITY_EDITOR
                beforeAssemblyUnload += static () => List.Dispose();
#endif
#endif
            }
        }
    }
}