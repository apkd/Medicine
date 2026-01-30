#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using static System.ComponentModel.EditorBrowsableState;
using static System.Runtime.CompilerServices.MethodImplOptions;
using static Medicine.SingletonAttribute.Strategy;
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
            public static SingletonAttribute.Strategy Strategy;
            static bool autoInstantiateInProgress;

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
#if MEDICINE_DISABLE_SINGLETON_DESTROYED_FILTER
                    if (ReferenceEquals(RawInstance, null))
                        if (Utility.TypeInfo<T>.IsAutoInstantiate)
                            TryAutoInstantiate();

                    return RawInstance;
#else
                    var instance = RawInstance;

                    if (Utility.IsNativeObjectAlive(instance as Object))
                        return instance;

                    if (Utility.TypeInfo<T>.IsAutoInstantiate)
                        instance = TryAutoInstantiate();
#if UNITY_EDITOR
                    if (Utility.EditMode)
                        if (Utility.IsNativeObjectDead(instance as Object))
                            instance = EditMode.Refresh();
#endif
                    if (Utility.IsNativeObjectDead(instance as Object))
                        instance = null; // "purify" the reference to ensure actual null is returned

                    return RawInstance = instance;
#endif
                }
                set => RawInstance = value;
            }

            /// <summary>
            /// Registers the given object as the current active singleton instance of <paramref name="T"/>.
            /// </summary>
            public static void Register(T? instance, SingletonAttribute.Strategy strategy)
            {
                StaticInit.RunOnce();
                Strategy = strategy;

                if (instance == null)
                {
#if DEBUG
                    Debug.LogError($"Tried to register null Singleton<{typeof(T).Name}> instance, ignoring");
#endif
                    return;
                }

                var existing = GetFilteredLiveInstance();

                if (ReferenceEquals(existing, instance))
                    return; // in theory, should never happen?

                if (ReferenceEquals(existing, null))
                    RawInstance = instance;
                else
                    ResolveConflict(existing, instance, strategy);
            }

            /// <summary>
            /// Unregisters the given object as the current active singleton instance of <paramref name="T"/>.
            /// </summary>
            public static void Unregister(T? instance, SingletonAttribute.Strategy strategy)
            {
                Strategy = strategy;

                if (instance == null)
                {
#if DEBUG
                    Debug.LogError($"Tried to unregister null Singleton<{typeof(T).Name}> instance, ignoring");
#endif
                    return;
                }

                if (!ReferenceEquals(RawInstance, instance))
                    return;

                RawInstance = null;
            }

            [MethodImpl(AggressiveInlining)]
            static T? GetFilteredLiveInstance()
            {
                var instance = RawInstance;
#if MEDICINE_DISABLE_SINGLETON_DESTROYED_FILTER
                return instance;
#else
                if (Utility.IsNativeObjectAlive(instance as Object))
                    return instance;

                RawInstance = null;
                return null;
#endif
            }

            static void ResolveConflict(T existing, T incoming, SingletonAttribute.Strategy strategy)
            {
                string BuildConflictMessage()
                {
                    string existingName = (existing as Object)?.name ?? existing.ToString();
                    string incomingName = (incoming as Object)?.name ?? incoming.ToString();

                    string action = (strategy.Has(Destroy), strategy.Has(KeepExisting)) switch
                    {
                        (true, true)   => $"Destroying the new instance ({incomingName}) and keeping the existing one ({existingName}).",
                        (true, false)  => $"Destroying the previous instance ({existingName}) and keeping the new one ({incomingName}).",
                        (false, true)  => $"Keeping the existing instance ({existingName}). (Discarding {incomingName})",
                        (false, false) => $"Replacing the previous instance ({existingName}) with the new one ({incomingName}).",
                    };

                    return $"Singleton<{typeof(T).Name}> already has an active instance. {action}";
                }

                if (strategy.Has(ThrowException))
                    throw new InvalidOperationException($"Singleton<{typeof(T).Name}> already has an active instance.");

                if (strategy.Has(LogError))
                    Debug.LogError(BuildConflictMessage(), existing as Object ?? incoming as Object);
                else if (strategy.Has(LogWarning))
                    Debug.LogWarning(BuildConflictMessage(), existing as Object ?? incoming as Object);

                if (strategy.Has(KeepExisting))
                {
                    if (strategy.Has(Destroy) && incoming is Object obj)
                    {
#if UNITY_EDITOR
                        if (Utility.EditMode)
                        {
                            // need to delay the call - using DestroyImmediate in OnEnable crashes the editor
                            UnityEditor.EditorApplication.delayCall += () => TryDestroy(obj);

                            // a bit of a hack: reset the m_CachedPtr to mark the object as dead
                            new UnmanagedRef<Object>(obj).SetNativeObjectPtr(0);
                        }
                        else
#endif
                        {
                            TryDestroy(obj);
                        }
                    }
                }
                else
                {
                    if (strategy.Has(Destroy) && existing is Object obj)
                        TryDestroy(obj);

                    RawInstance = incoming;
                }
            }

            static T? TryAutoInstantiate()
            {
                if (autoInstantiateInProgress)
                    return null;

                if (Utility.TypeInfo<T>.IsAbstract)
                    return null;

                if (Utility.TypeInfo<T>.IsInterface)
                    return null;

                autoInstantiateInProgress = true;

                try
                {
                    T? instance;
                    var type = typeof(T);

                    if (Utility.TypeInfo<T>.IsScriptableObject)
                    {
                        instance = ScriptableObject.CreateInstance(type) as T;
                        if (instance == null)
                            return null;

                        StaticInit.RunOnce();
                        return instance;
                    }

                    if (Utility.TypeInfo<T>.IsMonoBehaviour)
                    {
                        var go = new GameObject(type.Name);

                        if (Utility.EditMode)
                            go.hideFlags = HideFlags.HideAndDontSave;
                        else
                            Object.DontDestroyOnLoad(go);

                        try
                        {
                            instance = go.AddComponent(type) as T;
                            if (instance == null)
                            {
                                TryDestroy(go);
                                return null;
                            }

                            StaticInit.RunOnce();
                            return instance;
                        }
                        catch (Exception ex)
                        {
                            TryDestroy(go);
                            Debug.LogError($"Failed to auto-instantiate singleton {type.Name}: {ex.Message}");
                        }
                    }
                }
                finally
                {
                    autoInstantiateInProgress = false;
                }

                return null;
            }

            static void TryDestroy(Object? obj)
            {
                if (Utility.IsNativeObjectDead(obj))
                    return;

#if UNITY_EDITOR
                if (UnityEditor.EditorUtility.IsPersistent(obj))
                    return;
#endif

                if (Utility.EditMode)
                    Object.DestroyImmediate(obj);
                else
                    Object.Destroy(obj);
            }

#if UNITY_EDITOR
            public static class EditMode
            {
                static int editModeVersion = int.MinValue;

                /// <inheritdoc cref="Storage.Instances{T}.EditMode.Invalidate()"/>
                public static int Invalidate()
                    => editModeVersion = int.MinValue;

                static bool InstanceBecameInvalid()
                    => Utility.IsNativeObjectDead(RawInstance as Object) ||                        // destroyed
                       RawInstance is Behaviour { isActiveAndEnabled: false }; // deactivated;

                static T? Search()
                {
                    if (Utility.TypeInfo<T>.IsScriptableObject)
                        return Find.ObjectsByTypeAll<T>().FirstOrDefault();

                    if (Utility.TypeInfo<T>.IsMonoBehaviour)
                        foreach (var obj in Find.ObjectsByType<T>())
                            if (obj is Behaviour { enabled: true })
                                return obj;

                    return null;
                }

                [MethodImpl(NoInlining)]
                internal static T? Refresh()
                {
                    int frameCount = Time.frameCount;

#if !MEDICINE_EDITMODE_ALWAYS_REFRESH
                    if (editModeVersion == frameCount) // refresh once per frame
                        if (!InstanceBecameInvalid())  // refresh more often if we detect changes
                            return RawInstance;
#endif

                    editModeVersion = frameCount;
                    return Search();
                }
            }
#endif
        }
    }
}