using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Medicine;
using NUnit.Framework;
using UnityEngine;

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
}