using System.ComponentModel;
using UnityEngine;
using UnityEngine.Jobs;
using static System.ComponentModel.EditorBrowsableState;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedTypeParameter

namespace Medicine.Internal
{
    public static partial class Storage
    {
        [EditorBrowsable(Never)]
        public static class TransformAccess<T> where T : class
        {
            public static TransformAccessArray Transforms;

            // ReSharper disable once UnusedMethodReturnValue.Global
            public static int Initialize(int initialCapacity, int desiredJobCount)
            {
                if (Transforms.isCreated)
                    return 0;

                Transforms = new(initialCapacity, desiredJobCount);
#if UNITY_EDITOR
                beforeAssemblyUnload += static () => Transforms.Dispose();
#endif
                return 0;
            }

            public static void Register(Transform transform)
            {
                if (!Transforms.isCreated)
                {
                    // initialize with default settings when no statically-provided capacity
                    // and job count values were provided
                    Initialize(64, -1);
                }

                Transforms.Add(transform);
            }

            public static void Unregister(int transformIndex)
            {
#if DEBUG
                if (!Transforms.isCreated)
                    return; // possible right before domain reload
#endif
                if (transformIndex >= 0)
                    Transforms.RemoveAtSwapBack(transformIndex);
            }
        }
    }
}
