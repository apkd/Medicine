#nullable enable
using System;
using System.ComponentModel;
using UnityEngine;
using static System.ComponentModel.EditorBrowsableState;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedTypeParameter

namespace Medicine.Internal
{
#if UNITY_EDITOR
    /// <remarks>
    /// Struct used to hook into the object constructor to invoke <c>Invalidate()</c>.
    /// <list type="bullet">
    /// <item>Used as a hacky "OnObjectInstanceCreated" kind of callback</item>
    /// <item>This is used in user classes - technically we could just emit a ctor, but that could conflict with user code</item>
    /// <item>This is less ideal than OnEnable, but we don't want to mark user types with [ExecuteAlways]...</item>
    /// <item>Imprecise, but only used in edit mode, and in case the tracked list is accessed multiple times per update</item>
    /// <item>None of this code is shipped in game builds - we want [Track] to be as lightweight as possible</item>
    /// </list>
    /// </remarks>
    [EditorBrowsable(Never)]
    public struct InvalidateInstanceToken<T> where T : class
    {
        // ReSharper disable once UnusedParameter.Global
        public InvalidateInstanceToken(int meaningOfLife)
            => Storage.Instances<T>.EditMode.Invalidate();
    }

    /// <inheritdoc cref="InvalidateInstanceToken{T}"/>
    [EditorBrowsable(Never)]
    public struct InvalidateSingletonToken<T> where T : class
    {
        // ReSharper disable once UnusedParameter.Global
        public InvalidateSingletonToken(int meaningOfLife)
            => Storage.Singleton<T>.EditMode.Invalidate();
    }
#endif

    /// <summary>
    /// Used internally by Medicine to store tracked instance information.
    /// </summary>
    /// <remarks>
    /// <b>Not intended as a public API!</b> You probably shouldn't be accessing this class directly...
    /// </remarks>
    [EditorBrowsable(Never)]
    public static partial class Storage
    {
#if UNITY_EDITOR
        // used to release static native resources (e.g., TransformAccessArray)
        static Action? beforeAssemblyUnload;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void OnAssemblyUnload()
        {
            void InvokeBeforeAssemblyUnload()
            {
                beforeAssemblyUnload?.Invoke();
                beforeAssemblyUnload = null;
            }

            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += InvokeBeforeAssemblyUnload;
        }
#endif
    }
}