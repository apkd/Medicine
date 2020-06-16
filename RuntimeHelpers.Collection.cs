using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using static System.Runtime.CompilerServices.MethodImplOptions;

// ReSharper disable StaticMemberInGenericType
namespace Medicine
{
    public static partial class RuntimeHelpers
    {
        /// <summary>
        /// Helper methods related to the [Inject.All] implementation.
        /// </summary>
        public static class Collection<TRegistered> where TRegistered : class
        {
            const int InitialCapacity = 32;

            static TRegistered[] instances = new TRegistered[InitialCapacity];
            static int capacity = InitialCapacity;
            static int count = 0;

            /// <summary>
            /// Get an array of active registered instances of type <see cref="TRegistered"/>.
            /// </summary>
            [MethodImpl(AggressiveInlining)]
            public static TRegistered[] GetInstances()
            {
                if (!ApplicationIsPlaying)
                    return ErrorEditMode();

                NonAlloc.Unsafe.OverwriteArrayLength(instances, count);
                return instances;
            }

            /// <summary>
            /// Add an instance of <see cref="TRegistered"/> to the registered instances.
            /// </summary>
            [MethodImpl(AggressiveInlining)]
            public static void RegisterInstance(TRegistered instance)
            {
                if (count == capacity)
                    Array.Resize(ref instances, capacity *= 2);
                else
                    NonAlloc.Unsafe.OverwriteArrayLength(instances, capacity);

                instances[count++] = instance;
            }


            /// <summary>
            /// Remove an instance of <see cref="TRegistered"/> from the registered instances.
            /// </summary>
            [MethodImpl(AggressiveInlining)]
            public static void UnregisterInstance(TRegistered instance)
            {
                // search from end - assume the oldest instances are the most likely to be long-lived
                for (int i = count - 1; i >= 0; --i)
                {
                    if (!ReferenceEquals(instances[i], instance))
                        continue;

                    // remove by swapping with last array element
                    instances[i] = instances[count - 1];
                    instances[count - 1] = null; // clear reference to allow gc
                    count -= 1;
                    return;
                }
            }

            static TRegistered[] ErrorEditMode()
            {
                Debug.LogError($"Cannot acquire registered object array in edit mode: <i>{typeof(TRegistered).Name}</i>");
                return null;
            }
        }
    }
}
