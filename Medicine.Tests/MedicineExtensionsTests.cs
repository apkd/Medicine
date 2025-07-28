#if MODULE_ZLINQ
using ZLinq;
#endif
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;
using Medicine;
using UnityEngine.Animations;
using Object = UnityEngine.Object;

public sealed class MedicineExtensionsTests
{
    Scene scene;

    [SetUp]
    public void Setup()
    {
        scene = SceneManager.GetActiveScene();
    }

    [TearDown]
    public void Cleanup()
    {
        foreach (var gameObject in scene.GetRootGameObjects())
            Object.DestroyImmediate(gameObject);
    }

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
        var spawned = SpawnGameObjectsWith<BoxCollider>(8, nested: true);
        var result = scene.EnumerateComponentsInScene<BoxCollider>().AsValueEnumerable().ToArray();
        Assert.That(result, Is.EquivalentTo(spawned));
    }

    [Test]
    public void EnumerateComponentsInScene_WithInterfaceType()
    {
        var spawned = SpawnGameObjectsWith<PositionConstraint>(8, nested: true);
        var result = scene.EnumerateComponentsInScene<IConstraint>().AsValueEnumerable().ToArray();
        Assert.That(result, Is.EquivalentTo(spawned), "Should find all components implementing the interface");
    }

    [Test]
    public void EnumerateComponentsInScene_WithDerivedTypes()
    {
        var boxColliders = SpawnGameObjectsWith<BoxCollider>(8, nested: true);
        var sphereColliders = SpawnGameObjectsWith<SphereCollider>(7, nested: true);

        var baseTypeResult = scene.EnumerateComponentsInScene<Collider>().AsValueEnumerable().ToArray();
        var derivedTypeResult = scene.EnumerateComponentsInScene<SphereCollider>().AsValueEnumerable().ToArray();

        Assert.That(baseTypeResult, Is.EquivalentTo(boxColliders.OfType<Collider>().Concat(sphereColliders)), "Should find both base and derived types when searching for base type");
        Assert.That(derivedTypeResult, Is.EquivalentTo(sphereColliders), "Should find only derived types when searching for derived type");
    }

    [Test]
    public void EnumerateComponentsInScene_WithNoMatchingComponents_ReturnsEmptyEnumerable()
    {
        _ = SpawnGameObjectsWith<MeshFilter>(8, nested: true);
        var result = scene.EnumerateComponentsInScene<BoxCollider>().AsValueEnumerable().ToArray();
        Assert.That(result, Is.Empty, "Should return empty enumerable when no matching components exist");
    }

    [Test]
    public void EnumerateComponentsInScene_RespectsIncludeInactiveFlag_WhenFalse()
    {
        var components = SpawnGameObjectsWith<BoxCollider>(8);

        foreach (var x in components.Take(5))
            x.gameObject.SetActive(false);

        var result = scene.EnumerateComponentsInScene<BoxCollider>(includeInactive: false).AsValueEnumerable().ToArray();

        Assert.That(result, Is.EquivalentTo(components.Skip(5).Take(3)), "Should only include active components when includeInactive is false");
    }

    [Test]
    public void EnumerateComponentsInScene_RespectsIncludeInactiveFlag_WhenTrue()
    {
        var components = SpawnGameObjectsWith<BoxCollider>(8);

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
}