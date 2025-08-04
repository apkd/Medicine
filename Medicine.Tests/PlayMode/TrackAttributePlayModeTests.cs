using System;
using Medicine;
using NUnit.Framework;
using UnityEngine;
using ZLinq;

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
        for (int i = 1; i < 5; ++i)
        {
            var go = new GameObject(null, typeof(MBTrackBasic));
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
        const int passCount = 512;

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
        const int passCount = 512;

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
                Assert.That(list[i].GetHashCode(), Is.EqualTo(instanceIDs[i]));
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
}