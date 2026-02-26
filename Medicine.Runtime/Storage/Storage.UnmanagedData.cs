using System.ComponentModel;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using static System.ComponentModel.EditorBrowsableState;
using static System.Runtime.CompilerServices.MethodImplOptions;
using static UnityEngine.Debug;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedTypeParameter

namespace Medicine.Internal
{
    public static partial class Storage
    {
        [EditorBrowsable(Never)]
        public static class UnmanagedData<T, TData>
            where T : class
            where TData : unmanaged
        {
            public static NativeList<TData> List = Initialize();
            public static NativeArray<TData> Array;

            [MethodImpl(AggressiveInlining)]
            public static unsafe ref TData ElementAtRefRW(int index)
            {
                var ptr = NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(Array);
                return ref UnsafeUtility.ArrayElementAsRef<TData>(ptr, index);
            }

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
                try
                {
                    ((IUnmanagedData<TData>)instance).Initialize(out state);
                }
                catch (System.Exception ex)
                {
                    state = default;
                    LogException(ex);
                }
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
                var enforceJobResult = AtomicSafetyHandle.EnforceAllBufferJobsHaveCompleted(safety);
                if (enforceJobResult is EnforceJobResult.DidSyncRunningJobs)
                {
                    LogWarning(
                        $"Job completion was enforced while unregistering an instance of {typeof(T).Name}. " +
                        $"This is an editor-only check. Disabling/enabling tracked instances while jobs are running " +
                        $"may cause race condition bugs in release builds."
                    );
                }
#endif

                try
                {
#if UNITY_EDITOR
                    if (!List.IsCreated)
                        return; // possible right before domain reload
#endif

                    ((IUnmanagedData<TData>)instance).Cleanup(ref List.ElementAt(elementIndex));
                }
                catch (System.Exception ex)
                {
                    LogException(ex);
                    return;
                }
                List.RemoveAtSwapBack(elementIndex);
                Array = List.AsArray();
            }
        }
    }
}