using System;
using System.Runtime.CompilerServices;
using Medicine.Internal;
using static System.Runtime.CompilerServices.MethodImplOptions;

namespace Medicine
{
#if MODULE_ZLINQ
    using MedicineUnsafeShim = Unsafe;
#endif

    /// <summary>
    /// Utilities for creating <see cref="LazyRef{T}"/> and <see cref="LazyVal{T}"/>,
    /// efficient wrappers for lazily initialized objects.
    /// </summary>
    public static class Lazy
    {
        public static LazyRef<T> From<T>(Func<T> init) where T : class
            => new(lazy: init);

        public static LazyVal<T> From<T>(in Func<T> init) where T : struct
            => new(lazy: init);
    }

    [DisallowReadonly]
    public struct LazyRef<T> where T : class
    {
        object obj;

        public LazyRef(Func<T> lazy)
            => obj = lazy;

        [MethodImpl(AggressiveInlining)]
        public static implicit operator T(LazyRef<T> lazyRef)
            => lazyRef.Value;

        public T Value
        {
            [MethodImpl(AggressiveInlining)]
            get
            {
                switch (obj)
                {
                    case T value:
                        return value;
                    case null:
                        return null;
                    case Func<T> init:
                        var result = init();
                        obj = result;
                        return result;
                    default:
#if DEBUG
                        throw new InvalidOperationException($"Unexpected object type: {obj.GetType().Name} vs {typeof(T).Name}");
#else
                            return null;
#endif
                }
            }
        }
    }

    [DisallowReadonly]
    public struct LazyVal<T> where T : struct
    {
        T obj;
        Func<T> init;

        public LazyVal(Func<T> lazy)
        {
            MedicineUnsafeShim.SkipInit(out obj);
            this.init = lazy;
        }

        [MethodImpl(AggressiveInlining)]
        public static implicit operator T(in LazyVal<T> lazyVal)
            => lazyVal.Value;

        public T Value
        {
            [MethodImpl(AggressiveInlining)]
            get
            {
                switch (init)
                {
                    case null:
                        return obj;
                    default:
                        obj = init();
                        init = null;
                        return obj;
                }
            }
        }
    }
}