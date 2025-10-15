using System.ComponentModel;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using static System.ComponentModel.EditorBrowsableState;
using static System.Runtime.CompilerServices.MethodImplOptions;

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
            public static NativeList<TData> List = Initialize();
            public static NativeArray<TData> Array;

            [MethodImpl(AggressiveInlining)]
            public static unsafe ref TData ElementAtRefRW(int index)
                => ref UnsafeUtility.ArrayElementAsRef<TData>(Array.GetUnsafePtr(), index);

            /// <summary>
            /// This method is used to statically initialize the static fields on first access.
            /// </summary>
            static NativeList<TData> Initialize()
            {
#if UNITY_2023_1_OR_NEWER
                List = new(initialCapacity: 8, Allocator.Domain);
#else
                List = new(initialCapacity: 8, Allocator.Persistent);
#if UNITY_EDITOR
                beforeAssemblyUnload += static () =>
                {
                    var safetyHandle = NativeListUnsafeUtility.GetAtomicSafetyHandle(ref List);
                    AtomicSafetyHandle.EnforceAllBufferJobsHaveCompleted(safetyHandle);
                    List.Dispose();
                    Array = default;
                };
#endif
#endif
                Array = List.AsArray();
                return List;
            }

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
                instance.Initialize(out state);
#endif
                List.Add(state);
                Array = List.AsArray();
            }

            public static void Unregister(T instance, int elementIndex)
            {
                if (!Utility.IsNativeObjectAlive(instance as Object))
                    return;

                if (elementIndex < 0)
                    return;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                var safety = NativeListUnsafeUtility.GetAtomicSafetyHandle(ref List);
                AtomicSafetyHandle.EnforceAllBufferJobsHaveCompleted(safety);
#endif

#if DEBUG
                try
                {
                    if (!List.IsCreated)
                        return; // possible right before domain reload

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
                List.RemoveAtSwapBack(elementIndex);

                Array = List.AsArray();
            }
        }
    }
}