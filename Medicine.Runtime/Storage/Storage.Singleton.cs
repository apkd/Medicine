#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Scripting;
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
        public static class Singleton
        {
            internal static readonly Dictionary<Type, Func<Object?>> UntypedAccess = new(capacity: 8);
        }

        [EditorBrowsable(Never)]
        public static class Singleton<T> where T : class
        {
            static class StaticInit
            {
                [MethodImpl(AggressiveInlining)]
                internal static void RunOnce() { }

                static StaticInit()
                    => Singleton.UntypedAccess.Add(typeof(T), static () => Instance as Object);
            }

            [EditorBrowsable(Never)]
            public static T? RawInstance;

            /// <remarks>
            /// Do not access directly!
            /// <p>Use <see cref="Find.Singleton{T}"/> or the generated <c>.Instance</c> property instead.</p>
            /// </remarks>
            public static T? Instance
            {
                [MethodImpl(AggressiveInlining)]
                get
                {
#if UNITY_EDITOR
                    if (Utility.EditMode)
                        EditMode.Refresh();
#endif

#if MEDICINE_DISABLE_SINGLETON_DESTROYED_FILTER
                    return RawInstance;
#else
                    return Utility.IsNativeObjectAlive(RawInstance as Object)
                        ? RawInstance
                        : null; // ensure pure null is returned for destroyed objects
#endif
                }
                set => RawInstance = value;
            }

            /// <summary>
            /// Registers the given object as the current active singleton instance of <paramref name="T"/>.
            /// </summary>
            public static void Register(T? instance)
            {
                StaticInit.RunOnce();

                if (instance == null)
                {
#if DEBUG
                    Debug.LogError($"Tried to register null Singleton<{typeof(T).Name}> instance, ignoring");
#endif
                    return;
                }

                Instance = instance;
            }

            /// <summary>
            /// Unregisters the given object as the current active singleton instance of <paramref name="T"/>.
            /// </summary>
            public static void Unregister(T? instance)
            {
                if (instance == null)
                {
#if DEBUG
                    Debug.LogError($"Tried to unregister null Singleton<{typeof(T).Name}> instance, ignoring");
#endif
                    return;
                }

                if (!ReferenceEquals(Instance, instance))
                    return;

                Instance = null;
            }

#if UNITY_EDITOR
            public static class EditMode
            {
                static int editModeVersion = int.MinValue;

                /// <inheritdoc cref="Storage.Instances{T}.EditMode.Invalidate()"/>
                public static int Invalidate()
                    => editModeVersion = int.MinValue;

                static bool InstanceBecameInvalid()
                    => RawInstance as Object == null ||                        // destroyed
                       RawInstance is Behaviour { isActiveAndEnabled: false }; // deactivated;

                static T? Search()
                {
                    if (Utility.TypeInfo<T>.IsScriptableObject)
                        return Find.ObjectsByTypeAll<T>().FirstOrDefault();

                    foreach (var obj in Find.ObjectsByType<T>())
                        if (obj is Behaviour { enabled: true })
                            return obj;

                    return null;
                }

                [MethodImpl(NoInlining)]
                internal static void Refresh()
                {
                    int frameCount = Time.frameCount;

#if !MEDICINE_EDITMODE_ALWAYS_REFRESH
                    if (editModeVersion == frameCount) // refresh once per frame
                        if (!InstanceBecameInvalid())  // refresh more often if we detect changes
                            return;
#endif

                    editModeVersion = frameCount;
                    Instance = Search();
                }
            }
#endif
        }
    }
}