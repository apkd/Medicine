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
        enum SearchMode : byte
        {
            Self,
            Parents,
            Children,
        }

        UnityEngine.Object Target { get; init; }
        SearchMode Mode { get; init; }
        bool IncludeInactive { get; init; }

        public static ComponentsEnumerable<T> InChildren(UnityEngine.Object target, bool includeInactive)
            => new()
            {
                Target = target,
                IncludeInactive = includeInactive,
                Mode = SearchMode.Children,
            };

        public static ComponentsEnumerable<T> InParents(UnityEngine.Object target, bool includeInactive)
            => new()
            {
                Target = target,
                IncludeInactive = includeInactive,
                Mode = SearchMode.Parents,
            };

        public static ComponentsEnumerable<T> InSelf(UnityEngine.Object target)
            => new()
            {
                Target = target,
                Mode = SearchMode.Self,
            };

        [MethodImpl(AggressiveInlining)]
        void Search(List<T> list)
        {
            if (Target is UnityEngine.GameObject gameObject)
            {
                switch (Mode)
                {
                    case SearchMode.Self:
                        gameObject.GetComponents(list);
                        return;
                    case SearchMode.Parents:
                        gameObject.GetComponentsInParent(IncludeInactive, list);
                        return;
                    case SearchMode.Children:
                        gameObject.GetComponentsInChildren(IncludeInactive, list);
                        return;
                }
            }

            var component = Target as UnityEngine.Component;
            switch (Mode)
            {
                case SearchMode.Self:
                    component.GetComponents(list);
                    return;
                case SearchMode.Parents:
                    component.GetComponentsInParent(IncludeInactive, list);
                    return;
                case SearchMode.Children:
                    component.GetComponentsInChildren(IncludeInactive, list);
                    return;
            }
        }

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
            Search(list);
            return handle;
        }

        /// <inheritdoc cref="ToPooledList(out List{T})"/>
        [MustDisposeResource]
        [MethodImpl(AggressiveInlining)]
        public PooledList<T> ToPooledList()
        {
            var handle = PooledList.Get<T>(out var list);
            Search(list);
            return handle;
        }

        public PooledListEnumerator<T> GetEnumerator()
        {
            var pooled = PooledList.Get<T>(out var list);
            Search(list);
            return new(pooled);
        }

#if MODULE_ZLINQ
        public ValueEnumerable<PooledListEnumerator<T>, T> AsValueEnumerable()
            => new(GetEnumerator());
#endif
    }
}
