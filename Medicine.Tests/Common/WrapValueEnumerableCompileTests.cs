#if MODULE_ZLINQ
using System;
using Medicine;
using NUnit.Framework;
using UnityEngine;
using ZLinq;

public partial class WrapValueEnumerableCompileTests
{
    [Track]
    public partial class SomeTracked : MonoBehaviour { }

    [Singleton]
    public partial class SomeSingleton : MonoBehaviour { }

    public static partial class MethodExpressionBody
    {
        [WrapValueEnumerable]
        public static Query1 Enemies()
            => Array.Empty<int>()
                .AsValueEnumerable()
                .Select(x => x);
    }

    public static partial class PropertyExpressionBody
    {
        [WrapValueEnumerable]
        public static Query2 Enemies
            => Array.Empty<int>()
                .AsValueEnumerable()
                .Select(x => x);
    }

    public static partial class MethodBlockBody
    {
        [WrapValueEnumerable]
        public static Query3 Enemies(int[] source)
        {
            var query = source
                .AsValueEnumerable()
                .Select(x => x + 1);

            return query;
        }
    }

    public static partial class GenericContainingType<T>
    {
        [WrapValueEnumerable]
        public static Query4 All(T[] source)
            => source
                .AsValueEnumerable()
                .Select(x => x);
    }

    public static partial class ContainingTrackedAccess
    {
        [WrapValueEnumerable]
        public static Query5 TrackedInstances()
            => SomeTracked.Instances
                .AsValueEnumerable()
                .Select(x => x);

        [WrapValueEnumerable]
        public static Query6 TrackedSingletonAccess()
            => SomeSingleton.Instance
                .name
                .AsValueEnumerable()
                .Select(x => x);
    }

    public partial class ContainingInjectedAccess : MonoBehaviour
    {
        [Inject]
        void Awake()
            => InjectedTransform = GetComponent<Transform>();

        [WrapValueEnumerable]
        public Query7 TrackedInstances()
            => InjectedTransform
                .name
                .AsValueEnumerable()
                .Select(x => x);
    }

    [Test]
    public void GeneratedWrapperTypesExist()
    {
        _ = typeof(MethodExpressionBody.Query1);
        _ = typeof(PropertyExpressionBody.Query2);
        _ = typeof(MethodBlockBody.Query3);
        _ = typeof(GenericContainingType<int>.Query4);
        _ = typeof(ContainingTrackedAccess.Query5);
        _ = typeof(ContainingTrackedAccess.Query6);
        _ = typeof(ContainingInjectedAccess.Query7);

        Assert.Pass();
    }
}
#endif
