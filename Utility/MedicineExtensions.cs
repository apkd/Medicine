#nullable enable
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Medicine.Internal;
using UnityEngine;
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

        /// <summary>
        /// Enumerates every component of type <typeparamref name="T"/> found on the children
        /// of the GameObject.
        /// </summary>
        /// <typeparam name="T"> The component type to look for. </typeparam>
        /// <param name="gameObject"> Root GameObject whose child hierarchy is inspected. </param>
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
    }
}