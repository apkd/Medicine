using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;
using static System.Runtime.CompilerServices.MethodImplOptions;

namespace Medicine
{
    /// <summary>
    /// Represents Burst-compatible references to managed objects.<br/>
    /// Essentially a wrapper for a pointer to a class instance.
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
        public static implicit operator TClass(UnmanagedRef<TClass> unmanagedRef)
            => unmanagedRef.Resolve();

        [MethodImpl(AggressiveInlining)]
        public TClass Resolve()
        {
            var ptr = Ptr;
            return UnsafeUtility.As<nint, TClass>(ref ptr);
        }

        /// <summary>
        /// Returns a ref to an unmanaged value of type <typeparamref name="T"/> located at a specified memory offset.
        /// </summary>
        /// <param name="offset">The memory offset, in bytes, from the base pointer.</param>
        /// <returns> A writeable reference to the value of type <typeparamref name="T"/> at the specified offset. </returns>
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
    [SuppressMessage("ReSharper", "HeuristicUnreachableCode")]
    public static unsafe class UnmanagedRefExtensions
    {
        /// <summary>
        /// Returns true if the <see cref="UnityEngine.Object"/> has been destroyed (or the reference is null).
        /// </summary>
        [MethodImpl(AggressiveInlining)]
        public static bool IsInvalid<TClass>(this UnmanagedRef<TClass> classRef) where TClass : UnityEngine.Object
        {
            nint ptr = classRef.Ptr;
            nint nativePtr = ptr is not 0 ? *(nint*)(ptr + sizeof(nint) * 2) : 0;
            return nativePtr is 0;
        }

        /// <summary>
        /// Returns true if the <see cref="UnityEngine.Object"/> is not null and not a destroyed object.
        /// </summary>
        [MethodImpl(AggressiveInlining)]
        public static bool IsValid<TClass>(this UnmanagedRef<TClass> classRef) where TClass : UnityEngine.Object
        {
            nint ptr = classRef.Ptr;
            nint nativePtr = ptr is not 0 ? *(nint*)(ptr + sizeof(nint) * 2) : 0;
            return nativePtr is not 0;
        }

        /// <summary>
        /// Retrieves the unmanaged native object pointer associated with a UnityEngine.Object instance.
        /// </summary>
        /// <typeparam name="TClass">The type of UnityEngine.Object the unmanaged reference points to.</typeparam>
        /// <param name="classRef">A reference to the unmanaged UnityEngine.Object wrapper.</param>
        /// <returns>The native memory address of the native representation of a Unity object as an <see cref="nint"/> value.</returns>
        [MethodImpl(AggressiveInlining)]
        public static nint GetNativeObjectPtr<TClass>(this UnmanagedRef<TClass> classRef) where TClass : UnityEngine.Object
            => *(nint*)(classRef.Ptr + sizeof(nint) * 2);

        [MethodImpl(AggressiveInlining)]
        internal static void SetNativeObjectPtr<TClass>(this UnmanagedRef<TClass> classRef, nint value) where TClass : UnityEngine.Object
            => *(nint*)(classRef.Ptr + sizeof(nint) * 2) = value;

        /// <summary>
        /// Equivalent of the <see cref="UnityEngine.Object.GetInstanceID"/> method.
        /// Assumes that the referenced object is not null or destroyed.
        /// </summary>
        /// <remarks>
        /// The instance ID of an object acts like a handle to the in-memory instance.
        /// It is always unique and never has the value 0.
        /// Objects loaded from a file will be assigned a positive Instance ID.
        /// <br/></br>
        /// Newly created objects will have a negative Instance ID and retain that
        /// negative value even if the object is later saved to file.
        /// Therefore, the sign of the InstanceID value is not a safe indicator for
        /// whether the object is persistent.
        /// <br/></br>
        /// The ID changes between sessions of the player runtime and Editor.
        /// As such, the ID is not reliable for performing actions that could span between sessions, for example,
        /// loading an object state from a file.
        /// </remarks>
        [MethodImpl(AggressiveInlining)]
        public static int GetInstanceID<TClass>(this UnmanagedRef<TClass> classRef) where TClass : UnityEngine.Object
        {
#if UNITY_EDITOR
            // we can use this fast path in the editor
            return *(int*)(classRef.Ptr + sizeof(nint) * 3);
#elif MODULE_BURST
            // in player builds, the instance ID is not stored in the managed object;
            // we need to grab it from the actual native object
            return *(int*)(*(nint*)(classRef.Ptr + sizeof(nint) * 2) + offsetOfInstanceIDInCPlusPlusObject.Data);
#else
            // i can't really see why anyone would want to use UnmanagedRef<T> without
            // Burst Compiler installed, but it doesn't cost us much to support it, so here we go
            return *(int*)(*(nint*)(classRef.Ptr + sizeof(nint) * 2) + offsetOfInstanceIDInCPlusPlusObject);
#endif
        }

#if !UNITY_EDITOR
        const System.Reflection.BindingFlags PrivateStatic
            = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
#if MODULE_BURST
        enum OffsetOfInstanceIDTypeKey { }

        static readonly Unity.Burst.SharedStatic<nint> offsetOfInstanceIDInCPlusPlusObject
            = Unity.Burst.SharedStatic<nint>.GetOrCreate<nint, OffsetOfInstanceIDTypeKey>();

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InitializeOffsetOfInstanceID()
            => offsetOfInstanceIDInCPlusPlusObject.Data
                = (int)typeof(UnityEngine.Object)
                    .GetMethod("GetOffsetOfInstanceIDInCPlusPlusObject", PrivateStatic)
                    .Invoke(null, Array.Empty<object>());
#else
        static readonly nint offsetOfInstanceIDInCPlusPlusObject
            = (int)typeof(UnityEngine.Object)
                .GetMethod("GetOffsetOfInstanceIDInCPlusPlusObject", PrivateStatic)
                .Invoke(null, Array.Empty<object>());
#endif
#endif
    }
}