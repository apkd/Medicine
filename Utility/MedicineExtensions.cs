#nullable enable
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using static System.ComponentModel.EditorBrowsableState;
using static System.Runtime.CompilerServices.MethodImplOptions;

namespace Medicine
{
    /// <summary>
    /// Public extension methods.
    /// </summary>
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
        [MethodImpl(AggressiveInlining), DebuggerStepThrough]
        [EditorBrowsable(Never)]
        public static T? Optional<T>(this T? value) where T : class => value;

        [MethodImpl(AggressiveInlining), DebuggerStepThrough]
        [EditorBrowsable(Never)]
        public static T? Transient<T>(this T? value) where T : class => value;

        [MethodImpl(AggressiveInlining), DebuggerStepThrough]
        public static T CleanupDispose<T>(this T value) where T : IDisposable => value;

        [MethodImpl(AggressiveInlining), DebuggerStepThrough]
        public static T CleanupDestroy<T>(this T value) where T : UnityEngine.Object => value;
    }
}