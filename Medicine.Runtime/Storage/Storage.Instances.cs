using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using static System.ComponentModel.EditorBrowsableState;
using Object = UnityEngine.Object;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedTypeParameter

namespace Medicine.Internal
{
    public static partial class Storage
    {
        [EditorBrowsable(Never)]
        public static class Instances<T> where T : class
        {
            /// <remarks>
            /// Do not access directly!
            /// <p>Use <see cref="Find.Instances{T}"/> or the generated <c>.Instances</c> property instead.</p>
            /// </remarks>
            public static readonly List<T> List = new(capacity: 8);

            public static Span<T> AsSpan()
                => List.AsInternalsView().Array.AsSpan(List.Count);

            public static unsafe UnsafeList<UnmanagedRef<T>> AsUnmanaged()
                => new((UnmanagedRef<T>*)UnsafeUtility.AddressOf(ref UnsafeUtility.As<T, ulong>(ref List.AsInternalsView().Array[0])), List.Count);

            /// <summary>
            /// Registers the object as one of the active instances of <paramref name="T"/>.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static int Register(T instance)
            {
#if DEBUG
                if (instance == null)
                {
                    Debug.LogError($"Tried to register a null instance of {typeof(T).Name} for tracking.");
                    return -1;
                }
#endif
                int index = List.Count;

                if (instance is IInstanceIndex<T> trackIndex)
                    trackIndex.InstanceIndex = index;

                List.Add(instance);
                return index;
            }

            /// <summary>
            /// Unregisters the object from the list of active instances of <paramref name="T"/>.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static int Unregister(T instance)
            {
#if DEBUG
                if (ReferenceEquals(instance, null))
                {
                    Debug.LogError($"Singleton<{typeof(T).Name}> is null, ignoring");
                    return -1;
                }
#endif

                return RemoveFromEndSwapBack(List, instance);
            }

            static int RemoveFromEndSwapBack(List<T> list, T instance)
            {
                // RemoveFromEndSwapBack implementation:
                // - we are searching from the list end because it seems likely that we're
                //   most often interested in removing short-lived objects that were added recently
                // - returns the removed element index so we can unregister it from the
                //   TransformAccessArray using RemoveAtSwapBack
                // - needs to be kept in sync with the TransformAccessArray behavior
                // - if the class implements IInstanceIndex, we can skip the array search
                //   and retrieve the stored index

                var listView = list.AsInternalsView();
                var array = listView.Array;

                if (array is null) // strictly speaking, never possible?
                    return -1;

                int index = -1;

                if (instance is IInstanceIndex<T> selfTrackIndex)
                {
                    // get the stored index - no array search
                    index = selfTrackIndex.InstanceIndex;

                    // update swapped instance's index
                    // (we know the other element also implements the interface)
                    var lastElement = (IInstanceIndex<T>)array[listView.Count - 1];
                    lastElement.InstanceIndex = index;

                    selfTrackIndex.InstanceIndex = -1;
                }
                else // search from end
                {
                    int length = listView.Count;
                    for (int i = length - 1; i >= 0; --i)
                    {
                        if (ReferenceEquals(array[i], instance))
                        {
                            index = i;
                            break;
                        }
                    }

                    if (index < 0)
                        return -1;
                }

                int last = listView.Count - 1;

                // swap element at removed index with element at last index
                // (noop when index == last)
                array[index] = array[last];

                // clear the last element
                array[last] = null;

                // decrease element count
                listView.Count = last;
                listView.Version++;

                return index;
            }

            /// <inheritdoc cref="Register{T}"/>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static int RegisterWithInstanceID(T instance)
            {
                int index = Register(instance);
                if (index >= 0)
                    InstanceIDs<T>.List.Add((instance as Object)!.GetInstanceID());

                return index;
            }

            /// <inheritdoc cref="Unregister{T}"/>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static int UnregisterWithInstanceID(T instance)
            {
                int index = Unregister(instance);

#if DEBUG
                if (!InstanceIDs<T>.List.IsCreated)
                    return index; // possible right before domain reload
#endif

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                var safety = NativeListUnsafeUtility.GetAtomicSafetyHandle(ref InstanceIDs<T>.List);
                AtomicSafetyHandle.EnforceAllBufferJobsHaveCompleted(safety);
#endif

                if (index >= 0)
                    InstanceIDs<T>.List.RemoveAtSwapBack(index);

                return index;
            }

#if UNITY_EDITOR
            public static class EditMode
            {
                static int editModeVersion = int.MinValue;
                static readonly bool editModeIsScriptableObject = typeof(ScriptableObject).IsAssignableFrom(typeof(T));

                /// <remarks>
                /// This method is used to hook into the object constructor to invalidate the active object list.
                /// <list type="bullet">
                /// <item>Used as a hacky "OnObjectInstanceCreated" kind of callback</item>
                /// <item>This is used in user classes - technically we could just emit a ctor, but that could conflict with user code</item>
                /// <item>This is less ideal than OnEnable, but we don't want to mark user types with [ExecuteAlways]...</item>
                /// <item>Imprecise, but only used in edit mode, and in case the tracked list is accessed multiple times per update</item>
                /// <item>None of this code is shipped in game builds - we want [Track] to be as lightweight as possible</item>
                /// </list>
                /// </remarks>
                public static int Invalidate()
                    => editModeVersion = int.MinValue;

                static bool AnyInstanceBecameInvalid()
                {
                    foreach (var instance in List.AsInternalsView().Array ?? Array.Empty<T>())
                    {
                        // any instance was destroyed
                        if (instance as Object == null)
                            return true;

                        // any instance was deactivated
                        if (instance is Behaviour { isActiveAndEnabled: false })
                            return true;
                    }

                    return false;
                }

                internal static void Refresh()
                {
                    int frameCount = Time.frameCount;

#if !MEDICINE_EDITMODE_ALWAYS_REFRESH
                    if (editModeVersion == frameCount)   // refresh once per frame
                        if (!AnyInstanceBecameInvalid()) // refresh more often if we detect changes
                            return;
#endif

                    editModeVersion = frameCount;

                    // gathered instances
                    {
                        List.Clear();

                        if (editModeIsScriptableObject)
                        {
                            List.AddRange(Find.ObjectsByTypeAll<T>());
                        }
                        else
                        {
                            foreach (var instance in Find.ObjectsByType<T>())
                                if (instance is Behaviour { enabled: true })
                                    List.Add(instance);
                        }
                    }
                }
            }
#endif
        }
    }
}