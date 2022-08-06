using System;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using static System.Runtime.CompilerServices.MethodImplOptions;
#pragma warning disable CS0162 // ReSharper disable HeuristicUnreachableCode
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

            static TRegistered[] instances = CreateInstanceArray();
            static int capacity = InitialCapacity;
            static int count = 0;
            
#if UNITY_EDITOR
            static bool checkedAttribute;
#endif

            // this method lets us avoid a static ctor to ensure beforefieldinit:
            // https://docs.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1810
            static TRegistered[] CreateInstanceArray()
            {
#if UNITY_EDITOR
                reinitializeAction += () =>
                {
                    // if (MedicineDebug)
                    Debug.Log($"Clearing collection: <i>{typeof(TRegistered).Name}</i>");
                    instances = new TRegistered[capacity = InitialCapacity];
                    count = 0;
                };

                debugAction += () => Debug.Log($"Collection<{typeof(TRegistered).Name}> = {count}/{capacity}");
#endif
                return new TRegistered[InitialCapacity];
            }

            /// <summary>
            /// Get an array of active registered instances of type <see cref="TRegistered"/>.
            /// </summary>
            /// <remarks>
            /// This is a helper method. Useful for some edge cases, but you don't usually need to use it directly.
            /// See <see cref="Register.All"/> and <see cref="Inject.All"/> to learn more.
            /// </remarks>
            [MethodImpl(AggressiveInlining)]
            public static TRegistered[] GetInstances()
            {
                if (!ApplicationIsPlaying)
                    return ErrorEditMode();
                
#if UNITY_EDITOR
                if (!checkedAttribute && typeof(TRegistered).CustomAttributes.All(x => x.AttributeType != typeof(Register.All)))
                    Debug.LogError($"Tried to obtain all instances of {typeof(TRegistered).Name}, but it isn't marked with [Medicine.Register.All].");

                checkedAttribute = true;
#endif

                if (count == 0)
                    return Array.Empty<TRegistered>();
                
#if MEDICINE_DEBUG
                var instancesForEnumeration = new TRegistered[count];

                Array.Copy(
                    sourceArray: instances,
                    destinationArray: instancesForEnumeration,
                    length: count
                );

                return instancesForEnumeration;
#elif MEDICINE_FUNSAFE_COLLECTIONS
                // copyless implementation - trim the array and return it directly without copying
                NonAlloc.Unsafe.OverwriteArrayLength(instances, count);
                return instances;
#else
                // copy instances to temporary buffer
                // this avoids issues with instances being disabled during enumeration
                var instancesForEnumeration = NonAlloc.GetArray<TRegistered>(length: count, clear: false);

                Array.Copy(
                    sourceArray: instances,
                    destinationArray: instancesForEnumeration,
                    length: count
                );

                return instancesForEnumeration;
#endif
            }

            /// <summary>
            /// Add an instance of <see cref="TRegistered"/> to the registered instances.
            /// </summary>
            /// <remarks>
            /// This is a helper method. Useful for some edge cases, but you don't usually need to use it directly.
            /// See <see cref="Register.All"/> and <see cref="Inject.All"/> to learn more.
            /// </remarks>
            [MethodImpl(AggressiveInlining)]
            public static void RegisterInstance(TRegistered instance)
            {
                if (MedicineDebug && (instance == null || instance is UnityEngine.Object obj && !obj))
                {
                    Debug.LogError($"Tried to register null {typeof(TRegistered).Name} instance.");
                    return;
                }
                
                if (MedicineDebug)
                    Debug.Log($"Registering {instance} as {typeof(TRegistered).Name}");

                if (count == capacity)
                    Resize();

#if MEDICINE_FUNSAFE_COLLECTIONS && !MEDICINE_DEBUG
                // copyless implementation - array was (possibly) trimmed during enumeration.
                // ensure array length is reset to capacity before registering new instances
                NonAlloc.Unsafe.OverwriteArrayLength(instances, capacity);
#endif

                instances[count++] = instance;

                static void Resize()
                {
                    capacity *= 2;
                    Array.Resize(ref instances, capacity);
                }
            }

            /// <summary>
            /// Remove an instance of <see cref="TRegistered"/> from the registered instances.
            /// </summary>
            /// <remarks>
            /// This is a helper method. Useful for some edge cases, but you don't usually need to use it directly.
            /// See <see cref="Register.All"/> and <see cref="Inject.All"/> to learn more.
            /// </remarks>
            [MethodImpl(AggressiveInlining)]
            public static void UnregisterInstance(TRegistered instance)
            {
                if (MedicineDebug && (instance == null || instance is UnityEngine.Object obj && !obj))
                {
                    Debug.LogError($"Tried to unregister null {typeof(TRegistered).Name} instance.");
                    return;
                }

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
