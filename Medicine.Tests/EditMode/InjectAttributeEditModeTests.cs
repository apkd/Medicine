using System.Diagnostics.CodeAnalysis;
using Medicine;
using NUnit.Framework;
using UnityEngine;

[SuppressMessage("ReSharper", "ArrangeStaticMemberQualifier")]
[SuppressMessage("ReSharper", "RedundantNameQualifier")]
public partial class InjectAttributeEditModeTests
{
    [SetUp]
    public void Cleanup()
        => TestUtility.DestroyAllGameObjects();

    //////////////////////////////////////////////////////////////////////////////

    sealed partial class MBInjectBasic : MonoBehaviour
    {
        [Inject]
        void NotAwake()
            => Transform = GetComponent<Transform>();
    }

    [Test]
    public void InjectBasic()
    {
        var obj = new GameObject(null, typeof(MBInjectBasic));
        Assert.That(obj.GetComponent<MBInjectBasic>().Transform.IsAlive());
    }

    //////////////////////////////////////////////////////////////////////////////

    [Singleton]
    sealed partial class MBSingleton : MonoBehaviour
    {
        [Inject]
        void Awake()
            => SingletonInstance = Find.Singleton<MBSingleton>();
    }

    [Test]
    public void InjectSingleton()
    {
        var obj = new GameObject(null, typeof(MBSingleton));
        Assert.That(obj.GetComponent<MBSingleton>().SingletonInstance.IsAlive());
    }

    //////////////////////////////////////////////////////////////////////////////

    [Track]
    sealed partial class MBTracked : MonoBehaviour
    {
        [Inject]
        void Awake()
            => TrackedInstances = Find.Instances<MBTracked>();
    }

    [Test]
    public void InjectTracked()
    {
        var obj = new GameObject(null, typeof(MBTracked));
        Assert.That(obj.GetComponent<MBTracked>().TrackedInstances, Has.Count.EqualTo(1));
    }
}