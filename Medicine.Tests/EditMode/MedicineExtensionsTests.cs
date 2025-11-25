#if MODULE_ZLINQ
using ZLinq;
#endif
using System;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using Medicine;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Animations;

public sealed class MedicineExtensionsTests
{
    Scene scene;

    [SetUp]
    public void Setup()
        => scene = SceneManager.GetActiveScene();

    [TearDown]
    public void Cleanup()
        => TestUtility.DestroyAllGameObjects();

    static T[] SpawnGameObjectsWith<T>(int count, bool nested = false) where T : Component
    {
        return Enumerable().ToArray();

        IEnumerable<T> Enumerable()
        {
            GameObject previous = null;
            for (int i = 0; i < count; i++)
            {
                var gameObject = new GameObject();
                yield return gameObject.AddComponent<T>();

                if (nested)
                    if (i % 2 is 1)
                        gameObject.transform.SetParent(previous.transform);

                previous = gameObject;
            }
        }
    }

    [Test]
    public void EnumerateComponentsInScene_ReturnsCorrectComponents()
    {
        var spawned = SpawnGameObjectsWith<BoxCollider>(count: 8, nested: true);
        var result = scene.EnumerateComponentsInScene<BoxCollider>().AsValueEnumerable().ToArray();
        Assert.That(result, Is.EquivalentTo(spawned));
    }

    [Test]
    public void EnumerateComponentsInScene_MixedTypes()
    {
        SpawnGameObjectsWith<BoxCollider>(count: 8, nested: true);
        SpawnGameObjectsWith<PositionConstraint>(count: 8, nested: true);
        SpawnGameObjectsWith<Light>(count: 8, nested: true);
        SpawnGameObjectsWith<Rigidbody>(count: 8, nested: true);

        foreach (var go in scene.GetRootGameObjects())
        {
            go.EnumerateComponentsInChildren<BoxCollider>().AsValueEnumerable().ToArray();
            go.EnumerateComponentsInChildren<PositionConstraint>().AsValueEnumerable().ToArray();
            go.EnumerateComponentsInChildren<Light>().AsValueEnumerable().ToArray();
            go.EnumerateComponentsInChildren<Rigidbody>().AsValueEnumerable().ToArray();
            go.EnumerateComponentsInChildren<Transform>().AsValueEnumerable().ToArray();
            go.EnumerateComponentsInChildren<IConstraint>().AsValueEnumerable().ToArray();
        }
    }

    [Test]
    public void EnumerateComponentsInScene_WithInterfaceType()
    {
        var spawned = SpawnGameObjectsWith<PositionConstraint>(count: 8, nested: true);
        var result = scene.EnumerateComponentsInScene<IConstraint>().AsValueEnumerable().ToArray();
        Assert.That(result, Is.EquivalentTo(spawned), "Should find all components implementing the interface");
    }

    [Test]
    public void EnumerateComponentsInScene_WithDerivedTypes()
    {
        var boxColliders = SpawnGameObjectsWith<BoxCollider>(count: 8, nested: true);
        var sphereColliders = SpawnGameObjectsWith<SphereCollider>(count: 7, nested: true);

        var baseTypeResult = scene.EnumerateComponentsInScene<Collider>().AsValueEnumerable().ToArray();
        var derivedTypeResult = scene.EnumerateComponentsInScene<SphereCollider>().AsValueEnumerable().ToArray();

        Assert.That(baseTypeResult, Is.EquivalentTo(boxColliders.OfType<Collider>().Concat(sphereColliders)), "Should find both base and derived types when searching for base type");
        Assert.That(derivedTypeResult, Is.EquivalentTo(sphereColliders), "Should find only derived types when searching for derived type");
    }

    [Test]
    public void EnumerateComponentsInScene_WithNoMatchingComponents_ReturnsEmptyEnumerable()
    {
        _ = SpawnGameObjectsWith<MeshFilter>(count: 8, nested: true);
        var result = scene.EnumerateComponentsInScene<BoxCollider>().AsValueEnumerable().ToArray();
        Assert.That(result, Is.Empty, "Should return empty enumerable when no matching components exist");
    }

    [Test]
    public void EnumerateComponentsInScene_RespectsIncludeInactiveFlag_WhenFalse()
    {
        var components = SpawnGameObjectsWith<BoxCollider>(count: 8);

        foreach (var x in components.Take(5))
            x.gameObject.SetActive(false);

        var result = scene.EnumerateComponentsInScene<BoxCollider>(includeInactive: false).AsValueEnumerable().ToArray();

        Assert.That(result, Is.EquivalentTo(components.Skip(5).Take(3)), "Should only include active components when includeInactive is false");
    }

    [Test]
    public void EnumerateComponentsInScene_RespectsIncludeInactiveFlag_WhenTrue()
    {
        var components = SpawnGameObjectsWith<BoxCollider>(count: 8);

        foreach (var x in components.Take(5))
            x.gameObject.SetActive(false);

        var result = scene.EnumerateComponentsInScene<BoxCollider>(includeInactive: true).AsValueEnumerable().ToArray();

        Assert.That(result, Is.EquivalentTo(components), "Should include both active and inactive components when includeInactive is true");
    }

    [Test]
    public void EnumerateComponentsInScene_HandlesMultipleComponentsOnSameGameObject()
    {
        var gameObject = new GameObject();
        var component1 = gameObject.AddComponent<BoxCollider>();
        var component2 = gameObject.AddComponent<BoxCollider>();
        var component3 = gameObject.AddComponent<BoxCollider>();

        var result = scene.EnumerateComponentsInScene<BoxCollider>().AsValueEnumerable().ToArray();

        Assert.That(result, Has.Length.EqualTo(3), "Should find all components even if they're on the same GameObject");
        Assert.That(result, Does.Contain(component1));
        Assert.That(result, Does.Contain(component2));
        Assert.That(result, Does.Contain(component3));
    }

    [Test]
    public void EnumerateComponents_ReturnsOnlyComponentsOnSelf()
    {
        var parent = new GameObject("Parent").AddComponent<BoxCollider>();
        var selfGo = new GameObject("Self") { transform = { parent = parent.transform } };
        var child = new GameObject("Child") { transform = { parent = selfGo.transform } }.AddComponent<BoxCollider>();

        var selfComp1 = selfGo.AddComponent<BoxCollider>();
        var selfComp2 = selfGo.AddComponent<BoxCollider>();

        var result = selfGo.EnumerateComponents<BoxCollider>().AsValueEnumerable().ToArray();

        Assert.That(result, Is.EquivalentTo(new[] { selfComp1, selfComp2 }));
    }

    [Test]
    public void EnumerateComponents_WithInterfaceType()
    {
        var go = new GameObject("Test", typeof(BoxCollider), typeof(PositionConstraint));
        var constraint = go.GetComponent<PositionConstraint>();

        var result = go.EnumerateComponents<IConstraint>().AsValueEnumerable().ToArray();

        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(constraint));
    }

    [Test]
    public void EnumerateComponentsInChildren_RespectsIncludeInactiveFlag()
    {
        var rootGo = new GameObject("Root", typeof(BoxCollider));
        var rootComp = rootGo.GetComponent<BoxCollider>();

        var childGo = new GameObject("Child", typeof(BoxCollider)) { transform = { parent = rootGo.transform } };
        var childComp = childGo.GetComponent<BoxCollider>();
        childGo.SetActive(false);

        var resultWithInactive = rootGo.EnumerateComponentsInChildren<BoxCollider>(includeInactive: true).AsValueEnumerable().ToArray();
        Assert.That(resultWithInactive, Is.EquivalentTo(new[] { rootComp, childComp }));

        var resultWithoutInactive = rootGo.EnumerateComponentsInChildren<BoxCollider>(includeInactive: false).AsValueEnumerable().ToArray();
        Assert.That(resultWithoutInactive, Is.EquivalentTo(new[] { rootComp }));
    }

    [Test]
    public void EnumerateComponentsInChildren_WithInterfaceType()
    {
        var rootGo = new GameObject("Root");
        var childGo = new GameObject("Child") { transform = { parent = rootGo.transform } };
        var constraint = childGo.AddComponent<PositionConstraint>();

        var result = rootGo.EnumerateComponentsInChildren<IConstraint>(true).AsValueEnumerable().ToArray();

        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(constraint));
    }

    [Test]
    public void EnumerateComponentsInParents_RespectsIncludeInactiveFlag()
    {
        var go1 = new GameObject("Root", typeof(BoxCollider));
        var box1 = go1.GetComponent<BoxCollider>();

        var go2 = new GameObject("Parent", typeof(BoxCollider)) { transform = { parent = go1.transform } };
        var box2 = go2.GetComponent<BoxCollider>();
        go2.SetActive(false);

        var go3 = new GameObject("Child", typeof(BoxCollider)) { transform = { parent = go2.transform } };
        var box3 = go3.GetComponent<BoxCollider>();

        var resultWithInactive = go3.EnumerateComponentsInParents<BoxCollider>(includeInactive: true).AsValueEnumerable().ToArray();
        Assert.That(resultWithInactive, Is.EquivalentTo(new[] { box3, box2, box1 }));

        var resultWithoutInactive = go3.EnumerateComponentsInParents<BoxCollider>(includeInactive: false).AsValueEnumerable().ToArray();
        Assert.That(resultWithoutInactive, Is.EquivalentTo(new[] { box1 }));
    }

    [Test]
    public void EnumerateComponentsInParents_WithInterfaceType()
    {
        var go1 = new GameObject("Root", typeof(PositionConstraint));
        var go2 = new GameObject("Parent", typeof(PositionConstraint)) { transform = { parent = go1.transform } };
        var go3 = new GameObject("Child") { transform = { parent = go2.transform } };

        var comp1 = go1.GetComponent<PositionConstraint>();
        var comp2 = go2.GetComponent<PositionConstraint>();

        go2.SetActive(false);

        var resultIncludeInactive = go3.EnumerateComponentsInParents<IConstraint>(includeInactive: true)
            .AsValueEnumerable()
            .ToArray();

        Assert.That(
            resultIncludeInactive, Has.Length.EqualTo(2),
            "Should return self-and-ancestor components implementing the interface even when some ancestors are inactive"
        );

        Assert.That(resultIncludeInactive[0], Is.EqualTo(comp2), "Should return inactive parent first");
        Assert.That(resultIncludeInactive[1], Is.EqualTo(comp1), "Should return active parent second");
        ;

        var resultOnlyActive = go3.EnumerateComponentsInParents<IConstraint>(includeInactive: false)
            .AsValueEnumerable()
            .ToArray();

        Assert.That(
            resultOnlyActive, Is.EquivalentTo(new[] { comp1 }),
            "When includeInactive is false, enumeration must skip inactive ancestors entirely"
        );
    }

#if MODULE_ZLINQ
    [Test]
    public void ToPooledList_Copies_All_Elements()
    {
        var source = new[] { 1, 2, 3, 4, 5 };
        using var pooled = source.AsValueEnumerable().ToPooledList();

        Assert.That(pooled.List, Is.Not.Null);
        Assert.That(pooled.List, Has.Count.EqualTo(source.Length));
        CollectionAssert.AreEqual(source, pooled);
    }

    [Test]
    public void ToPooledList_With_Empty_Source()
    {
        var source = Array.Empty<int>();
        using var pooled = source.AsValueEnumerable().ToPooledList();

        Assert.That(pooled.List, Is.Not.Null);
        Assert.That(pooled.List, Is.Empty);
    }

    [Test]
    public void ToPooledList_Independent_Lists_Should_Not_Share_State()
    {
        var first = new[] { 10, 20 };
        var second = new List<int> { 30, 40, 50 };

        using var list1 = first.AsValueEnumerable().ToPooledList();
        using var list2 = second.AsValueEnumerable().ToPooledList();
        using var list3 = ValueEnumerable.Range(0, 15).ToPooledList();

        Assert.That(first, Is.EqualTo(list1.List));
        Assert.That(second, Is.EqualTo(list2.List));
        Assert.That(Enumerable.Range(0, 15), Is.EqualTo(list3.List));
    }

    [Test]
    public void ToPooledList_Should_Reuse_Lists()
    {
        var lists = new List<List<int>>();

        using (var list = ValueEnumerable.Range(2, 2).ToPooledList())
            lists.Add(list.List);

        using (var list = ValueEnumerable.Range(10, 5).ToPooledList())
            lists.Add(list.List);

        using (var list = ValueEnumerable.Range(0, 10).ToPooledList())
            lists.Add(list.List);

        using (var list = ValueEnumerable.Range(3, 15).ToPooledList())
            lists.Add(list.List);

        using (var list = ValueEnumerable.Range(1, 1).ToPooledList())
            lists.Add(list.List);

        var first = lists.First();

        foreach (var list in lists.Skip(1))
            Assert.That(list, Is.SameAs(first), "Lists should be reused");

        Assert.That(first, Has.Count.EqualTo(0), "List should be cleared after use");
    }
#endif
}