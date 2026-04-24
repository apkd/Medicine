using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Unity.Collections;
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

    public readonly struct ListAccess<T>
        : IEnumerable<T>
        where T : unmanaged
    {
        readonly ListAccess<T, T> impl;

        [MethodImpl(AggressiveInlining)]
        public ListAccess(
            UnmanagedRef<List<T>> listRef,
            int itemsOffset,
            int countOffset,
            int arrayLengthOffset,
            int arrayDataOffset
        )
            => impl = new(listRef, itemsOffset, countOffset, arrayLengthOffset, arrayDataOffset);

        public int Count
        {
            [MethodImpl(AggressiveInlining)]
            get => impl.Count;
            [MethodImpl(AggressiveInlining)]
            set => impl.Count = value;
        }

        [MethodImpl(AggressiveInlining)]
        public NativeArray<T> AsNativeArray()
            => impl.AsNativeArray();

        [MethodImpl(AggressiveInlining)]
        public NativeArray<T>.Enumerator GetEnumerator()
            => impl.GetEnumerator();

        [MethodImpl(AggressiveInlining)]
        IEnumerator<T> IEnumerable<T>.GetEnumerator()
            => GetEnumerator();

        [MethodImpl(AggressiveInlining)]
        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();
    }

    public readonly struct ListAccess<TSource, TElement>
        : IEnumerable<TElement>
        where TElement : unmanaged
    {
        readonly UnmanagedRef<List<TSource>> listRef;
        readonly int itemsOffset;
        readonly int countOffset;
        readonly int arrayLengthOffset;
        readonly int arrayDataOffset;

        [MethodImpl(AggressiveInlining)]
        public ListAccess(
            UnmanagedRef<List<TSource>> listRef,
            int itemsOffset,
            int countOffset,
            int arrayLengthOffset,
            int arrayDataOffset
        )
        {
            this.listRef = listRef;
            this.itemsOffset = itemsOffset;
            this.countOffset = countOffset;
            this.arrayLengthOffset = arrayLengthOffset;
            this.arrayDataOffset = arrayDataOffset;
        }

        public int Count
        {
            [MethodImpl(AggressiveInlining)]
            get => listRef.Ptr is 0 ? 0 : listRef.Read<int>(countOffset);
            [MethodImpl(AggressiveInlining)]
            set
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value));

                if (listRef.Ptr is 0)
                    throw new InvalidOperationException("Cannot set Count on a null List reference.");
#endif

                var arrayRef = listRef.Read<UnmanagedRef<TSource[]>>(itemsOffset);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (value > Internal.Utility.GetArrayLength(arrayRef, arrayLengthOffset))
                    throw new ArgumentOutOfRangeException(nameof(value), "Cannot set Count beyond the List capacity from unmanaged access.");
#endif

                listRef.Read<int>(countOffset) = value;
            }
        }

        [MethodImpl(AggressiveInlining)]
        public NativeArray<TElement> AsNativeArray()
        {
            if (listRef.Ptr is 0)
                return default;

            var arrayRef = listRef.Read<UnmanagedRef<TSource[]>>(itemsOffset);
            return Internal.Utility.AsNativeArray<TSource, TElement>(arrayRef, listRef.Read<int>(countOffset), arrayDataOffset);
        }

        [MethodImpl(AggressiveInlining)]
        public NativeArray<TElement>.Enumerator GetEnumerator()
            => AsNativeArray().GetEnumerator();

        [MethodImpl(AggressiveInlining)]
        IEnumerator<TElement> IEnumerable<TElement>.GetEnumerator()
            => GetEnumerator();

        [MethodImpl(AggressiveInlining)]
        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();
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

#if UNITY_6000_4_OR_NEWER
        /// <summary>
        /// Equivalent of the <see cref="UnityEngine.Object.GetEntityId"/> method.
        /// Assumes that the referenced object is not null or destroyed.
        /// </summary>
        /// <remarks>
        /// The entity ID acts like a handle to the in-memory instance.
        /// It changes between sessions and is not suitable for persistence.
        /// </remarks>
        [MethodImpl(AggressiveInlining)]
        public static UnityEngine.EntityId GetEntityID<TClass>(this UnmanagedRef<TClass> classRef) where TClass : UnityEngine.Object
#if UNITY_EDITOR
            => ReadManagedObjectId<TClass, UnityEngine.EntityId>(classRef);
#else
            => ReadNativeObjectId<TClass, UnityEngine.EntityId>(classRef);
#endif

        /// <summary>
        /// Legacy Unity object identity API. Use <see cref="GetEntityID{TClass}"/> on Unity 6000.4 or newer.
        /// </summary>
        [Obsolete("InstanceID APIs are obsolete on Unity >=6.4. Use EntityId APIs instead.", true)]
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [MethodImpl(AggressiveInlining)]
        public static int GetInstanceID<TClass>(this UnmanagedRef<TClass> classRef) where TClass : UnityEngine.Object
            => throw new NotSupportedException("InstanceID APIs are obsolete on Unity >=6.4. Use EntityId APIs instead.");
#else
        /// <summary>
        /// Equivalent of the <see cref="UnityEngine.Object.GetInstanceID"/> method.
        /// Assumes that the referenced object is not null or destroyed.
        /// </summary>
        /// <remarks>
        /// The instance ID acts like a handle to the in-memory instance.
        /// It changes between sessions and is not suitable for persistence.
        /// </remarks>
        [MethodImpl(AggressiveInlining)]
        public static int GetInstanceID<TClass>(this UnmanagedRef<TClass> classRef) where TClass : UnityEngine.Object
#if UNITY_EDITOR
            => ReadManagedObjectId<TClass, int>(classRef);
#else
            => ReadNativeObjectId<TClass, int>(classRef);
#endif
#endif

        [MethodImpl(AggressiveInlining)]
        static TObjectID ReadManagedObjectId<TClass, TObjectID>(UnmanagedRef<TClass> classRef)
            where TClass : UnityEngine.Object
            where TObjectID : unmanaged
            => classRef.Read<TObjectID>(sizeof(nint) * 3);

#if !UNITY_EDITOR
        const System.Reflection.BindingFlags PrivateStatic
            = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;

        [MethodImpl(AggressiveInlining)]
        static TObjectID ReadNativeObjectId<TClass, TObjectID>(UnmanagedRef<TClass> classRef)
            where TClass : UnityEngine.Object
            where TObjectID : unmanaged
#if MODULE_BURST
            => *(TObjectID*)(*(nint*)(classRef.Ptr + sizeof(nint) * 2) + offsetOfNativeObjectIdInCPlusPlusObject.Data);
#else
            => *(TObjectID*)(*(nint*)(classRef.Ptr + sizeof(nint) * 2) + offsetOfNativeObjectIdInCPlusPlusObject);
#endif

        [MethodImpl(AggressiveInlining)]
        static nint GetOffsetOfNativeObjectIdInCPlusPlusObject()
            => (int)(
                typeof(UnityEngine.Object).GetMethod("GetOffsetOfInstanceIDInCPlusPlusObject", PrivateStatic)
                ?? throw new MissingMethodException(typeof(UnityEngine.Object).FullName, "GetOffsetOfInstanceIDInCPlusPlusObject")
            ).Invoke(null, Array.Empty<object>())!;

#if MODULE_BURST
        enum OffsetOfNativeObjectIdTypeKey { }

        static readonly Unity.Burst.SharedStatic<nint> offsetOfNativeObjectIdInCPlusPlusObject
            = Unity.Burst.SharedStatic<nint>.GetOrCreate<nint, OffsetOfNativeObjectIdTypeKey>();

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InitializeOffsetOfNativeObjectId()
            => offsetOfNativeObjectIdInCPlusPlusObject.Data = GetOffsetOfNativeObjectIdInCPlusPlusObject();
#else
        static readonly nint offsetOfNativeObjectIdInCPlusPlusObject = GetOffsetOfNativeObjectIdInCPlusPlusObject();
#endif
#endif
    }
}
