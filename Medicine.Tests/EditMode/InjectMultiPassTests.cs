using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Medicine;
using NUnit.Framework;
using UnityEngine;
#if MODULE_ZLINQ
using ZLinq;
#endif

public partial class InjectMultiPassTests
{
    [SetUp]
    public void Cleanup()
        => TestUtility.DestroyAllGameObjects();

    sealed partial class MBMultiPass : MonoBehaviour
    {
        [Inject]
        void Awake()
        {
            Colliders = GetComponentsInChildren<Collider>();
            FilteredColliders = Colliders.Where(x => x.enabled).ToArray();
            FirstCollider = Colliders.First();
        }
    }

    [Test]
    [SuppressMessage("ReSharper", "ConvertTypeCheckToNullCheck")]
    public void InjectMultiPass()
    {
        var obj = new GameObject("Test", typeof(MBMultiPass), typeof(BoxCollider));
        var component = obj.GetComponent<MBMultiPass>();
        Assert.That(component.Colliders.GetType(), Is.EqualTo(typeof(Collider[])));
        Assert.That(component.FilteredColliders.GetType(), Is.EqualTo(typeof(Collider[])));
        Assert.That(component.FirstCollider is Collider);
    }

    //////////////////////////////////////////////////////////////////////////////

    [Track]
    sealed partial class MBFindInstancesMultiPass : MonoBehaviour
    {
        [Inject]
        void Awake()
        {
            TrackedInstances = Find.Instances<MBFindInstancesMultiPass>();
            TrackedEnumerable = TrackedInstances.AsValueEnumerable();
            TrackedArray = TrackedEnumerable.ToArray();
            TrackedFirst = TrackedEnumerable.FirstOrDefault();
        }
    }

    [Test]
    public void InjectMultiPass_FindInstancesEnumerables()
    {
        var obj1 = new GameObject("Tracked1", typeof(MBFindInstancesMultiPass));
        var obj2 = new GameObject("Tracked2", typeof(MBFindInstancesMultiPass));

        var component = obj1.GetComponent<MBFindInstancesMultiPass>();
        var array = component.TrackedArray;

        Assert.That(array, Has.Length.EqualTo(2));
        Assert.That(array, Does.Contain(component));
        Assert.That(array, Does.Contain(obj2.GetComponent<MBFindInstancesMultiPass>()));
        Assert.That(component.TrackedFirst, Is.Not.Null);
        Assert.That(array, Does.Contain(component.TrackedFirst));
    }

    //////////////////////////////////////////////////////////////////////////////

    sealed partial class MBComponentsInSceneMultiPass : MonoBehaviour
    {
        [Inject]
        void Awake()
        {
            SceneComponents = Find.ComponentsInScene<BoxCollider>(gameObject.scene);
            SceneEnumerable = SceneComponents.AsValueEnumerable();
            SceneArray = SceneEnumerable.ToArray();
            FirstSceneComponent = SceneEnumerable.FirstOrDefault(); //.Cleanup(RecordComponent).Optional();
        }

        // static void RecordComponent(BoxCollider _) { }
    }

    [Test]
    public void InjectMultiPass_ComponentsInSceneEnumerables()
    {
        var box1 = new GameObject("Box1", typeof(BoxCollider)).GetComponent<BoxCollider>();
        var box2 = new GameObject("Box2", typeof(BoxCollider)).GetComponent<BoxCollider>();
        var box3 = new GameObject("Box3", typeof(BoxCollider)).GetComponent<BoxCollider>();

        var host = new GameObject("Host", typeof(MBComponentsInSceneMultiPass));
        var component = host.GetComponent<MBComponentsInSceneMultiPass>();

        var array = component.SceneArray;
        Assert.That(array, Is.EquivalentTo(new[] { box1, box2, box3 }));
        Assert.That(component.FirstSceneComponent, Is.Not.Null);
        Assert.That(array, Does.Contain(component.FirstSceneComponent));
    }
}