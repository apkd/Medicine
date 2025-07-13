using System;
using System.Collections.Generic;
using System.ComponentModel;
using static System.ComponentModel.EditorBrowsableState;

namespace Medicine.Internal
{
#if MODULE_ZLINQ
    using ZLinq;
#endif
    [EditorBrowsable(Never)]
    public readonly struct ComponentsEnumerable<T> : ILinqFallbackEnumerable<PooledListEnumerator<T>, T> where T : class
    {
        UnityEngine.Object Target { get; init; }
        Action<UnityEngine.Object, List<T>, bool> SearchFunc { get; init; }
        bool IncludeInactive { get; init; }

        public static ComponentsEnumerable<T> InChildren(UnityEngine.Object target, bool includeInactive)
            => new()
            {
                Target = target,
                IncludeInactive = includeInactive,
                SearchFunc = target is UnityEngine.GameObject
                    ? static (x, list, includeInactive) => (x as UnityEngine.GameObject).GetComponentsInChildren(includeInactive, list)
                    : static (x, list, includeInactive) => (x as UnityEngine.Component).GetComponentsInChildren(includeInactive, list),
            };

        public static ComponentsEnumerable<T> InParents(UnityEngine.Object target, bool includeInactive)
            => new()
            {
                Target = target,
                IncludeInactive = includeInactive,
                SearchFunc = target is UnityEngine.GameObject
                    ? static (x, list, includeInactive) => (x as UnityEngine.GameObject).GetComponentsInParent(includeInactive, list)
                    : static (x, list, includeInactive) => (x as UnityEngine.Component).GetComponentsInParent(includeInactive, list),
            };

        public static ComponentsEnumerable<T> InSelf(UnityEngine.Object target)
            => new()
            {
                Target = target,
                SearchFunc = target is UnityEngine.GameObject
                    ? static (x, list, _) => (x as UnityEngine.GameObject).GetComponents(list)
                    : static (x, list, _) => (x as UnityEngine.Component).GetComponents(list),
            };

        public PooledListEnumerator<T> GetEnumerator()
        {
            var pooled = PooledList.Get<T>(out var list);
            SearchFunc(Target, list, IncludeInactive);
            return new(pooled);
        }

#if MODULE_ZLINQ
        public ValueEnumerable<PooledListEnumerator<T>, T> AsValueEnumerable()
            => new(GetEnumerator());
#endif
    }
}