#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Object = UnityEngine.Object;

namespace Medicine.Internal
{
#if MODULE_ZLINQ
    using ZLinq;
#endif

    [EditorBrowsable(EditorBrowsableState.Never)]
    public struct SingletonInstancesEnumerable
        : ILinqFallbackEnumerable<SingletonInstancesEnumerator, (Type, Object)>
    {
        public SingletonInstancesEnumerator GetEnumerator()
            => new() { DictEnumerator = Storage.Singleton.UntypedAccess.GetEnumerator() };

#if MODULE_ZLINQ
        /// <summary>
        /// Returns a ValueEnumerable struct that allows chaining of ZLinq operators.
        /// </summary>
        public ValueEnumerable<SingletonInstancesEnumerator, (Type, Object)> AsValueEnumerable()
            => new(GetEnumerator());
#endif
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public struct SingletonInstancesEnumerator
        : IValueEnumerator<(Type, Object)>
    {
        internal Dictionary<Type, Func<Object?>>.Enumerator DictEnumerator;

        void IDisposable.Dispose()
            => DictEnumerator.Dispose();

        public (Type, Object) Current { get; private set; }

        public bool MoveNext()
        {
            while (DictEnumerator.MoveNext())
            {
                var current = DictEnumerator.Current;
                var instance = current.Value.Invoke();

                if (Utility.IsNativeObjectDead(instance))
                    continue;

                Current = (current.Key, instance);
                return true;
            }

            return false;
        }

        bool IValueEnumerator<(Type, Object)>.TryGetNext(out (Type, Object) current)
        {
            if (MoveNext())
            {
                current = Current;
                return true;
            }

            current = default;
            return false;
        }

        bool IValueEnumerator<(Type, Object)>.TryGetNonEnumeratedCount(out int count)
        {
            count = 0;
            return false;
        }

        bool IValueEnumerator<(Type, Object)>.TryGetSpan(out ReadOnlySpan<(Type, Object)> span)
        {
            span = default;
            return false;
        }

        bool IValueEnumerator<(Type, Object)>.TryCopyTo(Span<(Type, Object)> destination, Index offset)
            => false;
    }
}