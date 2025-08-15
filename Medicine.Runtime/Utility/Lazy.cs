#nullable enable
using System;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;
using static System.Runtime.CompilerServices.MethodImplOptions;

namespace Medicine
{
    /// <summary>
    /// Utilities for creating <see cref="LazyRef{T}"/> and <see cref="LazyVal{T}"/>,
    /// efficient wrappers for lazily initialized objects.
    /// </summary>
    public static class Lazy
    {
        public static LazyRef<T> From<T>(Func<T> init) where T : class
            => new(lazy: init);

        // the `in` parameter isn't functional, but it allows us to have two non-competing overloads of the method.
        // the compiler correctly chooses the LazyRef or LazyVal overload based on the generic constraints.
        // magic!
        public static LazyVal<T> From<T>(in Func<T> init) where T : struct
            => new(lazy: init);
    }

    /// <summary>
    /// Implements a lazy reference that defers the initialization of an object until it is accessed.
    /// Designed for situations where the immediate initialization of a reference-type object is
    /// expensive or impossible, and you want to delay the creation of the object until later.
    /// </summary>
    /// <typeparam name="T"> The type of the lazily initialized object. Must be a reference type. </typeparam>
    [DisallowReadonly]
    public struct LazyRef<T> where T : class
    {
        object obj;

        /// <param name="lazy">The delegate used to lazily create the object instance.</param>
        public LazyRef(Func<T> lazy)
            => obj = lazy;

        [MethodImpl(AggressiveInlining)]
        public static implicit operator T(LazyRef<T> lazyRef)
            => lazyRef.Value;

        // not evaluating the func because it seems likely that the struct is
        // boxed for the ToString() call anyway
        public override string ToString()
            => obj switch
            {
                T value => value.ToString(),
                Func<T> => $"LazyRef<{typeof(T).Name}> (unevaluated)",
                _       => $"LazyRef<{typeof(T).Name}> (null)",
            };

        /// <summary>
        /// Gets the lazily initialized object reference.
        /// If the object has not been initialized yet, the delegate provided during construction
        /// will be invoked to obtain the object and cache the reference.
        /// Subsequent accesses will return the cached instance instead of invoking the delegate again.
        /// </summary>
        /// <remarks>
        /// Accessing this property for the first time evaluates the specified lazy initialization
        /// function (if provided), assigns the initialized value to the internal cache, and stores
        /// it for future accesses.
        /// </remarks>
        public T Value
        {
            [MethodImpl(AggressiveInlining)]
            get
            {
                var temp = obj;
                if (temp is Func<T> init)
                {
                    var result = init();
                    obj = result;
                    return result;
                }

                return UnsafeUtility.As<object, T>(ref temp);
            }
        }
    }

    /// <summary>
    /// Implements a lazy reference that defers the initialization of a struct until it is accessed.
    /// Designed for situations where the immediate initialization of a struct value is
    /// expensive or impossible, and you want to delay the initialization until later.
    /// </summary>
    /// <typeparam name="T">The type of the lazily initialized value. Must be a value type.</typeparam>
    [DisallowReadonly]
    public struct LazyVal<T> where T : struct
    {
        T obj;
        Func<T>? init;

        /// <param name="lazy">The delegate used to lazily initialize the struct value.</param>
        public LazyVal(Func<T> lazy)
        {
            obj = default;
            init = lazy;
        }

        [MethodImpl(AggressiveInlining)]
        public static implicit operator T(in LazyVal<T> lazyVal)
            => lazyVal.Value;

        // not evaluating the func because it seems likely that the struct is
        // boxed for the ToString() call anyway
        public override string ToString()
            => init switch
            {
                null => obj.ToString(),
                _    => $"LazyRef<{typeof(T).Name}> (unevaluated)",
            };

        /// <summary>
        /// Gets the lazily initialized value of the struct.
        /// If the value has not yet been initialized, the delegate provided in the constructor
        /// will be invoked to initialize it.
        /// Subsequent accesses will return the cached value without invoking the delegate again.
        /// </summary>
        /// <remarks>
        /// Accessing this property for the first time will evaluate the provided initialization
        /// function (if any), set the value, and then replace the function reference with null
        /// to avoid re-evaluation on future accesses.
        /// </remarks>
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