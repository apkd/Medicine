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
        public static class UnmanagedData<T, TData>
            where T : class, IUnmanagedData<TData>
            where TData : unmanaged
        {
            public static NativeList<TData> List;

            public static void Register(T instance)
            {
                instance.Initialize(out var state);
                List.Add(state);
            }

            public static void Unregister(int elementIndex)
            {
                if (elementIndex >= 0)
                    List.RemoveAtSwapBack(elementIndex);
            }

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