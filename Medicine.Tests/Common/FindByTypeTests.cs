#nullable enable
using NUnit.Framework;
using UnityEngine;
using Medicine;
using Medicine.Internal;
using Object = UnityEngine.Object;

public partial class FindByTypeTests
{
    sealed class TestComponentA : MonoBehaviour { }
    sealed class TestComponentB : MonoBehaviour { }
    sealed class TestScriptableObject : ScriptableObject { }
    [Track]
    sealed partial class AnyObjectTestComponent : MonoBehaviour { }

    static void CreateWithComponent<T>(string name, bool active)
        where T : Component
    {
        var go = new GameObject(name);
        go.SetActive(active);
        go.AddComponent<T>();
    }

    [TearDown]
    public void Cleanup()
        => TestUtility.DestroyAllGameObjects();

    [TestCase(false, FindObjectsSortMode.None)]
    [TestCase(true, FindObjectsSortMode.None)]
    [TestCase(true, FindObjectsSortMode.InstanceID)]
    public void ObjectsByType_IsUsable(bool includeInactive, FindObjectsSortMode sortMode)
    {
        try
        {
            CreateWithComponent<TestComponentA>("Test.Active", active: true);
            CreateWithComponent<TestComponentA>("Test.Inactive", active: false);

            var results = Find.ObjectsByType<TestComponentA>(includeInactive, sortMode);

            Assert.That(results, Is.Not.Null);
            Assert.That(results.GetType(), Is.EqualTo(typeof(TestComponentA[])));

            foreach (var entry in results)
                Assert.That(entry, Is.Not.Null.And.TypeOf<TestComponentA>());

            var expected = includeInactive ? 2 : 1;
            Assert.That(results.Length, Is.EqualTo(expected));

            if (results.Length > 0)
                results[0] = results[0];
        }
        finally
        {
            TestUtility.DestroyAllGameObjects();
        }
    }

    [Test]
    public void ObjectsByTypeAll_PreservesCovariance()
    {
        TestScriptableObject? instance = null;

        try
        {
            instance = ScriptableObject.CreateInstance<TestScriptableObject>();

            var results = Find.ObjectsByTypeAll<TestScriptableObject>();

            Assert.That(results, Is.Not.Null);
            Assert.That(results.GetType(), Is.EqualTo(typeof(TestScriptableObject[])));
            Assert.That(results, Does.Contain(instance));

            // ReSharper disable once CoVariantArrayConversion
            Object[] asObjects = results;

            Assert.That(asObjects, Is.SameAs(results));
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void ObjectsByType_IsStableAcrossCalls()
    {
        CreateWithComponent<TestComponentA>("Test.A", active: true);
        CreateWithComponent<TestComponentB>("Test.B", active: true);

        var resultsA = Find.ObjectsByType<TestComponentA>();
        var resultsB = Find.ObjectsByType<TestComponentB>();

        Assert.That(resultsA.GetType(), Is.EqualTo(typeof(TestComponentA[])));
        Assert.That(resultsB.GetType(), Is.EqualTo(typeof(TestComponentB[])));
        Assert.That(resultsA, Is.Not.SameAs(resultsB));

        Assert.That(resultsA.Length, Is.EqualTo(1));
        Assert.That(resultsB.Length, Is.EqualTo(1));
    }

    [Test]
    public void ObjectsByType_ReturnsEmptyTypedArray_WhenNoInstancesExist()
    {
        var results = Find.ObjectsByType<TestComponentA>();

        Assert.That(results, Is.Not.Null);
        Assert.That(results.GetType(), Is.EqualTo(typeof(TestComponentA[])));
        Assert.That(results.Length, Is.EqualTo(0));
    }

    [Test]
    public void AnyObjectByType_DoesNotThrow_WhenTypeRegisteredButNoInstances()
    {
        var go = new GameObject();
        _ = go.AddComponent<AnyObjectTestComponent>();

        Object.DestroyImmediate(go);

        AnyObjectTestComponent? result = null;

        Assert.DoesNotThrow(() => result = Find.AnyObjectByType<AnyObjectTestComponent>());
        Assert.That(result, Is.Null);
    }
}
