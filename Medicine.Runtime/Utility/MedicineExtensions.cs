#nullable enable
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using Medicine.Internal;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.SceneManagement;
using static System.ComponentModel.EditorBrowsableState;
using static System.Runtime.CompilerServices.MethodImplOptions;
using Component = UnityEngine.Component;

namespace Medicine
{
    [EditorBrowsable(Never)]
    public static class MedicineExtensions
    {
        /// <summary>
        /// Suppresses the null check and error log when initializing an object reference
        /// that can potentially be null.
        /// </summary>
        /// <remarks>
        /// Only works when assigning to an <see cref="InjectAttribute"/> initialization
        /// expression. Does nothing otherwise.
        /// </remarks>
        [MethodImpl(AggressiveInlining), EditorBrowsable(Never), DebuggerStepThrough]
        public static T? Optional<T>(this T? value)
            where T : class
            => value;

        /// <summary>
        /// Emits a <c>Cleanup()</c> method that invokes the provided expression.
        /// Can be used to dispose unmanaged resources allocated in the <c>[Inject]</c> method.
        /// You should manually call the <c>Cleanup()</c> method, e.g., in <c>OnDestroy()</c>.
        /// </summary>
        /// <param name="action">Cleanup action to invoke.</param>
        [MethodImpl(AggressiveInlining), EditorBrowsable(Never), DebuggerStepThrough]
        public static T? Cleanup<T>(this T? value, Action<T> action)
            => value;

        /// <inheritdoc cref="Find.ComponentsInScene{T}"/>
        [MethodImpl(AggressiveInlining)]
        public static ComponentsInSceneEnumerable<T> EnumerateComponentsInScene<T>(this Scene scene, bool includeInactive = false)
            where T : class
            => Find.ComponentsInScene<T>(scene, includeInactive);

        /// <summary>
        /// Enumerates every component of type <typeparamref name="T"/> found on the children
        /// of the GameObject.
        /// </summary>
        /// <typeparam name="T"> The component type to look for. </typeparam>
        /// <param name="gameObject"> Root GameObject, whose child hierarchy is inspected. </param>
        /// <param name="includeInactive"> Whether to include GameObjects that are currently inactive in the scene. </param>
        /// <returns> A sequence that produces every matching component in the child hierarchy. </returns>
        /// <remarks>
        /// <list type="bullet">
        /// <item> Search includes the provided GameObject. </item>
        /// <item> Children are yielded in the same order as they exist in the Unity hierarchy. </item>
        /// <item> You can use <c>foreach</c> to iterate over the instances. </item>
        /// <item> The returned struct is compatible with <a href="https://github.com/Cysharp/ZLinq">ZLINQ</a>. </item>
        /// <item> Uses <see cref="GameObject.GetComponentsInChildren{T}(bool,System.Collections.Generic.List{T})"/> together with a pooled list underneath. </item>
        /// </list>
        /// </remarks>
        public static ComponentsEnumerable<T> EnumerateComponentsInChildren<T>(this GameObject gameObject, bool includeInactive = false)
            where T : class
            => ComponentsEnumerable<T>.InChildren(gameObject, includeInactive);

        /// <summary>
        /// Enumerates every component of type <typeparamref name="T"/> found on the parents
        /// of the GameObject.
        /// </summary>
        /// <typeparam name="T">The component type to look for.</typeparam>
        /// <param name="gameObject">GameObject whose parent chain is inspected.</param>
        /// <param name="includeInactive">Whether to include GameObjects that are currently inactive in the scene.</param>
        /// <returns>
        /// A sequence that produces every matching component in the parent chain.
        /// </returns>
        /// <remarks>
        /// <list type="bullet">
        /// <item> Search includes the provided GameObject. </item>
        /// <item> Parents are yielded starting with the immediate parent and moving upward to the root. </item>
        /// <item> You can use <c>foreach</c> to iterate over the instances. </item>
        /// <item> The returned struct is compatible with <a href="https://github.com/Cysharp/ZLinq">ZLINQ</a>. </item>
        /// <item> Uses <see cref="GameObject.GetComponentsInParent{T}(bool,System.Collections.Generic.List{T})"/> together with a pooled list underneath. </item>
        /// </list>
        /// </remarks>
        public static ComponentsEnumerable<T> EnumerateComponentsInParents<T>(this GameObject gameObject, bool includeInactive = false)
            where T : class
            => ComponentsEnumerable<T>.InParents(gameObject, includeInactive);

        /// <summary>
        /// Enumerates every component of type <typeparamref name="T"/> that is attached
        /// directly to the GameObject.
        /// </summary>
        /// <typeparam name="T">The component type to look for.</typeparam>
        /// <param name="gameObject">GameObject whose own components are inspected.</param>
        /// <returns>A sequence containing all matching components attached to the object.</returns>
        /// <remarks>
        /// <list type="bullet">
        /// <item> You can use <c>foreach</c> to iterate over the instances. </item>
        /// <item> The returned struct is compatible with <a href="https://github.com/Cysharp/ZLinq">ZLINQ</a>. </item>
        /// <item> Uses <see cref="GameObject.GetComponents{T}(System.Collections.Generic.List{T})"/> together with a pooled list underneath. </item>
        /// </list>
        /// </remarks>
        public static ComponentsEnumerable<T> EnumerateComponents<T>(this GameObject gameObject)
            where T : class
            => ComponentsEnumerable<T>.InSelf(gameObject);

        /// <param name="component">A component on the GameObject whose child hierarchy is inspected.</param>
        /// <inheritdoc cref="EnumerateComponentsInChildren{T}(UnityEngine.GameObject,bool)"/>
        public static ComponentsEnumerable<T> EnumerateComponentsInChildren<T>(this Component component, bool includeInactive = false)
            where T : class
            => ComponentsEnumerable<T>.InChildren(component, includeInactive);

        /// <param name="component">A component on the GameObject whose parent chain is inspected.</param>
        /// <inheritdoc cref="EnumerateComponentsInParents{T}(UnityEngine.GameObject,bool)"/>
        public static ComponentsEnumerable<T> EnumerateComponentsInParents<T>(this Component component, bool includeInactive = false)
            where T : class
            => ComponentsEnumerable<T>.InParents(component, includeInactive);

        /// <param name="component">A component on the GameObject to inspect.</param>
        /// <inheritdoc cref="EnumerateComponents{T}(UnityEngine.GameObject)"/>
        public static ComponentsEnumerable<T> EnumerateComponents<T>(this Component component)
            where T : class
            => ComponentsEnumerable<T>.InSelf(component);

        /// <inheritdoc cref="Scene.GetRootGameObjects()"/>
        /// <returns>
        /// A <see cref="PooledList{T}"/> of root-level GameObjects in the given scene.
        /// This list must be disposed of after use.
        /// </returns>
        [MustDisposeResource]
        public static PooledList<GameObject> GetRootGameObjectsPooledList(this Scene scene)
        {
            var pooledList = PooledList.Get<GameObject>();
            scene.GetRootGameObjects(pooledList.List);
            return pooledList;
        }

#if MODULE_ZLINQ
        /// <summary>
        /// Enumerates to a <see cref="PooledList{T}"/>, internally based on <see cref="ListPool{T}"/>.
        /// Remember to call <see cref="Dispose"/> <b>exactly once</b> to return the list to the pool.
        /// </summary>
        [MustDisposeResource]
        public static PooledList<T> ToPooledList<TEnumerator, T>(this ZLinq.ValueEnumerable<TEnumerator, T> source)
            where TEnumerator : struct, ZLinq.IValueEnumerator<T>
#if NET9_0_OR_GREATER
        , allows ref struct
#endif
        {
            using var enumerator = source.Enumerator;
            var pooledList = PooledList.Get<T>();
            var list = pooledList.List;

            if (enumerator.TryGetNonEnumeratedCount(out var count))
            {
                if (count is 0)
                    return pooledList;

                if (list.Capacity < count)
                    list.Capacity = count;

                var listView = list.AsInternalsView();
                var array = listView.Array;
                listView.Count = count;

                if (enumerator.TryCopyTo(array.AsSpan(0, count), 0))
                    return pooledList;

                var i = 0;
                while (enumerator.TryGetNext(out var item))
                {
                    array![i] = item;
                    i++;
                }
            }
            else
            {
                if (list.Capacity is 0)
                    list.Capacity = 16;

                while (enumerator.TryGetNext(out var item))
                    list.Add(item);
            }

            return pooledList;
        }
#endif
    }
}