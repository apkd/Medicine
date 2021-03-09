using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using Unity.Collections.LowLevel.Unsafe;
using static System.Runtime.CompilerServices.MethodImplOptions;

namespace Medicine
{
    public static partial class NonAlloc
    {
        [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
        public static class Unsafe
        {
            [MethodImpl(AggressiveInlining)]
            public static unsafe T[] GetInternalArray<T>(List<T> list)
            {
                void* ptr = UnsafeUtility.PinGCObjectAndGetAddress(list, out ulong gcHandle);
                var array = UnsafeUtility.AsRef<ListHeader>(ptr).Array;
                UnsafeUtility.ReleaseGCObject(gcHandle);
                return array as T[];
            }

            [MethodImpl(AggressiveInlining)]
            public static unsafe void OverwriteArrayLength(Array array, int length)
            {
                if (array.Length == length)
                    return;

                void* ptr = UnsafeUtility.PinGCObjectAndGetAddress(array, out ulong gcHandle);
                UnsafeUtility.AsRef<ArrayHeader>(ptr).ManagedArrayLength = length;
                UnsafeUtility.ReleaseGCObject(gcHandle);
            }

            [MethodImpl(AggressiveInlining)]
            public static unsafe void SetListBackingArray(object InternalList, Array array)
            {
                void* ptr = UnsafeUtility.PinGCObjectAndGetAddress(InternalList, out ulong gcHandle);
                UnsafeUtility.AsRef<ListHeader>(ptr).Array = array;
                UnsafeUtility.ReleaseGCObject(gcHandle);
            }

            [MethodImpl(AggressiveInlining)]
            public static unsafe void SetManagedObjectType<T>(object source)
            {
                void* ptr = UnsafeUtility.PinGCObjectAndGetAddress(source, out ulong gcHandle);
                UnsafeUtility.AsRef<ObjectHeader>(ptr) = TypeHeaders<T>.Header;
                UnsafeUtility.ReleaseGCObject(gcHandle);
            }
        }
    }
}
