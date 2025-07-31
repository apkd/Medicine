#nullable enable
using System;
using System.ComponentModel;
using static System.ComponentModel.EditorBrowsableState;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedTypeParameter

namespace Medicine.Internal
{
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

        [UnityEditor.InitializeOnLoadMethod]
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