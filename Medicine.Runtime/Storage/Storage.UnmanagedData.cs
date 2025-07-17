using System.ComponentModel;
using Unity.Collections;
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
                }
#else
                instance.Initialize(out var state);
#endif
                List.Add(state);
            }

            public static void Unregister(T instance, int elementIndex)
            {
#if DEBUG
                try
                {
                    instance.Cleanup(ref List.ElementAt(elementIndex));
                }
                catch (System.Exception ex)
                {
                    Debug.LogError(
                        $"Failed to invoke {typeof(T).Name}.Initialize(out {typeof(TData).Name} state). " +
                        $"Please make sure this method never throws. " +
                        $"This will cause tracking logic errors in release builds."
                    );
                    return;
                }
#else
                instance.Cleanup(ref List.ElementAt(elementIndex));
#endif
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