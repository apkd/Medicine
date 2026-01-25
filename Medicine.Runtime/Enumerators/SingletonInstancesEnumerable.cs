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
        : ILinqFallbackEnumerable<SingletonInstancesEnumerator, Object>
    {
        public SingletonInstancesEnumerator GetEnumerator()
            => new() { DictEnumerator = Storage.Singleton.UntypedAccess.Values.GetEnumerator() };

#if MODULE_ZLINQ
        /// <summary>
        /// Returns a ValueEnumerable struct that allows chaining of ZLinq operators.
        /// </summary>
        public ValueEnumerable<SingletonInstancesEnumerator, Object> AsValueEnumerable()
            => new(GetEnumerator());
#endif
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public struct SingletonInstancesEnumerator
        : IValueEnumerator<Object>
    {
        internal Dictionary<Type, Func<Object?>>.ValueCollection.Enumerator DictEnumerator;

        void IDisposable.Dispose() => DictEnumerator.Dispose();

        public Object Current { get; private set; }

        public bool MoveNext()
        {
            while (DictEnumerator.MoveNext())
            {
                var instance = DictEnumerator.Current();
                if (Utility.IsNativeObjectAlive(instance))
                {
                    Current = instance;
                    return true;
                }
            }

            return false;
        }

        bool IValueEnumerator<Object>.TryGetNext(out Object current)
        {
            if (MoveNext())
            {
                current = Current;
                return true;
            }

            current = null!;
            return false;
        }

        bool IValueEnumerator<Object>.TryGetNonEnumeratedCount(out int count)
        {
            count = 0;
            return false;
        }

        bool IValueEnumerator<Object>.TryGetSpan(out ReadOnlySpan<Object> span)
        {
            span = default;
            return false;
        }

        bool IValueEnumerator<Object>.TryCopyTo(Span<Object> destination, Index offset)
            => false;
    }
}