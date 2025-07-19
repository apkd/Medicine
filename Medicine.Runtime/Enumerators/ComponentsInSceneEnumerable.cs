#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.SceneManagement;
using static System.ComponentModel.EditorBrowsableState;
using static System.Runtime.CompilerServices.MethodImplOptions;

namespace Medicine.Internal
{
#if MODULE_ZLINQ
    using ZLinq;
    using MedicineUnsafeShim = Unsafe;
#endif

    [EditorBrowsable(Never)]
    public readonly struct ComponentsInSceneEnumerable<T> : ILinqFallbackEnumerable<ComponentsInSceneEnumerator<T>, T>
        where T : class
    {
        readonly Scene scene;
        readonly bool includeInactive;

        public ComponentsInSceneEnumerable(Scene scene, bool includeInactive)
            => (this.scene, this.includeInactive) = (scene, includeInactive);

        [EditorBrowsable(Never)]
        public ComponentsInSceneEnumerator<T> GetEnumerator()
            => new(scene, includeInactive);

#if MODULE_ZLINQ
        /// <summary>
        /// Returns a ValueEnumerable struct that allows chaining of ZLinq operators.
        /// </summary>
        public ValueEnumerable<ComponentsInSceneEnumerator<T>, T> AsValueEnumerable()
            => new(GetEnumerator());
#endif
    }

    [DisallowReadonly]
    [EditorBrowsable(Never)]
    [StructLayout(LayoutKind.Auto)]
    public struct ComponentsInSceneEnumerator<T> : IDisposable, IValueEnumerator<T>
        where T : class
    {
        readonly List<T> componentList;
        readonly bool includeInactive;
        PooledList<GameObject> rootListDisposable;
        PooledList<T> componentListDisposable;
        ListEnumerator<GameObject> rootEnumerator;
        ListEnumerator<T> componentEnumerator;

        public ComponentsInSceneEnumerator(Scene scene, bool includeInactive)
        {
            Assert.IsTrue(scene.IsValid());
            rootListDisposable = PooledList.Get<GameObject>(out var rootList);
            componentListDisposable = PooledList.Get(out componentList);
            scene.GetRootGameObjects(rootList);
            rootEnumerator = new(rootList);
            componentEnumerator = default;
            this.includeInactive = includeInactive;
        }

        [MethodImpl(AggressiveInlining)]
        public bool MoveNext()
        {
            // try to move to the next component
            while (!componentEnumerator.MoveNext())
            {
                // no components left; try to move to the next root
                do
                {
                    if (!rootEnumerator.MoveNext())
                        return false; // no more roots; done iterating
                }
                while (!includeInactive && !rootEnumerator.Current.activeInHierarchy);

                // get components from root
                rootEnumerator.Current.GetComponentsInChildren(includeInactive, componentList);
                componentEnumerator = new(componentList);
            }

            // found the next component
            return true;
        }

        public ref readonly T Current
        {
            [MethodImpl(AggressiveInlining)]
            get => ref componentEnumerator.Current;
        }

        void IDisposable.Dispose()
        {
            rootListDisposable.Dispose();
            componentListDisposable.Dispose();
        }

        bool IValueEnumerator<T>.TryGetNext(out T current)
        {
            if (MoveNext())
            {
                current = Current;
                return true;
            }

            MedicineUnsafeShim.SkipInit(out current);
            return false;
        }

        bool IValueEnumerator<T>.TryGetNonEnumeratedCount(out int count)
        {
            MedicineUnsafeShim.SkipInit(out count);
            return false;
        }

        bool IValueEnumerator<T>.TryGetSpan(out ReadOnlySpan<T> span)
        {
            span = default;
            return false;
        }

        bool IValueEnumerator<T>.TryCopyTo(Span<T> destination, Index offset)
            => false;
    }
}