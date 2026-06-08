using Medicine;
using Medicine.Internal;
using NUnit.Framework;
using UnityEngine;
using static System.Reflection.BindingFlags;

public sealed partial class TrackAttributeEditModeTests
{
    [SetUp]
    public void Cleanup()
        => TestUtility.DestroyAllGameObjects();

    [Track(cacheEnabledState: true)]
    sealed partial class MBTrackCacheEnabledState : MonoBehaviour { }

    [Track, ExecuteAlways]
    sealed partial class MBTrackExecuteAlwaysFastPath : MonoBehaviour { }

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
}
