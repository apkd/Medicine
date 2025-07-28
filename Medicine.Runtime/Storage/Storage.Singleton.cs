#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
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
        public static class Singleton
        {
            internal static readonly Dictionary<Type, Func<Object?>> UntypedAccess = new(capacity: 8);
        }

        [EditorBrowsable(Never)]
        public static class Singleton<T> where T : class
        {
            static class StaticInit
            {
                [Preserve]
                public static void Init() { }

                static StaticInit()
                    => Singleton.UntypedAccess.Add(typeof(T), static () => Instance as Object);
            }

#if UNITY_EDITOR
            static T? instance;

            /// <remarks>
            /// Do not access directly!
            /// <p>Use <see cref="Find.Singleton{T}"/> or the generated <c>.Instance</c> property instead.</p>
            /// </remarks>
            public static T? Instance
            {
                [MethodImpl(AggressiveInlining)]
                get
                {
                    if (Utility.EditMode)
                        EditMode.Refresh();

                    return instance;
                }
                set => instance = value;
            }
#else
            public static T? Instance;
#endif

            /// <summary>
            /// Registers the given object as the current active singleton instance of <paramref name="T"/>.
            /// </summary>
            public static void Register(T? instance)
            {
                StaticInit.Init();

                if (instance == null)
                {
#if DEBUG
                    Debug.LogError($"Singleton<{typeof(T).Name}> is null, ignoring");
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
                    Debug.LogError($"Singleton<{typeof(T).Name}> is null, ignoring");
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

                static readonly bool editModeIsScriptableObject
                    = typeof(ScriptableObject).IsAssignableFrom(typeof(T));

                /// <inheritdoc cref="Storage.Instances{T}.EditMode.Invalidate()"/>
                public static int Invalidate()
                    => editModeVersion = int.MinValue;

                static bool InstanceBecameInvalid()
                    => Instance as Object == null ||                        // destroyed
                       Instance is Behaviour { isActiveAndEnabled: false }; // deactivated;

                static T? Search()
                {
                    if (editModeIsScriptableObject)
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