using Medicine;
using NUnit.Framework;
using UnityEngine;

public partial class SingletonAttributePlayModeTests
{
    [SetUp]
    public void Cleanup()
        => TestUtility.DestroyAllGameObjects();

    //////////////////////////////////////////////////////////////////////////////

    [Singleton]
    sealed partial class MBSingletonBasic : MonoBehaviour { }

    [Test]
    public void Singleton_Basic()
    {
        Assert.That((bool)Find.Singleton<MBSingletonBasic>(), Is.False);

        var go = new GameObject(null, typeof(MBSingletonBasic));
        var component = go.GetComponent<MBSingletonBasic>();

        Assert.That(Find.Singleton<MBSingletonBasic>(), Is.EqualTo(component));
        Assert.That(MBSingletonBasic.Instance, Is.EqualTo(component));

        Object.DestroyImmediate(go);
        Assert.That((bool)Find.Singleton<MBSingletonBasic>(), Is.False);
    }

    //////////////////////////////////////////////////////////////////////////////

    [Singleton(manual: true)]
    sealed partial class MBSingletonManual : MonoBehaviour
    {
        public void Register() => RegisterInstance();
        public void Unregister() => UnregisterInstance();
    }

    [Test]
    public void Singleton_Manual()
    {
        var go = new GameObject(null, typeof(MBSingletonManual));
        var component = go.GetComponent<MBSingletonManual>();

        Assert.That((bool)Find.Singleton<MBSingletonManual>(), Is.False);

        component.Register();
        Assert.That(Find.Singleton<MBSingletonManual>(), Is.EqualTo(component));

        component.Unregister();
        Assert.That((bool)Find.Singleton<MBSingletonManual>(), Is.False);
    }

    //////////////////////////////////////////////////////////////////////////////

    [Singleton]
    partial class SOSingleton : ScriptableObject { }

    [Test]
    public void Singleton_ScriptableObject()
    {
        Assert.That((bool)Find.Singleton<SOSingleton>(), Is.False);

        var so = ScriptableObject.CreateInstance<SOSingleton>();
        
        Assert.That(Find.Singleton<SOSingleton>(), Is.EqualTo(so));

        Object.DestroyImmediate(so);
        Assert.That((bool)Find.Singleton<SOSingleton>(), Is.False);
    }

    //////////////////////////////////////////////////////////////////////////////

    [Singleton]
    partial class MBSingletonBase : MonoBehaviour { }

    [Singleton]
    sealed partial class MBSingletonDerived : MBSingletonBase { }

    [Test]
    public void Singleton_Inheritance()
    {
        var go = new GameObject(null, typeof(MBSingletonDerived));
        var instance = go.GetComponent<MBSingletonDerived>();

        Assert.That(Find.Singleton<MBSingletonBase>(), Is.EqualTo(instance));
        Assert.That(Find.Singleton<MBSingletonDerived>(), Is.EqualTo(instance));

        Object.DestroyImmediate(go);
        Assert.That((bool)Find.Singleton<MBSingletonBase>(), Is.False);
    }

    //////////////////////////////////////////////////////////////////////////////

    [Test]
    public void Singleton_UntypedAccess()
    {
        var go = new GameObject(null, typeof(MBSingletonBasic));
        var component = go.GetComponent<MBSingletonBasic>();

        Assert.That(Find.Singleton(typeof(MBSingletonBasic)), Is.EqualTo(component));
    }

    //////////////////////////////////////////////////////////////////////////////

    [Test]
    public void Singleton_AllActiveSingletons()
    {
        var go1 = new GameObject(null, typeof(MBSingletonBasic));
        var c1 = go1.GetComponent<MBSingletonBasic>();

        var go2 = new GameObject(null, typeof(MBSingletonManual));
        var c2 = go2.GetComponent<MBSingletonManual>();
        c2.Register();

        var activeSingletons = new System.Collections.Generic.List<Object>();
        foreach (var singleton in Find.AllActiveSingletons())
            activeSingletons.Add(singleton);

        Assert.That(activeSingletons, Contains.Item(c1));
        Assert.That(activeSingletons, Contains.Item(c2));
    }
}