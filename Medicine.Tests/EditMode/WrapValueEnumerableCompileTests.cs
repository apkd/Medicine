#if MODULE_ZLINQ
using System;
using Medicine;
using NUnit.Framework;
using ZLinq;

public sealed partial class WrapValueEnumerableCompileTests
{
    static partial class Fixtures
    {
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
    }

    [Test]
    public void GeneratedWrapperTypesExist()
    {
        _ = typeof(Fixtures.MethodExpressionBody.Query1);
        _ = typeof(Fixtures.PropertyExpressionBody.Query2);
        _ = typeof(Fixtures.MethodBlockBody.Query3);
        _ = typeof(Fixtures.GenericContainingType<int>.Query4);

        Assert.Pass();
    }
}
#endif