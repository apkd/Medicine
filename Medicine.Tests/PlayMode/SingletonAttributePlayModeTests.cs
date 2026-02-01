using System;
using System.Collections;
using System.Text.RegularExpressions;
using Medicine;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Medicine.SingletonAttribute;
using Object = UnityEngine.Object;

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
        Assert.That(Find.Singleton<MBSingletonBasic>().IsNull());

        var go = new GameObject(null, typeof(MBSingletonBasic));
        var component = go.GetComponent<MBSingletonBasic>();

        Assert.That(Find.Singleton<MBSingletonBasic>(), Is.EqualTo(component));
        Assert.That(MBSingletonBasic.Instance, Is.EqualTo(component));

        Object.DestroyImmediate(go);
        Assert.That(Find.Singleton<MBSingletonBasic>().IsNull());
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

        Assert.That(Find.Singleton<MBSingletonManual>().IsDead());

        component.Register();
        Assert.That(Find.Singleton<MBSingletonManual>(), Is.EqualTo(component));

        component.Unregister();
        Assert.That(Find.Singleton<MBSingletonManual>().IsDead());
    }

    //////////////////////////////////////////////////////////////////////////////

    [Singleton(strategy: Strategy.KeepExisting)]
    sealed partial class MBSingletonKeepExisting : MonoBehaviour { }

    [Test]
    public void Singleton_KeepExisting()
    {
        var existingGo = new GameObject("Existing", typeof(MBSingletonKeepExisting));
        var existing = existingGo.GetComponent<MBSingletonKeepExisting>();

        var incomingGo = new GameObject("Incoming", typeof(MBSingletonKeepExisting));
        var incoming = incomingGo.GetComponent<MBSingletonKeepExisting>();

        Assert.That(Find.Singleton<MBSingletonKeepExisting>(), Is.SameAs(existing));
        Assert.That(MBSingletonKeepExisting.Instance, Is.SameAs(existing));
        Assert.That(incoming, Is.Not.SameAs(existing));
    }

    //////////////////////////////////////////////////////////////////////////////

    [Singleton(strategy: Strategy.LogWarning)]
    sealed partial class MBSingletonLogWarning : MonoBehaviour { }

    [Test]
    public void Singleton_LogWarning()
    {
        var existingGo = new GameObject("Existing", typeof(MBSingletonLogWarning));
        existingGo.GetComponent<MBSingletonLogWarning>();

        LogAssert.Expect(
            LogType.Warning,
            "Singleton<MBSingletonLogWarning> already has an active instance. Replacing the previous instance (Existing) with the new one (Incoming)."
        );

        var incomingGo = new GameObject("Incoming", typeof(MBSingletonLogWarning));
        var incoming = incomingGo.GetComponent<MBSingletonLogWarning>();

        Assert.That(Find.Singleton<MBSingletonLogWarning>(), Is.SameAs(incoming));
    }

    //////////////////////////////////////////////////////////////////////////////

    [Singleton(strategy: Strategy.Recommended)]
    sealed partial class MBSingletonLogError : MonoBehaviour { }

    [Test]
    public void Singleton_LogError()
    {
        var existingGo = new GameObject("Existing", typeof(MBSingletonLogError));
        existingGo.GetComponent<MBSingletonLogError>();

        LogAssert.Expect(LogType.Error, new Regex(".*already has an active instance.*Replacing the previous instance.*"));

        var incomingGo = new GameObject("Incoming", typeof(MBSingletonLogError));
        var incoming = incomingGo.GetComponent<MBSingletonLogError>();

        Assert.That(Find.Singleton<MBSingletonLogError>(), Is.SameAs(incoming));
    }

    //////////////////////////////////////////////////////////////////////////////

    [Singleton(strategy: Strategy.ThrowException, manual: true)]
    sealed partial class MBSingletonThrowException : MonoBehaviour
    {
        public void Register() => RegisterInstance();
        public void Unregister() => UnregisterInstance();
    }

    [Test]
    public void Singleton_ThrowException()
    {
        var existingGo = new GameObject("Existing", typeof(MBSingletonThrowException));
        var existing = existingGo.GetComponent<MBSingletonThrowException>();
        existing.Register();

        var incomingGo = new GameObject("Incoming", typeof(MBSingletonThrowException));
        var incoming = incomingGo.GetComponent<MBSingletonThrowException>();

        Assert.Throws<InvalidOperationException>(() => incoming.Register());

        existing.Unregister();
    }

    //////////////////////////////////////////////////////////////////////////////

    [Singleton(strategy: Strategy.Destroy)]
    sealed partial class MBSingletonDestroyReplace : MonoBehaviour { }

    [UnityTest]
    public IEnumerator Singleton_Destroy_Replaces()
    {
        var existingGo = new GameObject("Existing", typeof(MBSingletonDestroyReplace));
        var existing = existingGo.GetComponent<MBSingletonDestroyReplace>();

        var incomingGo = new GameObject("Incoming", typeof(MBSingletonDestroyReplace));
        var incoming = incomingGo.GetComponent<MBSingletonDestroyReplace>();

        Assert.That(Find.Singleton<MBSingletonDestroyReplace>(), Is.SameAs(incoming));

        yield return null;

        Assert.That(existing.IsDead());
        Assert.That(Find.Singleton<MBSingletonDestroyReplace>(), Is.SameAs(incoming));
    }

    //////////////////////////////////////////////////////////////////////////////

    [Singleton(strategy: Strategy.KeepExisting | Strategy.Destroy)]
    sealed partial class MBSingletonDestroyKeepExisting : MonoBehaviour { }

    [UnityTest]
    public IEnumerator Singleton_Destroy_KeepExisting()
    {
        var existingGo = new GameObject("Existing", typeof(MBSingletonDestroyKeepExisting));
        var existing = existingGo.GetComponent<MBSingletonDestroyKeepExisting>();

        var incomingGo = new GameObject("Incoming", typeof(MBSingletonDestroyKeepExisting));
        var incoming = incomingGo.GetComponent<MBSingletonDestroyKeepExisting>();

        Assert.That(Find.Singleton<MBSingletonDestroyKeepExisting>(), Is.SameAs(existing));

        yield return null;

        Assert.That(incoming.IsDead());
        Assert.That(Find.Singleton<MBSingletonDestroyKeepExisting>(), Is.SameAs(existing));
    }

    //////////////////////////////////////////////////////////////////////////////

    [Singleton(strategy: Strategy.AutoInstantiate)]
    sealed partial class MBSingletonAutoInstantiate : MonoBehaviour { }

    [Test]
    public void Singleton_AutoInstantiate_MonoBehaviour()
    {
        var go = new GameObject("Existing", typeof(MBSingletonAutoInstantiate));
        var existing = go.GetComponent<MBSingletonAutoInstantiate>();

        Assert.That(Find.Singleton<MBSingletonAutoInstantiate>(), Is.SameAs(existing));

        Object.DestroyImmediate(go);

        var autoInstance = Find.Singleton<MBSingletonAutoInstantiate>();
        Assert.That(autoInstance.IsAlive());
        Assert.That(autoInstance, Is.Not.SameAs(existing));

        Object.DestroyImmediate(autoInstance.gameObject);
    }

    //////////////////////////////////////////////////////////////////////////////

    [Singleton(strategy: Strategy.AutoInstantiate)]
    sealed partial class MBSingletonAutoInstantiateFirstAccess : MonoBehaviour { }

    [Test]
    public void Singleton_AutoInstantiate_FirstAccess_MonoBehaviour()
    {
        var instance = MBSingletonAutoInstantiateFirstAccess.Instance;
        Assert.That(instance.IsAlive());
        Assert.That(Find.Singleton<MBSingletonAutoInstantiateFirstAccess>(), Is.SameAs(instance));
    }

    //////////////////////////////////////////////////////////////////////////////

    [Singleton]
    interface ISingletonByInterface1 { }

    [Singleton]
    interface ISingletonByInterface2 { }

    [Singleton]
    sealed partial class MBSingletonByInterface1 : MonoBehaviour, ISingletonByInterface1, ISingletonByInterface2 { }

    [Singleton]
    sealed partial class MBSingletonByInterface2 : MonoBehaviour, ISingletonByInterface1 { }

    [Test]
    public void Singleton_ByInterface()
    {
        var go1 = new GameObject(null, typeof(MBSingletonByInterface1));
        var singleton1 = go1.GetComponent<MBSingletonByInterface1>();
        Assert.That(Find.Singleton<ISingletonByInterface1>(), Is.SameAs(singleton1));
        Assert.That(Find.Singleton<ISingletonByInterface2>(), Is.SameAs(singleton1));
        Assert.That(Find.Singleton<ISingletonByInterface1>(), Is.SameAs(Find.Singleton<MBSingletonByInterface1>()));
        Assert.That(Find.Singleton<ISingletonByInterface2>(), Is.SameAs(Find.Singleton<MBSingletonByInterface1>()));
        TestUtility.DestroyAllGameObjects();
        Assert.That(go1.IsDead());

        var go2 = new GameObject(null, typeof(MBSingletonByInterface2));
        var singleton2 = go2.GetComponent<MBSingletonByInterface2>();
        Assert.That(Find.Singleton<ISingletonByInterface1>(), Is.SameAs(singleton2));
        Assert.That(Find.Singleton<ISingletonByInterface1>(), Is.SameAs(Find.Singleton<MBSingletonByInterface2>()));
        Assert.That(Find.Singleton<ISingletonByInterface2>(), Is.Null);
    }

    //////////////////////////////////////////////////////////////////////////////

    [Singleton]
    partial class SOSingleton : ScriptableObject { }

    [Test]
    public void Singleton_ScriptableObject()
    {
        Assert.That(Find.Singleton<SOSingleton>().IsNull());

        var so = ScriptableObject.CreateInstance<SOSingleton>();
        
        Assert.That(Find.Singleton<SOSingleton>(), Is.EqualTo(so));

        Object.DestroyImmediate(so);
        Assert.That(Find.Singleton<SOSingleton>().IsDead());
    }

    //////////////////////////////////////////////////////////////////////////////

    [Singleton(strategy: Strategy.AutoInstantiate)]
    partial class SOSingletonAutoInstantiate : ScriptableObject { }

    [Test]
    public void Singleton_AutoInstantiate_ScriptableObject()
    {
        var existing = ScriptableObject.CreateInstance<SOSingletonAutoInstantiate>();

        Assert.That(Find.Singleton<SOSingletonAutoInstantiate>(), Is.SameAs(existing));

        Object.DestroyImmediate(existing);

        var autoInstance = Find.Singleton<SOSingletonAutoInstantiate>();
        Assert.That(autoInstance, Is.Not.Null);
        Assert.That(autoInstance, Is.Not.SameAs(existing));

        Object.DestroyImmediate(autoInstance);
    }

    //////////////////////////////////////////////////////////////////////////////

    [Singleton(strategy: Strategy.AutoInstantiate)]
    partial class SOSingletonAutoInstantiateFirstAccess : ScriptableObject { }

    [Test]
    public void Singleton_AutoInstantiate_FirstAccess_ScriptableObject()
    {
        var instance = SOSingletonAutoInstantiateFirstAccess.Instance;
        Assert.That(instance.IsAlive());
        Assert.That(Find.Singleton<SOSingletonAutoInstantiateFirstAccess>(), Is.SameAs(instance));
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
        Assert.That(Find.Singleton<MBSingletonBase>().IsDead());
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
        foreach (var (type, instance) in Find.AllActiveSingletons())
            activeSingletons.Add(instance);

        Assert.That(activeSingletons, Contains.Item(c1));
        Assert.That(activeSingletons, Contains.Item(c2));
    }
}