using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using Unity.Collections.LowLevel.Unsafe;
using static System.Runtime.CompilerServices.MethodImplOptions;

// ReSharper disable StaticMemberInGenericType
namespace Medicine
{
    public static partial class NonAlloc
    {
        // number of recyclable lists to switch between
        const int RecyclableListCount = 4;

        // pool of RecyclableLists.
        // we're switching between multiple lists for each GetRecyclableList to avoid issues with nested GetComponentsNonAlloc usage.
        // this is still a non-deal solution because it will cause crashes when we're using 4+ temporary RecyclableLists at the
        // same time, but it should cover vast majority of use cases
        static readonly RecyclableList[] recyclableLists;

        // index of the last returned list
        static int currentRecyclableList = 0;

        static NonAlloc()
        {
            recyclableLists = new RecyclableList[RecyclableListCount];
            for (int i = 0; i < recyclableLists.Length; ++i)
                recyclableLists[i] = new RecyclableList(initialCapacity: 1024);
        }

        /// <summary>
        /// A wrapper type that stores a <see cref="List"/> together with its backing array, and implements utilities that allow you to
        /// re-use allocated memory by mutating the list's/array's managed type at runtime.
        /// </summary>
        public sealed class RecyclableList
        {
            /// <summary> The recyclable list of objects. </summary>
            public readonly List<object> InternalList = new List<object>();

            /// <summary> The actual backing array for the <see cref="InternalList"/>. </summary>
            public Array InternalBackingArray { get; private set; }

            Type currentType;
            int actualCapacity;

            /// <summary> The actual allocated length of the <see cref="InternalBackingArray"/>. </summary>
            public int ActualCapacity
                => actualCapacity;

            /// <summary> The current managed type of the elements stored in the <see cref="InternalList"/>/<see cref="InternalBackingArray"/>. </summary>
            public Type CurrentType
                => currentType;

            /// <summary>
            /// Main constructor.
            /// This allows you to create your own RecyclableLists to use in worker threads, to avoid nested iteration issues, etc.
            /// </summary>
            /// <param name="initialCapacity"> Initial capacity of the RecyclableList's <see cref="InternalBackingArray"/>. </param>
            public RecyclableList(int initialCapacity = 256)
            {
                actualCapacity = initialCapacity;
                InternalBackingArray = new object[initialCapacity];
                Unsafe.SetListBackingArray(InternalList, InternalBackingArray);
            }

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
            public List<T> AsList<T>(bool clear = true) where T : class
            {
                EnsureListSyncedWithArray();
                ExpandArrayToActualLength();

                if (clear)
                    InternalList.Clear();

                SetType<T>();
                return InternalList as List<T>;
            }

            /// <summary>
            /// Prepares the RecyclableList to be used as a generic array, setting array length and element type.
            /// (This is the method you're probably looking for if you're trying to use the RecyclableList API manually and you need an array).
            /// </summary>
            /// <param name="length"> Requested length of the array. </param>
            /// <param name="clear"> Setting this to false allows you to skip clearing the array before you use it. </param>
            /// <typeparam name="T"> Type of elements that will be stored in the array. </typeparam>
            /// <returns> Temporary array that should be used and discarded in current scope. </returns>
            [MethodImpl(AggressiveInlining)]
            public T[] AsArray<T>(int length, bool clear = true) where T : class
            {
                void ThrowArgumentOutOfRange()
                    => throw new ArgumentOutOfRangeException(nameof(length));

                if (length <= 0)
                    ThrowArgumentOutOfRange();

                if (clear)
                    InternalList.Clear();

                SetType<T>();
                SetCapacity(length);
                return InternalBackingArray as T[];
            }

            /// <summary>
            /// Ensures that the <see cref="InternalBackingArray"/> reference correctly points to the <see cref="InternalList"/>'s backing array.
            /// This can no longer be true if the list's capacity has changed, for example by adding elements to it or by a GetComponents call.
            /// </summary>
            [MethodImpl(AggressiveInlining)]
            public unsafe void EnsureListSyncedWithArray()
            {
                var capacity = InternalList.Capacity;

                // detect if the list's capacity has changed
                // this indicates that the internal array has been replaced with a new one
                // (in that case, we want to start using its internal array as the new staticArray)
                if (capacity == InternalBackingArray.Length)
                    return;

                void* ptr = UnsafeUtility.PinGCObjectAndGetAddress(InternalList, out ulong gcHandle);
                var array = UnsafeUtility.AsRef<ListHeader>(ptr).Array;
                UnsafeUtility.ReleaseGCObject(gcHandle);
                InternalBackingArray = array;
                actualCapacity = capacity;
            }

            /// <summary>
            /// Trims the list's backing array to the number of elements stored in the list. 
            /// </summary>
            [MethodImpl(AggressiveInlining)]
            public void TrimArrayToListLength()
                => Unsafe.OverwriteArrayLength(InternalBackingArray, InternalList.Count);

            /// <summary>
            /// Expands the <see cref="InternalBackingArray"/>'s length to the actual memory-allocated capacity.
            /// </summary>
            [MethodImpl(AggressiveInlining)]
            public void ExpandArrayToActualLength()
                => Unsafe.OverwriteArrayLength(InternalBackingArray, actualCapacity);

            /// <summary>
            /// Sets the array length (== list capacity), reallocating or trimming the array if necessary.
            /// </summary>
            [MethodImpl(AggressiveInlining)]
            public void SetCapacity(int length)
            {
                if (InternalBackingArray.Length == length)
                    return;

                if (length <= actualCapacity)
                {
                    Unsafe.OverwriteArrayLength(InternalBackingArray, length);
                }
                else
                {
                    InternalList.Clear();
                    InternalBackingArray = new object[length];
                    Unsafe.SetListBackingArray(InternalList, InternalBackingArray);
                }
            }

            /// <summary>
            /// Sets the element type of the recyclable array and list.
            /// </summary>
            /// <typeparam name="T">Type of elements stored in the array/list.</typeparam>
            [MethodImpl(AggressiveInlining)]
            public unsafe void SetType<T>()
            {
                var type = typeof(T);
                if (currentType == typeof(T))
                    return;

                currentType = type;
                void* arrayPtr = UnsafeUtility.PinGCObjectAndGetAddress(InternalBackingArray, out ulong arrayGcHandle);
                void* listPtr = UnsafeUtility.PinGCObjectAndGetAddress(InternalList, out ulong listGcHandle);
                UnsafeUtility.AsRef<ObjectHeader>(arrayPtr) = TypeHeaders<T[]>.Header;
                UnsafeUtility.AsRef<ObjectHeader>(listPtr) = TypeHeaders<List<T>>.Header;
                UnsafeUtility.ReleaseGCObject(arrayGcHandle);
                UnsafeUtility.ReleaseGCObject(listGcHandle);
            }
        }

        // generic "dictionary-like" static class that is used to obtain the header of a given managed type.
        // by overwriting this header with a header of another type, we can effectively mutate the object's managed type.
        static class TypeHeaders<T>
        {
            /// <summary> Managed object header data for type <see cref="T"/>. </summary>
            public static readonly ObjectHeader Header;

            static unsafe TypeHeaders()
            {
                // create temporary instance of managed type in order to read the type header
                // this is done once per type in the lifetime of the program

                // ReSharper disable once AssignNullToNotNullAttribute
                var tempInstance = typeof(T).IsArray
                    // create array of 0 length
                    ? Array.CreateInstance(typeof(T).GetElementType(), 0)
                    // create object instance without calling the ctor
                    : FormatterServices.GetUninitializedObject(typeof(T));

                void* ptr = UnsafeUtility.PinGCObjectAndGetAddress(tempInstance, out ulong gcHandle);
                UnsafeUtility.CopyPtrToStructure(ptr, out Header);
                UnsafeUtility.ReleaseGCObject(gcHandle);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        readonly struct ObjectHeader
        {
            readonly IntPtr data;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ListHeader
        {
            readonly IntPtr data0, data1;
            public Array Array;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ArrayHeader
        {
            readonly IntPtr data0, data1, data2;
            public int ManagedArrayLength;
        }
    }
}
