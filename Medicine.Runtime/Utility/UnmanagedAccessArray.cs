using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using static System.Runtime.CompilerServices.MethodImplOptions;

namespace Medicine.Internal
{
    [NativeContainer]
    public unsafe struct UnmanagedAccessArray<TClass, TLayout, TAccess> : IDisposable
        where TLayout : unmanaged
        where TAccess : unmanaged
        where TClass : class
    {
        static readonly SharedStatic<TLayout> unmanagedLayoutStorage
            = SharedStatic<TLayout>.GetOrCreate<TLayout>();

        UnsafeList<UnmanagedRef<TClass>> classRefArray;

        [NativeDisableUnsafePtrRestriction]
        readonly TLayout* layoutInfo;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle m_Safety;
        DisposeSentinel m_DisposeSentinel;

        enum SafetyIdKey { }
        static readonly SharedStatic<int> staticSafetyId = SharedStatic<int>.GetOrCreate<int, SafetyIdKey>();
#endif

        public int Length
        {
            [MethodImpl(AggressiveInlining)]
            get => classRefArray.Length;
        }

        public bool IsCreated
        {
            [MethodImpl(AggressiveInlining)]
            get => classRefArray.IsCreated;
        }

        public UnmanagedAccessArray(UnsafeList<UnmanagedRef<TClass>> classRefArray)
        {
            this.classRefArray = classRefArray;
            layoutInfo = (TLayout*)unmanagedLayoutStorage.UnsafeDataPointer;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Create(out m_Safety, out m_DisposeSentinel, 1, Allocator.TempJob);

            if (staticSafetyId.Data is 0)
                staticSafetyId.Data = AtomicSafetyHandle.NewStaticSafetyId<UnmanagedAccessArray<TClass, TLayout, TAccess>>();

            AtomicSafetyHandle.SetStaticSafetyId(ref m_Safety, staticSafetyId.Data);
#endif
        }

        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Dispose(ref m_Safety, ref m_DisposeSentinel);
#endif
            classRefArray = default;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        [MethodImpl(AggressiveInlining)]
        void CheckWriteAccess()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        [MethodImpl(AggressiveInlining)]
        void CheckIndexInRange(int index)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if ((uint)index >= (uint)classRefArray.Length)
                throw new IndexOutOfRangeException($"Index {index} is out of range [0..{classRefArray.Length - 1}].");
#endif
        }

        struct UntypedAccess
        {
            [UsedImplicitly] readonly UnmanagedRef<TClass> classRef;
            [UsedImplicitly] readonly void* layoutInfo;

            [MethodImpl(AggressiveInlining)]
            public UntypedAccess(UnmanagedRef<TClass> classRef, void* layoutInfo)
            {
                this.classRef = classRef;
                this.layoutInfo = layoutInfo;
            }
        }

        public TAccess this[int index]
        {
            [MethodImpl(AggressiveInlining)]
            get
            {
                CheckWriteAccess();
                CheckIndexInRange(index);

                var access = new UntypedAccess(classRefArray[index], layoutInfo);
                return UnsafeUtility.As<UntypedAccess, TAccess>(ref access);
            }
        }

        public Enumerator GetEnumerator() => new(this);

        public struct Enumerator
        {
            const int PrefetchDistance = 4;

            readonly UnmanagedAccessArray<TClass, TLayout, TAccess> array;
            int index;

            public Enumerator(UnmanagedAccessArray<TClass, TLayout, TAccess> array)
            {
                this.array = array;
                index = -1;

                array.CheckWriteAccess();

#if MODULE_BURST && UNITY_BURST_EXPERIMENTAL_PREFETCH_INTRINSIC
                var len = array.classRefArray.Length;
                if (len > 0)
                {
                    int count = len < PrefetchDistance ? len : PrefetchDistance;
                    for (int i = 0; i < count; i++)
                        Common.Prefetch((void*)array.classRefArray.Ptr[i].Ptr, Common.ReadWrite.Read, Common.Locality.LowTemporalLocality);
                }
#endif
            }

            [MethodImpl(AggressiveInlining)]
            public bool MoveNext()
            {
                array.CheckWriteAccess();

                index++;

                if (index >= array.classRefArray.Length)
                    return false;

#if MODULE_BURST && UNITY_BURST_EXPERIMENTAL_PREFETCH_INTRINSIC
                int prefetchOffset = index + PrefetchDistance;
                if ((uint)prefetchOffset < (uint)array.classRefArray.Length)
                    Common.Prefetch((void*)array.classRefArray.Ptr[prefetchOffset].Ptr, Common.ReadWrite.Read, Common.Locality.LowTemporalLocality);
#endif

                return true;
            }

            public TAccess Current
            {
                [MethodImpl(AggressiveInlining)]
                get => array[index];
            }
        }
    }
}