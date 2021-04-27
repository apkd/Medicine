using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using static System.Runtime.CompilerServices.MethodImplOptions;
using UnityEngine;
using Obj = UnityEngine.Object;

#pragma warning disable 162
// ReSharper disable StaticMemberInGenericType
namespace Medicine
{
    public static partial class RuntimeHelpers
    {
        /// <summary>
        /// Helper methods related to the [Inject.Single] implementation.
        /// </summary>
        public static class Singleton<TSingleton> where TSingleton : Obj
        {
            static TSingleton instance;

            static TSingleton uninitializedInstance;

            /// <summary>
            /// Register the object as the active <see cref="TSingleton"/> instance.
            /// </summary>
            /// <remarks>
            /// This is a helper method. Useful for some edge cases, but you don't usually need to use it directly.
            /// See <see cref="Register.Single"/> and <see cref="Inject.Single"/> to learn more.
            /// </remarks>
            [MethodImpl(AggressiveInlining)]
            public static void RegisterInstance(TSingleton obj)
            {
#if UNITY_EDITOR
                // only allow the registration of ScriptableObject singleton instances from Preloaded Assets.
                // ------------------------------------------------------------------------------------------
                // in editor, we can have multiple loaded instances of the SO, which will result in all of them
                // trying to register themselves as the active instance.
                // in build, the only loaded instance will be the one in preloaded assets, which means we don't have that problem
                // (as long as the developer doesn't reference other instances from other loaded objects - not supported for now).
                if (obj is ScriptableObject)
                    if (System.Array.IndexOf(UnityEditor.PlayerSettings.GetPreloadedAssets(), obj) < 0)
                        return;
#endif

                if (!instance)
                {
                    if (MedicineDebug)
                        Debug.Log($"Registering singleton instance: <i>{obj.name}</i> as <i>{typeof(TSingleton).Name}</i>", obj);

                    instance = obj;
                }
                else if (instance != obj)
                {
                    Debug.LogError($"Failed to register singleton instance <i>{obj.name}</i> as <i>{typeof(TSingleton).Name}</i>: a registered instance already exists: <i>{instance.name}</i>", obj);
                }
            }

            /// <summary>
            /// Unregister the object from being the active <see cref="TSingleton"/> instance.
            /// </summary>
            /// <remarks>
            /// This is a helper method. Useful for some edge cases, but you don't usually need to use it directly.
            /// See <see cref="Register.Single"/> and <see cref="Inject.Single"/> to learn more.
            /// </remarks>
            [MethodImpl(AggressiveInlining)]
            public static void UnregisterInstance(TSingleton obj)
            {
                if (ReferenceEquals(instance, obj))
                {
                    if (MedicineDebug)
                        Debug.Log($"Unregistering singleton instance: <i>{obj.name}</i> as <i>{typeof(TSingleton).Name}</i>", obj);

                    instance = null;
                }
                else
                {
                    if (MedicineDebug)
                        Debug.LogError($"Failed to unregister singleton instance: <i>{obj.name}</i> as <i>{typeof(TSingleton).Name}</i>", obj);
                }
            }

            /// <summary>
            /// Get the active registered <see cref="TSingleton"/> instance.
            /// Logs an error and returns null when there is no registered instance.
            /// </summary>
            /// <remarks>
            /// This is a helper method. Useful for some edge cases, but you don't usually need to use it directly.
            /// See <see cref="Register.Single"/> and <see cref="Inject.Single"/> to learn more.
            /// </remarks>
            [MethodImpl(AggressiveInlining)]
            public static TSingleton GetInstance()
            {
#if UNITY_EDITOR
                if (!ApplicationIsPlaying)
                {
                    if (instance)
                        return instance;

                    return instance = TryFindObjectByType() ?? TryFindScriptableObject() ?? ErrorNoSingletonInstance();
                }
                else
#endif
                {
                    // ReSharper disable once Unity.NoNullCoalescing
                    // we can safely use reference comparison assuming objects always unregister themselves in OnDestroy
                    return instance ?? TryFindScriptableObject() ?? ErrorNoSingletonInstance();
                }
            }

            static TSingleton TryFindObjectByType()
            {
                var objectsOfType = NonAlloc.FindObjectsOfType<TSingleton>();

                return objectsOfType.Length > 0
                    ? objectsOfType[0]
                    : null;
            }

            static TSingleton TryFindScriptableObject()
            {
#if UNITY_EDITOR
                // create static noninitialized instance for fast inheritance checks
                if (ReferenceEquals(uninitializedInstance, null))
                    uninitializedInstance = FormatterServices.GetUninitializedObject(typeof(TSingleton)) as TSingleton;

                // give up unless derived from ScriptableObject
                if (!(uninitializedInstance is ScriptableObject))
                    return null;

                // try find ScriptableObject instance in preloaded assets
                var preloadedAssets = UnityEditor.PlayerSettings.GetPreloadedAssets();

                foreach (var asset in preloadedAssets)
                    if (asset is TSingleton singleton)
                        return singleton;
#endif

                return null;
            }

            static TSingleton ErrorNoSingletonInstance()
            {
                Debug.LogError(
                    ReferenceEquals(instance, null)
                        ? $"No registered singleton instance: <i>{typeof(TSingleton).Name}</i>"
                        : $"Singleton instance has been destroyed: <i>{typeof(TSingleton).Name}</i>"
                );

                return null;
            }
        }
    }
}
