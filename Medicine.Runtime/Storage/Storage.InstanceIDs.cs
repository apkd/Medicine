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
            public static NativeList<int> List = Initialize();

            static NativeList<int> Initialize()
            {
#if UNITY_2023_1_OR_NEWER
                List = new(initialCapacity: 8, Allocator.Domain);
#else
                List = new(initialCapacity: 8, Allocator.Persistent);
#if UNITY_EDITOR
                beforeAssemblyUnload += static () =>
                {
                    var safetyHandle = Unity.Collections.LowLevel.Unsafe.NativeListUnsafeUtility.GetAtomicSafetyHandle(ref List);
                    Unity.Collections.LowLevel.Unsafe.AtomicSafetyHandle.EnforceAllBufferJobsHaveCompleted(safetyHandle);
                    List.Dispose();
                };
#endif
#endif
                return List;
            }
        }
    }
}