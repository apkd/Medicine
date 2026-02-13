using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using static System.Runtime.CompilerServices.MethodImplOptions;

namespace Medicine.Internal
{
    [NativeContainer]
    [NativeContainerSupportsMinMaxWriteRestriction]
    public unsafe struct UnmanagedAccessArray<TClass, TLayout, TAccessRW, TAccessRO> : IDisposable
        where TLayout : unmanaged
        where TAccessRW : unmanaged
        where TAccessRO : unmanaged
        where TClass : class
    {
        const int PrefetchDistance = 8;

        static readonly SharedStatic<TLayout> unmanagedLayoutStorage
            = SharedStatic<TLayout>.GetOrCreate<TLayout>();

        UnsafeList<UnmanagedRef<TClass>> classRefArray;
        int rangeStart;
        int rangeLength;

        [NativeDisableUnsafePtrRestriction]
        readonly TLayout* layoutInfo;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        int m_Length;
        int m_MinIndex;
        int m_MaxIndex;

        AtomicSafetyHandle m_Safety;

        static readonly SharedStatic<int> s_staticSafetyId
            = SharedStatic<int>.GetOrCreate<UnmanagedAccessArray<TClass, TLayout, TAccessRW, TAccessRO>>();
#endif

        public readonly int Length
        {
            [MethodImpl(AggressiveInlining)]
            get => rangeLength;
        }

        public readonly bool IsCreated
        {
            [MethodImpl(AggressiveInlining)]
            get => classRefArray.IsCreated;
        }

        public void UpdateBuffer(UnsafeList<UnmanagedRef<TClass>> classRefArray)
        {
            this.classRefArray = classRefArray;
            rangeStart = 0;
            rangeLength = classRefArray.Length;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Length = classRefArray.Length;
            m_MinIndex = 0;
            m_MaxIndex = m_Length - 1;
#endif
        }

        public UnmanagedAccessArray(UnsafeList<UnmanagedRef<TClass>> classRefArray)
        {
            this.classRefArray = classRefArray;
            rangeStart = 0;
            rangeLength = classRefArray.Length;
            layoutInfo = (TLayout*)unmanagedLayoutStorage.UnsafeDataPointer;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Length = classRefArray.Length;
            m_MinIndex = 0;
            m_MaxIndex = m_Length - 1;
            m_Safety = AtomicSafetyHandle.Create();
            CollectionHelper.SetStaticSafetyId<UnmanagedAccessArray<TClass, TLayout, TAccessRW, TAccessRO>>(ref m_Safety, ref s_staticSafetyId.Data);
            AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(m_Safety, value: true);
#endif
        }

        UnmanagedAccessArray(
            UnsafeList<UnmanagedRef<TClass>> classRefArray,
            TLayout* layoutInfo,
            int rangeStart,
            int rangeLength
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            , int length,
            int minIndex,
            int maxIndex,
            AtomicSafetyHandle safety
#endif
        )
        {
            this.classRefArray = classRefArray;
            this.layoutInfo = layoutInfo;
            this.rangeStart = rangeStart;
            this.rangeLength = rangeLength;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Length = length;
            m_MinIndex = minIndex;
            m_MaxIndex = maxIndex;
            m_Safety = safety;
#endif
        }

        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (AtomicSafetyHandle.IsValidNonDefaultHandle(m_Safety))
            {
                AtomicSafetyHandle.CheckDeallocateAndThrow(m_Safety);
                AtomicSafetyHandle.Release(m_Safety);
            }
#endif
            classRefArray = default;
        }

        public readonly JobHandle Dispose(JobHandle dependsOn)
            => new DisposeJob(this).Schedule(dependsOn);

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        [MethodImpl(AggressiveInlining)]
        readonly void CheckIndexInRange(int index)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if ((uint)index >= (uint)rangeLength)
                ThrowIndexOutOfRange(index);
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        [MethodImpl(AggressiveInlining)]
        readonly void CheckAbsoluteIndexInRange(int index)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if ((uint)index >= (uint)classRefArray.Length)
                ThrowAbsoluteIndexOutOfRange(index);
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        [MethodImpl(AggressiveInlining)]
        readonly void CheckIndexInParallelWriteRange(int index)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (index < m_MinIndex || index > m_MaxIndex)
                ThrowIndexOutOfWriteRange(index);
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        [MethodImpl(AggressiveInlining)]
        readonly void CheckWriteAccess()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        [MethodImpl(NoInlining)]
        readonly void ThrowIndexOutOfRange(int index)
            => throw new IndexOutOfRangeException($"Index {index} is out of range of [0..{rangeLength - 1}].");

        [MethodImpl(NoInlining)]
        readonly void ThrowAbsoluteIndexOutOfRange(int index)
            => throw new IndexOutOfRangeException($"Index {index} is out of range of [0..{m_Length - 1}].");

        [MethodImpl(NoInlining)]
        readonly void ThrowIndexOutOfWriteRange(int index)
            => throw new IndexOutOfRangeException($"Index {index} is outside the permitted write range [{m_MinIndex}..{m_MaxIndex}] for this job.");
#endif

        struct DisposeJob : IJob
        {
            [NativeDisableUnsafePtrRestriction] UnmanagedAccessArray<TClass, TLayout, TAccessRW, TAccessRO> array;

            public DisposeJob(UnmanagedAccessArray<TClass, TLayout, TAccessRW, TAccessRO> array)
                => this.array = array;

            void IJob.Execute() => array.Dispose();
        }

        readonly struct UntypedAccess
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

        public readonly TAccessRW this[int index]
        {
            [MethodImpl(AggressiveInlining)]
            get
            {
                CheckIndexInRange(index);
                return AccessAt(rangeStart + index);
            }
        }

        public readonly UnmanagedAccessArray<TClass, TLayout, TAccessRW, TAccessRO> this[Range range]
        {
            [MethodImpl(AggressiveInlining)]
            get
            {
                var (offset, length) = range.GetOffsetAndLength(rangeLength);
                return new(
                    classRefArray,
                    layoutInfo,
                    rangeStart + offset,
                    length
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    , m_Length,
                    m_MinIndex,
                    m_MaxIndex,
                    m_Safety
#endif
                );
            }
        }

        [MethodImpl(AggressiveInlining)]
        readonly TAccessRW AccessAt(int absoluteIndex)
        {
            CheckWriteAccess();
            CheckAbsoluteIndexInRange(absoluteIndex);
            CheckIndexInParallelWriteRange(absoluteIndex);
            var access = new UntypedAccess(classRefArray[absoluteIndex], layoutInfo);
            return UnsafeUtility.As<UntypedAccess, TAccessRW>(ref access);
        }

        public readonly Enumerator GetEnumerator()
            => new(this);

        public readonly ReadOnly AsReadOnly()
            => new(
                classRefArray, layoutInfo, rangeStart, rangeLength
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                , m_Safety
#endif
            );

        [DisallowReadonly]
        public struct Enumerator
        {
            readonly UnmanagedAccessArray<TClass, TLayout, TAccessRW, TAccessRO> array;
            int index;
            readonly int endExclusive;

            public Enumerator(UnmanagedAccessArray<TClass, TLayout, TAccessRW, TAccessRO> array)
            {
                this.array = array;
                index = array.rangeStart - 1;
                endExclusive = array.rangeStart + array.rangeLength;

                array.CheckWriteAccess();

#if MODULE_BURST && UNITY_BURST_EXPERIMENTAL_PREFETCH_INTRINSIC && !MEDICINE_DISABLE_UNMANAGEDACCESSARRAY_PREFETCH
                var len = array.rangeLength;
                if (len > 0)
                {
                    int count = len < PrefetchDistance ? len : PrefetchDistance;
                    for (int i = 0; i < count; i++)
                        Common.Prefetch((void*)array.classRefArray.Ptr[array.rangeStart + i].Ptr, Common.ReadWrite.Write);
                }
#endif
            }

            [MethodImpl(AggressiveInlining)]
            public bool MoveNext()
            {
                array.CheckWriteAccess();

                if (++index >= endExclusive)
                    return false;

#if MODULE_BURST && UNITY_BURST_EXPERIMENTAL_PREFETCH_INTRINSIC && !MEDICINE_DISABLE_UNMANAGEDACCESSARRAY_PREFETCH
                int prefetchOffset = index + PrefetchDistance;
                if ((uint)prefetchOffset < (uint)endExclusive)
                    Common.Prefetch((void*)array.classRefArray.Ptr[prefetchOffset].Ptr, Common.ReadWrite.Write);
#endif

                return true;
            }

            public TAccessRW Current
            {
                [MethodImpl(AggressiveInlining)]
                get => array.AccessAt(index);
            }
        }

        [NativeContainer]
        [NativeContainerIsReadOnly]
        public readonly struct ReadOnly
        {
            readonly UnsafeList<UnmanagedRef<TClass>> classRefArray;
            readonly int rangeStart;
            readonly int rangeLength;

            [NativeDisableUnsafePtrRestriction]
            readonly TLayout* layoutInfo;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            readonly AtomicSafetyHandle m_Safety;
#endif

            public int Length
            {
                [MethodImpl(AggressiveInlining)]
                get => rangeLength;
            }

            public bool IsCreated
            {
                [MethodImpl(AggressiveInlining)]
                get => classRefArray.IsCreated;
            }

            public ReadOnly(
                UnsafeList<UnmanagedRef<TClass>> classRefArray,
                TLayout* layoutInfo,
                int rangeStart,
                int rangeLength
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                , AtomicSafetyHandle safety
#endif
            )
            {
                this.classRefArray = classRefArray;
                this.layoutInfo = layoutInfo;
                this.rangeStart = rangeStart;
                this.rangeLength = rangeLength;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = safety;
#endif
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            [MethodImpl(AggressiveInlining)]
            void CheckIndexInRange(int index)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if ((uint)index >= (uint)rangeLength)
                    ThrowIndexOutOfRange(index);
#endif
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            [MethodImpl(AggressiveInlining)]
            void CheckAbsoluteIndexInRange(int index)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if ((uint)index >= (uint)classRefArray.Length)
                    ThrowAbsoluteIndexOutOfRange(index);
#endif
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            [MethodImpl(AggressiveInlining)]
            void CheckReadAccess()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            [MethodImpl(NoInlining)]
            void ThrowIndexOutOfRange(int index)
                => throw new IndexOutOfRangeException($"Index {index} is out of range of [0..{rangeLength - 1}].");

            [MethodImpl(NoInlining)]
            void ThrowAbsoluteIndexOutOfRange(int index)
                => throw new IndexOutOfRangeException($"Index {index} is out of range of [0..{classRefArray.Length - 1}].");
#endif

            public TAccessRO this[int index]
            {
                [MethodImpl(AggressiveInlining)]
                get
                {
                    CheckIndexInRange(index);
                    return AccessAt(rangeStart + index);
                }
            }

            public ReadOnly this[Range range]
            {
                [MethodImpl(AggressiveInlining)]
                get
                {
                    var (offset, length) = range.GetOffsetAndLength(rangeLength);
                    return new(
                        classRefArray,
                        layoutInfo,
                        rangeStart + offset,
                        length
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                        , m_Safety
#endif
                    );
                }
            }

            [MethodImpl(AggressiveInlining)]
            TAccessRO AccessAt(int absoluteIndex)
            {
                CheckReadAccess();
                CheckAbsoluteIndexInRange(absoluteIndex);
                var access = new UntypedAccess(classRefArray[absoluteIndex], layoutInfo);
                return UnsafeUtility.As<UntypedAccess, TAccessRO>(ref access);
            }

            public Enumerator GetEnumerator()
                => new(this);

            [DisallowReadonly]
            [SuppressMessage("ReSharper", "MemberHidesStaticFromOuterClass")]
            public struct Enumerator
            {
                readonly ReadOnly array;
                int index;
                readonly int endExclusive;

                public Enumerator(in ReadOnly array)
                {
                    this.array = array;
                    index = array.rangeStart - 1;
                    endExclusive = array.rangeStart + array.rangeLength;

#if MODULE_BURST && UNITY_BURST_EXPERIMENTAL_PREFETCH_INTRINSIC && !MEDICINE_DISABLE_UNMANAGEDACCESSARRAY_PREFETCH
                    var len = array.rangeLength;
                    if (len > 0)
                    {
                        int count = len < PrefetchDistance ? len : PrefetchDistance;
                        for (int i = 0; i < count; i++)
                            Common.Prefetch((void*)array.classRefArray.Ptr[array.rangeStart + i].Ptr, Common.ReadWrite.Read);
                    }
#endif
                }

                [MethodImpl(AggressiveInlining)]
                public bool MoveNext()
                {
                    array.CheckReadAccess();

                    if (++index >= endExclusive)
                        return false;

#if MODULE_BURST && UNITY_BURST_EXPERIMENTAL_PREFETCH_INTRINSIC && !MEDICINE_DISABLE_UNMANAGEDACCESSARRAY_PREFETCH
                    int prefetchOffset = index + PrefetchDistance;
                    if ((uint)prefetchOffset < (uint)endExclusive)
                        Common.Prefetch((void*)array.classRefArray.Ptr[prefetchOffset].Ptr, Common.ReadWrite.Read);
#endif

                    return true;
                }

                public TAccessRO Current
                {
                    [MethodImpl(AggressiveInlining)]
                    get => array.AccessAt(index);
                }
            }
        }
    }
}
