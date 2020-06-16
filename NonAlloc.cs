using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using Obj = UnityEngine.Object;
using UnityEngine;
using static System.Runtime.CompilerServices.MethodImplOptions;

namespace Medicine
{
    public static partial class NonAlloc
    {
        /// <summary>
        /// Returns a list of all active loaded objects of given type.
        /// This does not return assets (such as meshes, textures or prefabs), or objects with HideFlags.DontSave set.
        /// Objects attached to inactive GameObjects are only included if inactiveObjects is set to true.
        /// Use FindObjectsOfTypeAll to avoid these limitations.
        /// In Editor, this searches the Scene view by default.
        /// If you want to find an object in the Prefab stage, see the StageUtility APIs.
        /// </summary>
        /// <remarks>
        /// This is an optimized version of the Object.FindObjectsOfType<T> method that reduces unnecessary array copying.
        /// It can be used as a direct replacement.
        /// </remarks>
#if UNITY_2020_1_OR_NEWER // includeInactive argument only supported in Unity 2020.1+
        [UsedImplicitly]
        [MethodImpl(AggressiveInlining)]
        public static T[] FindObjectsOfType<T>(bool includeInactive = false) where T : Object
        {
            var array = Object.FindObjectsOfType(typeof(T), includeInactive);
            Unsafe.SetManagedObjectType<T[]>(array);
            return array as T[];
        }
#else
        [UsedImplicitly]
        [MethodImpl(AggressiveInlining)]
        public static T[] FindObjectsOfType<T>() where T : Object
        {
            var array = Object.FindObjectsOfType(typeof(T));
            Unsafe.SetManagedObjectType<T[]>(array);
            return array as T[];
        }
#endif

        /// <summary>
        /// Returns an array of all objects of type T.
        /// This function can return any type of Unity object that is loaded, including game objects, prefabs,
        /// materials, meshes, textures, etc.
        /// It will also list internal objects, therefore be careful with the way you handle the returned objects.
        /// </summary>
        /// <remarks>
        /// This is an optimized version of the Resources.FindObjectsOfTypeAll<T> method that reduces unnecessary array copying.
        /// It can be used as a direct replacement.
        /// </remarks>
        [UsedImplicitly]
        [MethodImpl(AggressiveInlining)]
        public static T[] FindObjectsOfTypeAll<T>() where T : Object
        {
            var array = Resources.FindObjectsOfTypeAll(typeof(T));
            Unsafe.SetManagedObjectType<T[]>(array);
            return array as T[];
        }

        /// <summary>
        /// Loads all assets of given type in a folder or file at path in a Resources folder.
        /// </summary>
        /// <param name="path">
        /// Path of the target folder. When using the empty string (i.e., ""),
        /// the function will load the entire contents of the Resources folder.
        /// </param>
        /// <remarks>
        /// This is an optimized version of the Resources.LoadAll<T> method that reduces unnecessary array copying.
        /// It can be used as a direct replacement.
        /// </remarks>
        [UsedImplicitly]
        [MethodImpl(AggressiveInlining)]
        public static T[] LoadAll<T>(string path) where T : Object
        {
            var array = Resources.LoadAll(path, typeof(T));
            Unsafe.SetManagedObjectType<T[]>(array);
            return array as T[];
        }

        /// <summary>
        /// Equivalent to <see cref="GameObject.GetComponents{T}()"/>, but re-uses a recyclable buffer to minimize memory allocations
        /// and improve performance.
        /// WARNING: Make sure you use the result array directly and not store any references to it, as they may become invalid on next call.
        /// </summary>
        /// <typeparam name="T"> Type of components to find. </typeparam>
        /// <returns> Temporary array of components. </returns>
        [MethodImpl(AggressiveInlining)]
        public static T[] GetComponentsNonAlloc<T>(this GameObject gameObject) where T : class
        {
            var recyclableList = GetRecyclableList();
            var list = recyclableList.AsList<T>(clear: false);
            gameObject.GetComponents(list);
            recyclableList.TrimArrayToListLength();
            return recyclableList.InternalBackingArray as T[];
        }

        /// <summary>
        /// Equivalent to <see cref="GameObject.GetComponentsInChildren{T}()"/>, but re-uses a recyclable buffer to minimize memory allocations.
        /// WARNING: Make sure you use the result array directly and not store any references to it, as they may become invalid on next call.
        /// </summary>
        /// <typeparam name="T"> Type of components to find. </typeparam>
        /// <returns> Temporary array of components. </returns>
        [MethodImpl(AggressiveInlining)]
        public static T[] GetComponentsInChildrenNonAlloc<T>(this GameObject gameObject, bool includeInactive = false) where T : class
        {
            var recyclableList = GetRecyclableList();
            var list = recyclableList.AsList<T>(clear: false);
            gameObject.GetComponentsInChildren(includeInactive, list);
            recyclableList.TrimArrayToListLength();
            return recyclableList.InternalBackingArray as T[];
        }

        /// <summary>
        /// Equivalent to <see cref="GameObject.GetComponentsInParent{T}()"/>, but re-uses a recyclable buffer to minimize memory allocations.
        /// WARNING: Make sure you use the result array directly and not store any references to it, as they may become invalid on next call.
        /// </summary>
        /// <typeparam name="T"> Type of components to find. </typeparam>
        /// <returns> Temporary array of components. </returns>
        [MethodImpl(AggressiveInlining)]
        public static T[] GetComponentsInParentNonAlloc<T>(this GameObject gameObject, bool includeInactive = false) where T : class
        {
            var recyclableList = GetRecyclableList();
            var list = recyclableList.AsList<T>(clear: false);
            gameObject.GetComponentsInParent(includeInactive, list);
            recyclableList.TrimArrayToListLength();
            return recyclableList.InternalBackingArray as T[];
        }

        /// <summary>
        /// Equivalent to <see cref="GameObject.GetComponents{T}()"/>, but re-uses a recyclable buffer to minimize memory allocations
        /// and improve performance.
        /// WARNING: Make sure you use the result array directly and not store any references to it, as they may become invalid on next call.
        /// </summary>
        /// <typeparam name="T"> Type of components to find. </typeparam>
        /// <returns> Temporary array of components. </returns>
        [MethodImpl(AggressiveInlining)]
        public static T[] GetComponentsNonAlloc<T>(this Component component) where T : class
            => GetComponentsNonAlloc<T>(component.gameObject);


        /// <summary>
        /// Equivalent to <see cref="GameObject.GetComponentsInChildren{T}()"/>, but re-uses a recyclable buffer to minimize memory allocations.
        /// WARNING: Make sure you use the result array directly and not store any references to it, as they may become invalid on next call.
        /// </summary>
        /// <typeparam name="T"> Type of components to find. </typeparam>
        /// <returns> Temporary array of components. </returns>
        [MethodImpl(AggressiveInlining)]
        public static T[] GetComponentsInChildrenNonAlloc<T>(this Component component, bool includeInactive = false) where T : class
            => GetComponentsInChildrenNonAlloc<T>(component.gameObject, includeInactive);


        /// <summary>
        /// Equivalent to <see cref="GameObject.GetComponentsInParent{T}()"/>, but re-uses a recyclable buffer to minimize memory allocations.
        /// WARNING: Make sure you use the result array directly and not store any references to it, as they may become invalid on next call.
        /// </summary>
        /// <typeparam name="T"> Type of components to find. </typeparam>
        /// <returns> Temporary array of components. </returns>
        [MethodImpl(AggressiveInlining)]
        public static T[] GetComponentsInParentNonAlloc<T>(this Component component, bool includeInactive = false) where T : class
            => GetComponentsInParentNonAlloc<T>(component.gameObject, includeInactive);

        /// <summary>
        /// Gets a RecyclableList that is based on a recycled buffer.
        /// </summary>
        [MethodImpl(AggressiveInlining)]
        public static RecyclableList GetRecyclableList()
            => recyclableLists[currentRecyclableList = (currentRecyclableList + 1) % RecyclableListCount];

        /// <summary>
        /// Gets an array of given length that is based on a recycled buffer.
        /// </summary>
        /// <param name="length"> Requested length of the array. </param>
        /// <param name="clear"> Settings this to false allows you to skip clearing the array before you use it. </param>
        /// <typeparam name="T"> Type of elements that will be stored in the list. </typeparam>
        /// <returns> Temporary <see cref="List{T}"/> that should be used and discarded in current scope. </returns>
        [MethodImpl(AggressiveInlining)]
        public static T[] GetArray<T>(int length, bool clear = true) where T : class
            => GetRecyclableList().AsArray<T>(length, clear);

        /// <summary>
        /// Prepares the RecyclableList to be used as a <see cref="List"/>, restoring original capacity and setting element type.
        /// (This is the method you're probably looking for if you're trying to use the RecyclableList API manually and you need a <see cref="List"/>).
        /// </summary>
        /// <param name="clear">
        /// Setting this to false allows you to skip clearing the list before you use it.
        /// This is useful if you're going to pass it to a method that clears the list anyway, such as <see cref="UnityEngine.GameObject.GetComponents{T}()"/>
        /// </param>
        /// <typeparam name="T"> Type of elements that will be stored in the list. </typeparam>
        /// <returns> Temporary <see cref="List{T}"/> that should be used and discarded in current scope. </returns>
        [MethodImpl(AggressiveInlining)]
        public static List<T> GetList<T>(bool clear = true) where T : class
            => GetRecyclableList().AsList<T>(clear);
    }
}
