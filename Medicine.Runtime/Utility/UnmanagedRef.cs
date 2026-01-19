using System;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;
using static System.Runtime.CompilerServices.MethodImplOptions;

namespace Medicine
{
    /// <summary>
    /// A low-level wrapper for managing Burst-compatible references to instances of a class.
    /// Essentially a pointer to a class instance.
    /// </summary>
    /// <typeparam name="TClass">The type of the class to which this reference points. Must be a reference type.</typeparam>
    /// <remarks>
    /// This struct enables direct memory manipulation and interactions with classes from unmanaged contexts.
    /// It is up to you to ensure that the managed object is not garbage collected.<br/><br/>
    /// Unity (≤7) uses a non-compacting garbage collector, which means objects will not be relocated in memory.
    /// In later versions supporting modern .NET, this assumption may no longer hold true.
    /// </remarks>
    public readonly unsafe struct UnmanagedRef<TClass>
        : IEquatable<UnmanagedRef<TClass>>
        where TClass : class
    {
        public readonly nint Ptr;

        [MethodImpl(AggressiveInlining)]
        public UnmanagedRef(nint ptr)
            => Ptr = ptr;

        [MethodImpl(AggressiveInlining)]
        public UnmanagedRef(void* ptr)
            => Ptr = (nint)ptr;

        [MethodImpl(AggressiveInlining)]
        public UnmanagedRef(TClass obj)
            => Ptr = UnsafeUtility.As<TClass, nint>(ref obj);

        [MethodImpl(AggressiveInlining)]
        public static implicit operator UnmanagedRef<TClass>(TClass ptr)
            => new(ptr);

        [MethodImpl(AggressiveInlining)]
        public ref T Read<T>(int offset) where T : unmanaged
            => ref *(T*)(Ptr + offset);

        [MethodImpl(AggressiveInlining)]
        public bool Equals(UnmanagedRef<TClass> other)
            => Ptr == other.Ptr;
    }

    /// <summary>
    /// Extension methods for <see cref="UnmanagedRef{TClass}"/> representing unity objects.
    /// Objects derived from <see cref="UnityEngine.Object"/> have a common sequential layout, allowing us to
    /// access these internal fields.
    /// </summary>
    public static unsafe class UnmanagedRefExtensions
    {
        /// <summary>
        /// Returns true if the <see cref="UnityEngine.Object"/> has been destroyed (or the reference is null).
        /// </summary>
        [MethodImpl(AggressiveInlining)]
        public static bool IsDestroyed<TClass>(this UnmanagedRef<TClass> classRef) where TClass : UnityEngine.Object
        {
            nint ptr = classRef.Ptr;
            nint nativePtr = ptr != 0
                ? *(nint*)(ptr + sizeof(ulong) * 2)
                : 0;
            return nativePtr is 0;
        }

        /// <summary>
        /// Equivalent of the <see cref="UnityEngine.Object.GetInstanceID"/> method.
        /// </summary>
        [MethodImpl(AggressiveInlining)]
        public static int GetInstanceID<TClass>(this UnmanagedRef<TClass> classRef) where TClass : UnityEngine.Object
            => *(int*)(classRef.Ptr + sizeof(ulong) * 3);
    }
}