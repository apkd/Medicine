#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.SceneManagement;
using static System.Runtime.CompilerServices.MethodImplOptions;
using static UnityEngine.FindObjectsInactive;
using Object = UnityEngine.Object;

// ReSharper disable UnusedMember.Global

namespace Medicine
{
    using Internal;

    [SuppressMessage("Performance", "MED011")]
    public static class Find
    {
        /// <summary>
        /// Retrieves the active singleton instance.
        /// </summary>
        /// <typeparam name="T"> The type of the singleton object. <br/>
        /// The target class needs to be decorated with [<see cref="SingletonAttribute"/>]. </typeparam>
        /// <returns> The active singleton instance of the specified type if available; otherwise, null. </returns>
        /// <remarks>
        /// <list type="bullet">
        /// <item> MonoBehaviours and ScriptableObjects decorated with [<see cref="SingletonAttribute"/>] will automatically register/unregister themselves
        /// as the active singleton instance in OnEnable/OnDisable. </item>
        /// <item> In edit mode, to provide better compatibility with editor tooling, <see cref="UnityEngine.Object.FindObjectsByType(System.Type,FindObjectsSortMode)"/>
        /// is used internally to attempt to locate the object (cached for one editor update). </item>
        /// </list>
        /// </remarks>
        [MethodImpl(AggressiveInlining)]
        public static T? Singleton<T>()
            where T : class
            => Storage.Singleton<T>.Instance;

        /// <summary>
        /// Retrieves the active singleton instance.
        /// </summary>
        /// <param name="type"> The type of the singleton object.<br/>
        /// The target class needs to be decorated with [<see cref="SingletonAttribute"/>]. </param>
        /// <returns> The active singleton instance of the specified type if available; otherwise, null. </returns>
        /// <remarks><inheritdoc cref="Singleton{T}"/></remarks>
        [MethodImpl(AggressiveInlining)]
        public static Object? Singleton(System.Type type)
            => Storage.Singleton.UntypedAccess.TryGetValue(type, out var getter) ? getter() : null;

        /// <summary>
        /// Gets an enumerator that allows iteration through all singleton instances marked with
        /// the [<see cref="SingletonAttribute"/>].
        /// </summary>
        /// <remarks>
        /// Instances are retrieved using a type-based lookup and returned as <see cref="UnityEngine.Object"/> refs.
        /// Useful for diagnostic or introspection purposes.
        /// </remarks>
        [MethodImpl(AggressiveInlining)]
        public static SingletonInstancesEnumerable AllActiveSingletons()
            => default;

        /// <summary>
        /// Allows you to enumerate all enabled instances of <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T"> The type of the tracked object. <br/>
        /// The class needs to be marked with <see cref="TrackAttribute"/>. </typeparam>
        /// <remarks>
        /// <list type="bullet">
        /// <item> MonoBehaviours and ScriptableObjects marked with the <see cref="TrackAttribute"/> will automatically register/unregister themselves
        /// in the active instance list in OnEnable/OnDisable. </item>
        /// <item> This property can return null if the singleton instance doesn't exist or hasn't executed OnEnabled yet. </item>
        /// <item> In edit mode, to provide better compatibility with editor tooling, <see cref="Object.FindObjectsByType(System.Type,UnityEngine.FindObjectsSortMode)"/>
        /// is used internally to find object instances (cached for one editor update). </item>
        /// <item> You can use <c>foreach</c> to iterate over the instances. </item>
        /// <item> If you’re enabling/disabling instances while enumerating, you need to use <c>Find.Instances&lt;T&gt;().Copied()</c>. </item>
        /// <item> The returned struct is compatible with <a href="https://github.com/Cysharp/ZLinq">ZLINQ</a>. </item>
        /// </list>
        /// </remarks>
        [MethodImpl(AggressiveInlining)]
        public static TrackedInstances<T> Instances<T>()
            where T : class
            => default;

        /// <summary>
        /// Allocation-free iterator over all components of type <typeparamref name="T"/> in the given scene.
        /// </summary>
        /// <remarks>
        /// Internally walks every root GameObject and calls
        /// <c>GetComponentsInChildren</c>, avoiding creating temporary arrays.
        /// Set <paramref name="includeInactive"/> to <see langword="true"/> to
        /// visit components on inactive objects.
        /// </remarks>
        /// <typeparam name="T">Component type to enumerate.</typeparam>
        /// <param name="scene">Scene to scan.</param>
        /// <param name="includeInactive">Include inactive objects.</param>
        /// <returns>Enumeration usable in a <c>foreach</c> loop.</returns>
        [MethodImpl(AggressiveInlining)]
        public static ComponentsInSceneEnumerable<T> ComponentsInScene<T>(Scene scene, bool includeInactive = false)
            where T : class
            => new(scene, includeInactive);

        /// <summary>
        /// Equivalent to <see cref="Object.FindObjectsByType{T}(FindObjectsInactive,FindObjectsSortMode)"/>.
        /// Slightly faster because this implementation avoids an unnecessary array copy.
        /// <p>Retrieves a list of all loaded objects of type <typeparamref name="T"/>.</p>
        /// </summary>
        /// <param name="includeInactive">
        /// Whether to include components attached to inactive GameObjects.
        /// If you don't specify this parameter, this function doesn't include inactive objects in the results.
        /// </param>
        /// <param name="sortMode">
        /// Whether to sort the returned array by <see cref="Object.m_InstanceID"/>.
        /// Not sorting the array makes this function run significantly faster.
        /// </param>
        /// <returns>
        /// <p>The array of objects found matching the type specified.</p>
        /// <p>Note that each time you call this function results in a new array allocation!</p>
        /// </returns>
        /// <remarks>
        /// <p>This method is slow, allocates a result array, and is not suitable for frequently executed gameplay code.</p>
        /// <p>Consider using <see cref="Instances{T}"/> instead when possible.</p>
        /// </remarks>
        [MethodImpl(AggressiveInlining)]
        public static T[] ObjectsByType<T>(bool includeInactive = false, FindObjectsSortMode sortMode = 0)
            where T : class
        {
            if (Utility.TypeInfo<T>.IsInterface)
            {
                // extremely inefficient path for finding objects by interface...
                // this is sad to look at, but it's sometimes necessary for editor
                // tooling, and FindObjectsByType does not work for interfaces.

                var temp = Object.FindObjectsByType(
                    type: typeof(Object),
                    includeInactive ? Include : Exclude,
                    sortMode
                );

                var list = new List<T>(capacity: 16);

                foreach (var x in temp)
                    if (x is T t)
                        list.Add(t);

                return list.ToArray();
            }

            var array = Object.FindObjectsByType(
                type: typeof(T),
                includeInactive ? Include : Exclude,
                sortMode
            );

            // the array type returned by the API is actually already T[]; it turns out that in the generic
            // version of the method, Unity seemingly copies the array around for no reason whatsoever
            return UnsafeUtility.As<Object[], T[]>(ref array);
        }

        /// <summary>
        /// Returns any live object of type <typeparamref name="T"/>.<br/>
        /// Equivalent of <see cref="Object.FindAnyObjectByType{T}()"/>, but with some fast paths.
        /// Returns a <c>[Singleton]</c> / <c>[Track]</c> instance if available, and otherwise
        /// caches the result of <c>FindAnyObjectByType</c> until the object is destroyed.
        /// </summary>
        /// <returns><inheritdoc cref="Object.FindAnyObjectByType(System.Type)"/></returns>
        public static T? AnyObjectByType<T>(bool includeInactive = false) where T : Object
        {
            if (Storage.Singleton<T>.RawInstance is var singleton)
                if (Utility.IsNativeObjectAlive(singleton))
                    return singleton;

            if (Storage.Instances<T>.TypeIsRegistered)
                return Storage.Instances<T>.List[0];

            if (Storage.AnyObject<T>.WeakReference?.TryGetTarget(out var cached) is true)
                if (Utility.IsNativeObjectAlive(cached))
                    return cached;

            var any = Object.FindAnyObjectByType(
                type: typeof(T),
                findObjectsInactive: includeInactive ? Include : Exclude
            ) as T;

            if (Utility.IsNativeObjectAlive(any))
            {
                if (Storage.AnyObject<T>.WeakReference != null)
                    Storage.AnyObject<T>.WeakReference.SetTarget(any);
                else
                    Storage.AnyObject<T>.WeakReference = new(any);

                return any;
            }

            return null;
        }

        /// <summary>
        /// Equivalent to <see cref="Resources.FindObjectsOfTypeAll{T}"/>.
        /// Slightly faster because this implementation avoids an unnecessary array copy.
        /// <p> Returns a list of all objects of Type T. </p>
        /// <p>
        /// This function can return any type of Unity object that is loaded, including game objects,
        /// prefabs, materials, meshes, textures, etc. It will also list internal objects, therefore,
        /// be careful with the way you handle the returned objects.
        /// </p>
        /// <p> Contrary to Object.FindObjectsOfType this function will also always list disabled objects.</p>
        /// </summary>
        /// <typeparam name="T"> Type of object to find. </typeparam>
        /// <returns>
        /// <p>The array of objects found matching the type specified.</p>
        /// <p>Note that each time you call this function results in a new array allocation!</p>
        /// </returns>
        /// <remarks>
        /// <p>This method is slow, allocates a result array, and is not suitable for frequently executed gameplay code.</p>
        /// <p>Consider using <see cref="Instances{T}"/> instead when possible.</p>
        /// </remarks>
        [MethodImpl(AggressiveInlining)]
        public static T[] ObjectsByTypeAll<T>()
            where T : class
        {
            var array = Resources.FindObjectsOfTypeAll(typeof(T));

            // the array type returned by the API is actually already T[]; it turns out that in the generic
            // version of the method, Unity seemingly copies the array around for no reason whatsoever
            return UnsafeUtility.As<Object[], T[]>(ref array);
        }
    }
}