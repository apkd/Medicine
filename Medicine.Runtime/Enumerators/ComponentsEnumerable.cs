using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using static System.ComponentModel.EditorBrowsableState;
using static System.Runtime.CompilerServices.MethodImplOptions;

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

        /// <summary>
        /// Returns the components as a pooled list.
        /// <br/><br/>
        /// Remember to call <c>Dispose()</c> <b>exactly once</b> to return the list to the pool.
        /// </summary>
        [MustDisposeResource]
        [MethodImpl(AggressiveInlining)]
        public PooledList<T> ToPooledList(out List<T> list)
        {
            var handle = PooledList.Get(out list);
            SearchFunc(Target, list, IncludeInactive);
            return handle;
        }

        /// <inheritdoc cref="ToPooledList(out List{T})"/>
        [MustDisposeResource]
        [MethodImpl(AggressiveInlining)]
        public PooledList<T> ToPooledList()
        {
            var handle = PooledList.Get<T>(out var list);
            SearchFunc(Target, list, IncludeInactive);
            return handle;
        }

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