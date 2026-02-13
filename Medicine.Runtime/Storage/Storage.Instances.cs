#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using static System.ComponentModel.EditorBrowsableState;
using static System.Runtime.CompilerServices.MethodImplOptions;
using Object = UnityEngine.Object;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedTypeParameter

namespace Medicine.Internal
{
    public static partial class Storage
    {
        [EditorBrowsable(Never)]
        public static class Instances
        {
            internal static readonly Dictionary<Type, Func<IEnumerable<object>>> UntypedAccess = new(capacity: 8);
        }

        [EditorBrowsable(Never)]
        public static class Instances<T> where T : class
        {
            static class StaticInit
            {
                [MethodImpl(AggressiveInlining)]
                internal static void RunOnce() { }

                static StaticInit()
                {
                    List.Capacity = 64;
                    Instances.UntypedAccess.Add(typeof(T), static () => List);
#if UNITY_EDITOR
                    enterPlayModeCleanup += static () => List.Clear();
#endif
                }
            }

            /// <summary>
            /// Main active instance storage for type <typeparamref name="T"/>.
            /// </summary>
            /// <remarks>
            /// Do not modify! <br/>
            /// Prefer read-only access via <see cref="Find.Instances{T}"/> or the
            /// generated <c>.Instances</c> property instead.
            /// </remarks>
            public static readonly List<T> List = new(capacity: 0);

            public static bool TypeIsRegistered
                => List.Capacity > 0;

            [MethodImpl(AggressiveInlining)]
            public static Span<T> AsSpan()
                => List.AsInternalsView().Array.AsSpanUnsafe(List.Count);

            [MethodImpl(AggressiveInlining)]
            public static UnsafeList<UnmanagedRef<T>> AsUnmanaged()
                => List.AsUnsafeList<T, UnmanagedRef<T>>();

            /// <summary>
            /// Registers the object as one of the active instances of <paramref name="T"/>.
            /// </summary>
            public static int Register(T instance)
            {
                StaticInit.RunOnce();
#if DEBUG
                if (!Utility.IsNativeObjectAlive(instance as Object))
                {
                    Debug.LogError(
                        $"Tried to register a null instance of {typeof(T).Name} for tracking. " +
                        $"This probably indicates a logic error in your code. " +
                        $"This check is not present in release builds and will result in bugs/errors."
                    );

                    return -1;
                }
#endif
                int index = List.Count;
                var trackIndex = (IInstanceIndex<T>)instance;
                trackIndex.InstanceIndex = index;

                List.Add(instance);
                return index;
            }

            /// <summary>
            /// Unregisters the object from the list of active instances of <paramref name="T"/>.
            /// </summary>
            public static int Unregister(T instance)
            {
#if DEBUG
                if (ReferenceEquals(instance, null))
                {
                    Debug.LogError(
                        $"Tried to unregister a null instance of {typeof(T).Name}. " +
                        $"This probably indicates a logic error in your code. " +
                        $"This check is not present in release builds and will result in bugs/errors."
                    );

                    return -1;
                }
#endif

                return RemoveFromEndSwapBack(List, instance);
            }

            [MethodImpl(AggressiveInlining)]
            static int RemoveFromEndSwapBack(List<T> list, T instance)
            {
                // RemoveFromEndSwapBack implementation:
                // - we are searching from the list end because it seems likely that we're
                //   most often interested in removing short-lived objects that were added recently
                // - returns the removed element index so we can unregister it from the
                //   TransformAccessArray using RemoveAtSwapBack
                // - needs to be kept in sync with the TransformAccessArray behavior
                // - tracked types implement IInstanceIndex<T>, so we can skip array search
                //   and retrieve the stored index directly

                var listView = list.AsInternalsView();
                var array = listView.Array;

                if (array is null) // strictly speaking, never possible?
                    return -1;
#if DEBUG
                if (listView.Count == 0)
                {
                    Debug.LogError(
                        $"Tried to unregister {typeof(T).Name} from an empty tracked list. " +
                        "This probably indicates a logic error in your code."
                    );
                    return -1;
                }
#endif

                int index;
                var selfTrackIndex = (IInstanceIndex<T>)instance;
                index = selfTrackIndex.InstanceIndex;
#if DEBUG
                if ((uint)index >= (uint)listView.Count)
                {
                    Debug.LogError(
                        $"Invalid InstanceIndex for {typeof(T).Name}: {index} (count: {listView.Count}). " +
                        "This probably indicates a logic error in your code, and will cause errors in release builds."
                    );
                    return -1;
                }
                else if (!ReferenceEquals(array[index], instance))
                {
                    Debug.LogError(
                        $"InstanceIndex mismatch for {typeof(T).Name}: stored index {index} does not match instance. " +
                        "This probably indicates a logic error in your code, and will cause errors in release builds."
                    );
                    return -1;
                }

                if (index != listView.Count - 1)
                {
                    var lastElement = (IInstanceIndex<T>)array[listView.Count - 1];
                    lastElement.InstanceIndex = index;
                }
#else
                if (index != listView.Count - 1)
                {
                    var lastElement = UnsafeUtility.As<T, IInstanceIndex<T>>(ref array[listView.Count - 1]);
                    lastElement.InstanceIndex = index;
                }
#endif
                selfTrackIndex.InstanceIndex = -1;

                int last = listView.Count - 1;

                // swap element at removed index with element at last index
                // (noop when index == last)
                array[index] = array[last];

                // clear the last element
                array[last] = null!;

                // decrease element count
                listView.Count = last;
                listView.Version++;

                return index;
            }

            /// <inheritdoc cref="Register{T}"/>
            public static int RegisterWithInstanceID(T instance)
            {
                int index = Register(instance);
                if (index >= 0)
                    InstanceIDs<T>.List.Add(UnsafeUtility.As<T, Object>(ref instance)!.GetInstanceID());

                return index;
            }

            /// <inheritdoc cref="Unregister{T}"/>
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
                    var (array, n) = List.AsInternalsView();

                    if (array is null)
                        return false;

                    for (int i = 0; i < n; i++)
                    {
                        var instance = array[i];

                        // any instance was destroyed
                        if (!Utility.IsNativeObjectAlive(instance as Object))
                            return true;

                        // any instance was deactivated
                        if (instance is Behaviour { isActiveAndEnabled: false })
                            return true;
                    }

                    return false;
                }

                internal static void Refresh()
                {
                    StaticInit.RunOnce();
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

                        if (Utility.TypeInfo<T>.IsScriptableObject)
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
