#if MODULE_ZLINQ
using ZLinq;
#else
using System.Linq;
#endif
using System;
using Medicine;
using NUnit.Framework;
using UnityEngine;
using static System.Reflection.BindingFlags;

public partial class TrackAttributePlayModeTests
{
    [SetUp]
    public void Cleanup()
        => TestUtility.DestroyAllGameObjects();

    //////////////////////////////////////////////////////////////////////////////

    [Track]
    sealed partial class MBTrackBasic : MonoBehaviour { }

    [Test]
    public void Track_Basic()
    {
        foreach (var _ in MBTrackBasic.Instances)
            Assert.Fail("Instance count should be zero at this point.");

        Assert.That(MBTrackBasic.Instances, Has.Count.EqualTo(0));

        for (int i = 1; i < 5; ++i)
        {
            _ = new GameObject(null, typeof(MBTrackBasic));
            Assert.That(MBTrackBasic.Instances, Has.Count.EqualTo(i));
            Assert.That(Find.Instances<MBTrackBasic>(), Has.Count.EqualTo(i));
        }
    }

    //////////////////////////////////////////////////////////////////////////////

    [Track]
    sealed partial class MBTrackInstanceIndex : MonoBehaviour, IInstanceIndex { }

    [Test]
    public void Track_InstanceIndex()
    {
        for (int i = 0; i < 5; ++i)
            _ = new GameObject(null, typeof(MBTrackInstanceIndex));

        foreach (var instance in MBTrackInstanceIndex.Instances)
            Assert.That(MBTrackInstanceIndex.Instances[instance.InstanceIndex], Is.EqualTo(instance));
    }

    //////////////////////////////////////////////////////////////////////////////

    [Track]
    interface ITrackByInterface1 { }

    [Track]
    interface ITrackByInterface2 { }

    [Track]
    sealed partial class MBTrackByInterface1 : MonoBehaviour, ITrackByInterface1, ITrackByInterface2 { }

    [Track]
    sealed partial class MBTrackByInterface2 : MonoBehaviour, ITrackByInterface1 { }

    [Test]
    public void Track_ByInterface()
    {
        for (int i = 0; i < 5; ++i)
            _ = new GameObject(null, typeof(MBTrackByInterface1));

        using (Find.Instances<MBTrackByInterface1>().ToPooledList(out var listMB1))
        using (Find.Instances<ITrackByInterface1>().ToPooledList(out var listI1))
        using (Find.Instances<ITrackByInterface2>().ToPooledList(out var listI2))
        {
            Assert.That(listMB1, Is.EquivalentTo(listI1));
            Assert.That(listMB1, Is.EquivalentTo(listI2));
        }

        for (int i = 0; i < 5; ++i)
            _ = new GameObject(null, typeof(MBTrackByInterface2));

        using (Find.Instances<MBTrackByInterface1>().ToPooledList(out var listMB1))
        using (Find.Instances<MBTrackByInterface2>().ToPooledList(out var listMB2))
        using (Find.Instances<ITrackByInterface1>().ToPooledList(out var listI1))
        using (Find.Instances<ITrackByInterface2>().ToPooledList(out var listI2))
        {
            Assert.That(listMB1, Is.SubsetOf(listI1));
            Assert.That(listMB2, Is.SubsetOf(listI1));
            Assert.That(listI2, Is.Not.SubsetOf(listMB2));
            Assert.That(listMB2, Is.Not.SubsetOf(listI2));
        }
    }

    //////////////////////////////////////////////////////////////////////////////

    [Track(instanceIdArray: true, transformAccessArray: true)]
    sealed partial class MBTrackStressTest
        : MonoBehaviour,
            IInstanceIndex,
            IFindByID<int>,
            IFindByID<ulong>,
            IUnmanagedData<float>,
            IUnmanagedData<int>
    {
        int IFindByID<int>.ID
            => GetHashCode();

        ulong IFindByID<ulong>.ID
            => (ulong)GetHashCode();

        void IUnmanagedData<float>.Initialize(out float data)
            => data = GetHashCode();

        void IUnmanagedData<int>.Initialize(out int data)
            => data = GetHashCode();
    }

    [Test]
    public void Track_StressTest()
    {
        const int instanceCount = 512;
        int passCount = Application.isBatchMode ? 1024 : 256;

        var all = new MBTrackStressTest[instanceCount];

        for (int i = 0; i < instanceCount; ++i)
        {
            var go = new GameObject(null, typeof(MBTrackStressTest));
            all[i] = go.GetComponent<MBTrackStressTest>();
        }

        var random = new System.Random(12345);
        for (int j = 0; j < passCount; ++j)
        {
            for (int i = 0; i < instanceCount; ++i)
                all[random.Next(0, instanceCount)].enabled ^= true;

            int expectedCount = all.AsValueEnumerable().Count(x => x.enabled);
            Assert.That(MBTrackStressTest.Instances, Has.Count.EqualTo(expectedCount));
            Assert.That(MBTrackStressTest.InstanceIDs, Has.Length.EqualTo(expectedCount));
            Assert.That(MBTrackStressTest.TransformAccessArray.length, Is.EqualTo(expectedCount));
            Assert.That(MBTrackStressTest.Unmanaged.floatArray, Has.Length.EqualTo(expectedCount));
            Assert.That(MBTrackStressTest.Unmanaged.intArray, Has.Length.EqualTo(expectedCount));

            for (int i = 0; i < instanceCount; ++i)
            {
                var instance = all[i];
                if (!instance.enabled)
                    continue;

                Assert.That(MBTrackStressTest.FindByID(instance.GetHashCode()), Is.EqualTo(instance));
                Assert.That(MBTrackStressTest.TransformAccessArray[instance.InstanceIndex], Is.EqualTo(instance.transform));
                ;
                Assert.That(MBTrackStressTest.InstanceIDs[instance.InstanceIndex], Is.EqualTo(instance.GetInstanceID()));
                Assert.That(MBTrackStressTest.Unmanaged.intArray[instance.InstanceIndex], Is.EqualTo(instance.GetHashCode()));
            }
        }
    }

    //////////////////////////////////////////////////////////////////////////////

    [Track(instanceIdArray: true, transformAccessArray: true)]
    abstract partial class MBTrackStressTestGeneric<TID, TData>
        : MonoBehaviour,
            IInstanceIndex,
            IFindByID<TID>,
            IUnmanagedData<TData>
        where TID : unmanaged, IEquatable<TID>
        where TData : unmanaged
    {
        TID IFindByID<TID>.ID
            => default;
    }

    sealed class MBTrackStressTestDerived : MBTrackStressTestGeneric<int, float>, IFindByID<int>
    {
        int IFindByID<int>.ID
            => GetHashCode();
    }

    [Test]
    public void Track_StressTest_Generic()
    {
        const int instanceCount = 512;
        int passCount = Application.isBatchMode ? 1024 : 256;

        MBTrackStressTestDerived.InitializeTransformAccessArray();

        var all = new MBTrackStressTestDerived[instanceCount];

        for (int i = 0; i < instanceCount; ++i)
        {
            var go = new GameObject(null, typeof(MBTrackStressTestDerived));
            all[i] = go.GetComponent<MBTrackStressTestDerived>();
        }

        var random = new System.Random(12345);
        for (int j = 0; j < passCount; ++j)
        {
            for (int i = 0; i < instanceCount; ++i)
                all[random.Next(0, instanceCount)].enabled ^= true;

            int expectedCount = all.AsValueEnumerable().Count(x => x.enabled);
            Assert.That(MBTrackStressTestDerived.Instances, Has.Count.EqualTo(expectedCount));
            Assert.That(MBTrackStressTestDerived.InstanceIDs, Has.Length.EqualTo(expectedCount));
            Assert.That(MBTrackStressTestDerived.TransformAccessArray.length, Is.EqualTo(expectedCount));
            Assert.That(MBTrackStressTestDerived.Unmanaged.TDataArray, Has.Length.EqualTo(expectedCount));

            for (int i = 0; i < instanceCount; ++i)
            {
                var instance = all[i];
                if (!instance.enabled)
                    continue;

                Assert.That(MBTrackStressTestDerived.FindByID(instance.GetHashCode()), Is.EqualTo(instance));
                Assert.That(MBTrackStressTestDerived.TransformAccessArray[instance.InstanceIndex], Is.EqualTo(instance.transform));
                Assert.That(MBTrackStressTestDerived.InstanceIDs[instance.InstanceIndex], Is.EqualTo(instance.GetInstanceID()));
            }
        }
    }

    //////////////////////////////////////////////////////////////////////////////

    [Track(manual: true)]
    sealed partial class MBTrackManual : MonoBehaviour
    {
        public void Register()
            => RegisterInstance();

        public void OnDestroy()
            => UnregisterInstance();
    }

    [Test]
    public void Track_Manual()
    {
        Assert.That(MBTrackManual.Instances, Has.Count.EqualTo(0));
        Assert.That(Find.Instances<MBTrackManual>(), Has.Count.EqualTo(0));

        for (int i = 1; i < 5; ++i)
        {
            var go = new GameObject(null, typeof(MBTrackManual));
            go.GetComponent<MBTrackManual>().Register();
            Assert.That(MBTrackManual.Instances, Has.Count.EqualTo(i));
            Assert.That(Find.Instances<MBTrackManual>(), Has.Count.EqualTo(i));
        }

        TestUtility.DestroyAllGameObjects();
        Assert.That(MBTrackManual.Instances, Has.Count.EqualTo(0));
        Assert.That(Find.Instances<MBTrackManual>(), Has.Count.EqualTo(0));
    }

    //////////////////////////////////////////////////////////////////////////////

    [Track(instanceIdArray: true)]
    sealed partial class MBTrackInstanceIdArray : MonoBehaviour { }

    [Test]
    public void Track_InstanceIdArray()
    {
        Assert.That(MBTrackInstanceIdArray.Instances, Has.Count.EqualTo(0));
        Assert.That(MBTrackInstanceIdArray.InstanceIDs, Has.Length.EqualTo(0));

        for (int i = 1; i < 5; ++i)
        {
            var go = new GameObject(null, typeof(MBTrackInstanceIdArray));
            Assert.That(MBTrackInstanceIdArray.Instances, Has.Count.EqualTo(i));
            Assert.That(MBTrackInstanceIdArray.InstanceIDs, Has.Length.EqualTo(i));
        }

        var instanceIDs = MBTrackInstanceIdArray.InstanceIDs;

        using (MBTrackInstanceIdArray.Instances.ToPooledList(out var list))
            for (int i = 0, n = list.Count; i < n; ++i)
                Assert.That(list[i].GetInstanceID(), Is.EqualTo(instanceIDs[i]));
    }

    //////////////////////////////////////////////////////////////////////////////

    [Track(transformAccessArray: true, transformInitialCapacity: 1337)]
    sealed partial class MBTrackTransformAccessArray : MonoBehaviour { }

    [Test]
    public void Track_TransformAccessArray()
    {
        Assert.That(MBTrackTransformAccessArray.TransformAccessArray.capacity, Is.EqualTo(1337));
        Assert.That(MBTrackTransformAccessArray.Instances, Has.Count.EqualTo(0));
        Assert.That(MBTrackTransformAccessArray.TransformAccessArray.length, Is.EqualTo(0));

        for (int i = 1; i < 5; ++i)
        {
            var go = new GameObject(null, typeof(MBTrackTransformAccessArray));
            Assert.That(MBTrackTransformAccessArray.Instances, Has.Count.EqualTo(i));
            Assert.That(MBTrackTransformAccessArray.TransformAccessArray.length, Is.EqualTo(i));
        }

        var taa = MBTrackTransformAccessArray.TransformAccessArray;

        using (MBTrackTransformAccessArray.Instances.ToPooledList(out var list))
            for (int i = 0, n = list.Count; i < n; ++i)
                Assert.That(list[i].transform, Is.EqualTo(taa[i]));
    }

    //////////////////////////////////////////////////////////////////////////////

    [Track(manual: true)]
    sealed partial class MBTrackFindByID : MonoBehaviour, IFindByID<ulong>, IFindByID<int>
    {
        public ulong ID { get; set; }

        int IFindByID<int>.ID => GetHashCode();

        public void Register()
            => RegisterInstance();

        public void OnDestroy()
            => UnregisterInstance();
    }

    [Test]
    public void Track_FindByID()
    {
        for (ulong i = 1; i < 5; ++i)
        {
            var go = new GameObject(null, typeof(MBTrackFindByID));
            var mb = go.GetComponent<MBTrackFindByID>();
            mb.ID = i;
            mb.Register();
            Assert.That(MBTrackFindByID.Instances, Has.Count.EqualTo(i));
        }

        foreach (var instance in MBTrackFindByID.Instances)
        {
            Assert.That(instance, Is.EqualTo(MBTrackFindByID.FindByID(instance.ID)));
            Assert.That(instance, Is.EqualTo(MBTrackFindByID.FindByID(instance.GetHashCode())));
        }
    }

    //////////////////////////////////////////////////////////////////////////////

    [Track]
    partial class MBTrackInheritanceBase : MonoBehaviour { }

    [Track]
    sealed partial class MBTrackInheritanceDerived : MBTrackInheritanceBase { }

    [Test]
    public void Track_Inheritance()
    {
        for (int i = 1; i < 5; ++i)
        {
            var go1 = new GameObject(null, typeof(MBTrackInheritanceBase));
            var go2 = new GameObject(null, typeof(MBTrackInheritanceDerived));
            Assert.That(MBTrackInheritanceBase.Instances, Has.Count.EqualTo(i * 2));
            Assert.That(MBTrackInheritanceDerived.Instances, Has.Count.EqualTo(i));
        }
    }

    //////////////////////////////////////////////////////////////////////////////

    [Track(cacheEnabledState: true)]
    sealed partial class MBTrackCacheEnabledState : MonoBehaviour { }

    [Test]
    public void Track_CacheEnabledState_EmitsEnabledProperty()
    {
        var enabledProperty
            = typeof(MBTrackCacheEnabledState)
                .GetProperty("enabled", Instance | Public | DeclaredOnly);

        Assert.That(enabledProperty, Is.Not.Null);
        Assert.That(enabledProperty!.PropertyType, Is.EqualTo(typeof(bool)));
        Assert.That(enabledProperty.CanRead, Is.True);
        Assert.That(enabledProperty.CanWrite, Is.True);
    }

    [Test]
    public void Track_CacheEnabledState_MonitorsEnableDisable()
    {
        var obj = new GameObject(nameof(MBTrackCacheEnabledState), typeof(MBTrackCacheEnabledState));
        var component = obj.GetComponent<MBTrackCacheEnabledState>();

        Assert.That(component.enabled, Is.True);

        ((Behaviour)component).enabled = false;
        Assert.That(component.enabled, Is.False);

        ((Behaviour)component).enabled = true;
        Assert.That(component.enabled, Is.True);

        component.enabled = false;
        Assert.That(((Behaviour)component).enabled, Is.False);
    }
}