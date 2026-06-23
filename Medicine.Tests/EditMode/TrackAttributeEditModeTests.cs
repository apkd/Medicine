using Medicine;
using Medicine.Internal;
using NUnit.Framework;
using UnityEngine;
using static System.Reflection.BindingFlags;
using Object = UnityEngine.Object;

public sealed partial class TrackAttributeEditModeTests
{
    [SetUp]
    public void Cleanup()
        => TestUtility.DestroyAllGameObjects();

    [Track(cacheEnabledState: true)]
    sealed partial class MBTrackCacheEnabledState : MonoBehaviour { }

    [Track, ExecuteAlways]
    sealed partial class MBTrackExecuteAlwaysFastPath : MonoBehaviour { }

    [Track]
    partial interface ITrackMixedExecuteAlwaysInterface { }

    [Track, ExecuteAlways]
    sealed partial class MBTrackMixedExecuteAlwaysInterface : MonoBehaviour, ITrackMixedExecuteAlwaysInterface { }

    [Track]
    sealed partial class MBTrackMixedRegularInterface : MonoBehaviour, ITrackMixedExecuteAlwaysInterface { }

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

    [Test]
    public void Track_ExecuteAlways_UsesRegisteredInstancesInEditMode()
    {
        var obj = new GameObject(nameof(MBTrackExecuteAlwaysFastPath), typeof(MBTrackExecuteAlwaysFastPath));
        var component = obj.GetComponent<MBTrackExecuteAlwaysFastPath>();

        try
        {
            Assert.That(Utility.TypeInfo<MBTrackExecuteAlwaysFastPath>.IsExecuteAlways, Is.True);
            Assert.That(MBTrackExecuteAlwaysFastPath.Instances, Has.Count.EqualTo(1));

            Storage.Instances<MBTrackExecuteAlwaysFastPath>.List.Clear();

            Assert.That(MBTrackExecuteAlwaysFastPath.Instances, Has.Count.EqualTo(0));

            Storage.Instances<MBTrackExecuteAlwaysFastPath>.Register(component);
        }
        finally
        {
            Object.DestroyImmediate(obj);
        }
    }

    [Test]
    public void Track_EditModeFallback_DoesNotPopulateCallbackStorage()
    {
        var obj = new GameObject(nameof(MBTrackCacheEnabledState), typeof(MBTrackCacheEnabledState));

        try
        {
            Assert.That(MBTrackCacheEnabledState.Instances, Has.Count.EqualTo(1));
            Assert.That(MBTrackCacheEnabledState.Instances.Unsafe.AsList(), Has.Count.EqualTo(1));
            Assert.That(Storage.Instances<MBTrackCacheEnabledState>.List, Has.Count.EqualTo(0));
        }
        finally
        {
            Object.DestroyImmediate(obj);
        }
    }

    [Test]
    public void Track_MixedExecuteAlwaysInterface_SplitsEditModeFallbackAndCallbackStorage()
    {
        var executeAlwaysObj = new GameObject(
            nameof(MBTrackMixedExecuteAlwaysInterface),
            typeof(MBTrackMixedExecuteAlwaysInterface)
        );

        var regularObj = new GameObject(
            nameof(MBTrackMixedRegularInterface),
            typeof(MBTrackMixedRegularInterface)
        );

        var executeAlways = executeAlwaysObj.GetComponent<MBTrackMixedExecuteAlwaysInterface>();

        try
        {
            var instances = Find.Instances<ITrackMixedExecuteAlwaysInterface>();

            Assert.That(instances.Count, Is.EqualTo(2));
            Assert.That(instances.Unsafe.AsList(), Has.Count.EqualTo(2));
            Assert.That(Storage.Instances<ITrackMixedExecuteAlwaysInterface>.List, Has.Count.EqualTo(1));
            Assert.That(Storage.Instances<ITrackMixedExecuteAlwaysInterface>.List[0], Is.EqualTo(executeAlways));
        }
        finally
        {
            Object.DestroyImmediate(executeAlwaysObj);
            Object.DestroyImmediate(regularObj);
        }
    }

}
