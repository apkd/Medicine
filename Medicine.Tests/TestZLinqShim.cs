#if !MODULE_ZLINQ
using System.Collections.Generic;
using Medicine.Internal;

public static class TestZLinqShim
{
    public static IEnumerable<T> AsValueEnumerable<T>(this T[] enumerable)
        where T : class
    {
        foreach (var value in enumerable)
            yield return value;
    }

    public static IEnumerable<T> AsValueEnumerable<T>(this ComponentsInSceneEnumerable<T> enumerable)
        where T : class
    {
        foreach (var value in enumerable)
            yield return value;
    }

    public static IEnumerable<T> AsValueEnumerable<T>(this ComponentsEnumerable<T> enumerable)
        where T : class
    {
        foreach (var value in enumerable)
            yield return value;
    }

    public static IEnumerable<T> AsValueEnumerable<T>(this TrackedInstances<T> enumerable)
        where T : class
    {
        foreach (var value in enumerable)
            yield return value;
    }
}
#endif