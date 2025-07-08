#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using Medicine.Internal;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Pool;
using static System.Runtime.CompilerServices.MethodImplOptions;

namespace Medicine
{
    public struct PooledList<T> : IDisposable
        where T : class
    {
        public readonly List<T> List;
        PooledObject<List<T>> disposable;

        internal PooledList(List<T> list, PooledObject<List<T>> disposable)
        {
            this.List = list;
            this.disposable = disposable;
        }

        public void Dispose()
            => PooledList.Dispose(List, ref disposable);
    }

    public static class PooledList
    {
        public static PooledList<T> Get<T>(out List<T> list)
            where T : class
        {
            // we can save some memory by sharing the same pooled lists for all object types :see_no_evil:
            // - various Unity APIs expect a typed list, which inflates the number of pool
            // - the UnsafeUtility.As<T1, T2> cast is actually enough this to work on Mono, but IL2CPP
            //   will crash unless we actually modify the object header so that it properly mimics the target type
            // - ListPool always clears the list when returned to the pool, and we always restore the type back to
            //   the original type, so it should be fine to release the lists to the global shared pool
            // - set MEDICINE_NO_FUNSAFE to disable this optimization and fall back to normal pool usage
#if MEDICINE_NO_FUNSAFE
            var disposable = ListPool<T>.Get(out var list);
#else
            var disposableGeneric = ListPool<object>.Get(out var listGeneric);
            var disposable = UnsafeUtility.As<PooledObject<List<object>>, PooledObject<List<T>>>(ref disposableGeneric);
            var array = listGeneric.AsInternalsView().Array;
            SetManagedObjectType<T[]>(array);
            list = SetManagedObjectType<List<T>>(listGeneric)!;
#endif
            return new(list, disposable);
        }

        [MethodImpl(AggressiveInlining)]
        public static PooledList<T> Get<T>() where T : class
            => Get<T>(out _);

        readonly struct PooledObjectData
        {
            readonly object obj;

            public bool IsDisposed
                => obj is null;
        }

        internal static void Dispose<T>(List<T> list, ref PooledObject<List<T>> disposable)
        {
            if (UnsafeUtility.As<PooledObject<List<T>>, PooledObjectData>(ref disposable).IsDisposed)
                return;

#if MEDICINE_NO_FUNSAFE
            disposable.InvokeDispose();
#else
            var array = list.AsInternalsView().Array;
            SetManagedObjectType<object[]>(array);
            SetManagedObjectType<List<object>>(list);
            UnsafeUtility.As<PooledObject<List<T>>, PooledObject<List<object>>>(ref disposable).InvokeDispose();
#endif
            disposable = default;
        }

        /// <summary> Sets the type of managed object. </summary>
        [MethodImpl(AggressiveInlining)]
        static unsafe T? SetManagedObjectType<T>(object? obj) where T : class
        {
            if (obj is null)
                return null;

            var ptr = UnsafeUtility.PinGCObjectAndGetAddress(obj, out ulong gcHandle);
            UnsafeUtility.AsRef<ObjectHeader>(ptr) = TypeHeaders<T>.Header;
            UnsafeUtility.ReleaseGCObject(gcHandle);
            return obj as T;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ObjectHeader
        {
            readonly IntPtr data0;
            readonly IntPtr data1;
        }

        static unsafe class TypeHeaders<T>
        {
            public static readonly ObjectHeader Header = Make();

            static ObjectHeader Make()
            {
                // create a temporary instance of a managed type to read the type header
                // this is done once per type in the lifetime of the program

                var tempInstance = typeof(T).IsArray
                    // create an array of length 0
                    ? Array.CreateInstance(typeof(T).GetElementType()!, 0)
                    // create an object instance without calling the ctor
                    : FormatterServices.GetUninitializedObject(typeof(T));

                var ptr = UnsafeUtility.PinGCObjectAndGetAddress(tempInstance, out ulong gcHandle);
                var value = UnsafeUtility.AsRef<ObjectHeader>(ptr);
                UnsafeUtility.ReleaseGCObject(gcHandle);
                return value;
            }
        }
    }
}