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
    public unsafe struct UnmanagedAccessArray<TClass, TLayout, TAccess, TAccessRO> : IDisposable
        where TLayout : unmanaged
        where TAccess : unmanaged
        where TAccessRO : unmanaged
        where TClass : class
    {
        static readonly SharedStatic<TLayout> unmanagedLayoutStorage
            = SharedStatic<TLayout>.GetOrCreate<TLayout>();

        UnsafeList<UnmanagedRef<TClass>> classRefArray;

        [NativeDisableUnsafePtrRestriction]
        readonly TLayout* layoutInfo;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        int m_Length;
        int m_MinIndex;
        int m_MaxIndex;

        AtomicSafetyHandle m_Safety;

        static readonly SharedStatic<int> s_staticSafetyId
            = SharedStatic<int>.GetOrCreate<UnmanagedAccessArray<TClass, TLayout, TAccess, TAccessRO>>();
#endif

        public readonly int Length
        {
            [MethodImpl(AggressiveInlining)]
            get => classRefArray.Length;
        }

        public readonly bool IsCreated
        {
            [MethodImpl(AggressiveInlining)]
            get => classRefArray.IsCreated;
        }

        public void UpdateBuffer(UnsafeList<UnmanagedRef<TClass>> classRefArray)
        {
            this.classRefArray = classRefArray;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Length = classRefArray.Length;
            m_MinIndex = 0;
            m_MaxIndex = m_Length - 1;
#endif
        }

        public UnmanagedAccessArray(UnsafeList<UnmanagedRef<TClass>> classRefArray)
        {
            this.classRefArray = classRefArray;
            layoutInfo = (TLayout*)unmanagedLayoutStorage.UnsafeDataPointer;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Length = classRefArray.Length;
            m_MinIndex = 0;
            m_MaxIndex = m_Length - 1;
            m_Safety = CollectionHelper.CreateSafetyHandle(Allocator.TempJob);
            CollectionHelper.SetStaticSafetyId<UnmanagedAccessArray<TClass, TLayout, TAccess, TAccessRO>>(ref m_Safety, ref s_staticSafetyId.Data);
            AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(m_Safety, value: true);
#endif
        }

        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (AtomicSafetyHandle.IsValidNonDefaultHandle(m_Safety))
                CollectionHelper.DisposeSafetyHandle(ref m_Safety);
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
            if ((uint)index >= (uint)classRefArray.Length)
                ThrowIndexOutOfRange(index);
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
        readonly void CheckReadAccess()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        [MethodImpl(NoInlining)]
        readonly void ThrowIndexOutOfRange(int index)
            => throw new IndexOutOfRangeException($"Index {index} is out of range of [0..{m_Length - 1}].");

        [MethodImpl(NoInlining)]
        readonly void ThrowIndexOutOfWriteRange(int index)
            => throw new IndexOutOfRangeException($"Index {index} is outside the permitted write range [{m_MinIndex}..{m_MaxIndex}] for this job.");
#endif

        struct DisposeJob : IJob
        {
            [NativeDisableUnsafePtrRestriction] UnmanagedAccessArray<TClass, TLayout, TAccess, TAccessRO> array;

            public DisposeJob(UnmanagedAccessArray<TClass, TLayout, TAccess, TAccessRO> array)
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

        public readonly TAccess this[int index]
        {
            [MethodImpl(AggressiveInlining)]
            get
            {
                CheckReadAccess();
                CheckIndexInRange(index);
                CheckIndexInParallelWriteRange(index);

                var access = new UntypedAccess(classRefArray[index], layoutInfo);
                return UnsafeUtility.As<UntypedAccess, TAccess>(ref access);
            }
        }

        public readonly Enumerator GetEnumerator()
            => new(this);

        public readonly ReadOnly AsReadOnly()
            => new(classRefArray, layoutInfo, m_Safety);

        [DisallowReadonly]
        public struct Enumerator
        {
            const int PrefetchDistance = 4;

            readonly UnmanagedAccessArray<TClass, TLayout, TAccess, TAccessRO> array;
            int index;

            public Enumerator(UnmanagedAccessArray<TClass, TLayout, TAccess, TAccessRO> array)
            {
                this.array = array;
                index = -1;

                array.CheckReadAccess();

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
                array.CheckReadAccess();

                if (++index >= array.classRefArray.Length)
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

        [NativeContainerIsReadOnly]
        public readonly struct ReadOnly
        {
            readonly UnsafeList<UnmanagedRef<TClass>> classRefArray;

            [NativeDisableUnsafePtrRestriction]
            readonly TLayout* layoutInfo;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            readonly AtomicSafetyHandle m_Safety;
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

            public ReadOnly(
                UnsafeList<UnmanagedRef<TClass>> classRefArray, TLayout* layoutInfo
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                , AtomicSafetyHandle safety
#endif
            )
            {
                this.classRefArray = classRefArray;
                this.layoutInfo = layoutInfo;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = safety;
#endif
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            [MethodImpl(AggressiveInlining)]
            void CheckIndexInRange(int index)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if ((uint)index >= (uint)classRefArray.Length)
                    ThrowIndexOutOfRange(index);
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
                => throw new IndexOutOfRangeException($"Index {index} is out of range of [0..{classRefArray.Length - 1}].");
#endif

            public TAccessRO this[int index]
            {
                [MethodImpl(AggressiveInlining)]
                get
                {
                    CheckReadAccess();
                    CheckIndexInRange(index);

                    var access = new UntypedAccess(classRefArray[index], layoutInfo);
                    return UnsafeUtility.As<UntypedAccess, TAccessRO>(ref access);
                }
            }

            public Enumerator GetEnumerator()
                => new(this);

            [DisallowReadonly]
            [SuppressMessage("ReSharper", "MemberHidesStaticFromOuterClass")]
            public struct Enumerator
            {
                const int PrefetchDistance = 4;

                readonly ReadOnly array;
                int index;

                public Enumerator(ReadOnly array)
                {
                    this.array = array;
                    index = -1;

#if MODULE_BURST && UNITY_BURST_EXPERIMENTAL_PREFETCH_INTRINSIC
                    var len = array.Length;
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
                    array.CheckReadAccess();

                    if (++index >= array.classRefArray.Length)
                        return false;

#if MODULE_BURST && UNITY_BURST_EXPERIMENTAL_PREFETCH_INTRINSIC
                    int prefetchOffset = index + PrefetchDistance;
                    if ((uint)prefetchOffset < (uint)array.classRefArray.Length)
                        Common.Prefetch((void*)array.classRefArray.Ptr[prefetchOffset].Ptr, Common.ReadWrite.Read, Common.Locality.LowTemporalLocality);
#endif

                    return true;
                }

                public TAccessRO Current
                {
                    [MethodImpl(AggressiveInlining)]
                    get => array[index];
                }
            }
        }
    }
}