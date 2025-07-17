using System.ComponentModel;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
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

            // initialize the class on first access
            // ReSharper disable once UnusedMember.Local
            static readonly int initToken = Initialize();

            public static void Register(T instance)
            {
                TData state;
#if DEBUG
                try
                {
                    instance.Initialize(out state);
                }
                catch (System.Exception ex)
                {
                    state = default;
                    Debug.LogError(
                        $"Failed to invoke {typeof(T).Name}.Initialize(out {typeof(TData).Name} state). " +
                        $"Please make sure this method never throws. " +
                        $"This will cause tracking logic errors in release builds."
                    );
                    Debug.LogException(ex);
                }
#else
                instance.Initialize(out var state);
#endif
                List.Add(state);
            }

            public static void Unregister(T instance, int elementIndex)
            {
                if (!Utility.IsNativeObjectAlive(instance as UnityEngine.Object))
                    return;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                var safety = NativeListUnsafeUtility.GetAtomicSafetyHandle(ref List);
                AtomicSafetyHandle.EnforceAllBufferJobsHaveCompleted(safety);
#endif

#if DEBUG
                try
                {
                    instance.Cleanup(ref List.ElementAt(elementIndex));
                }
                catch (System.Exception ex)
                {
                    Debug.LogError(
                        $"Failed to invoke {typeof(T).Name}.Cleanup(ref {typeof(TData).Name} state). " +
                        $"Please make sure this method never throws. " +
                        $"This will cause tracking logic errors in release builds."
                    );
                    Debug.LogException(ex);
                    return;
                }
#else
                instance.Cleanup(ref List.ElementAt(elementIndex));
#endif
                if (elementIndex >= 0)
                    List.RemoveAtSwapBack(elementIndex);
            }

            public static int Initialize()
            {
                if (List.IsCreated)
                    return 0;

#if UNITY_2023_1_OR_NEWER
                List = new(initialCapacity: 8, Allocator.Domain);
#else
                List = new(initialCapacity: 8, Allocator.Persistent);
#if UNITY_EDITOR
                beforeAssemblyUnload += static () => List.Dispose();
#endif
#endif
                return 0;
            }
        }
    }
}