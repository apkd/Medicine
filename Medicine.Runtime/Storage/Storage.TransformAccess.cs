using System.ComponentModel;
using UnityEngine;
using UnityEngine.Jobs;
using static System.ComponentModel.EditorBrowsableState;
using Component = UnityEngine.Component;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedTypeParameter

namespace Medicine.Internal
{
    public static partial class Storage
    {
        [EditorBrowsable(Never)]
        public static class TransformAccess<T> where T : Component
        {
            public static TransformAccessArray Transforms;

            public static void Initialize(int initialCapacity, int desiredJobCount)
            {
                if (Transforms.isCreated)
                    return;

                Transforms = new(initialCapacity, desiredJobCount);
#if UNITY_EDITOR
                beforeAssemblyUnload += static () => Transforms.Dispose();
#endif
            }

            public static void Register(Transform transform)
                => Transforms.Add(transform);

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