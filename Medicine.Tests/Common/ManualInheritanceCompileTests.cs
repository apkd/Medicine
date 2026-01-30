using Medicine;
using NUnit.Framework;
using UnityEngine;

// commented-out lines will not compile (should throw MED029)
public sealed partial class ManualInheritanceCompileTests
{
    [Singleton]
    partial class MBSingletonAutoBase : MonoBehaviour { }

    // [Singleton(manual: true)]
    partial class MBSingletonManualDerivedFromAuto : MBSingletonAutoBase { }

    [Singleton(manual: true)]
    partial class MBSingletonManualBase : MonoBehaviour { }

    // [Singleton]
    partial class MBSingletonAutoDerivedFromManual : MBSingletonManualBase { }

    [Track]
    partial class MBTrackAutoBase : MonoBehaviour { }

    // [Track(manual: true)]
    partial class MBTrackManualDerivedFromAuto : MBTrackAutoBase { }

    [Track(manual: true)]
    partial class MBTrackManualBase : MonoBehaviour { }

    // [Track]
    partial class MBTrackAutoDerivedFromManual : MBTrackManualBase { }

    [Test]
    public void MixedManualAutomaticInheritance_Compiles()
    {
        _ = typeof(MBSingletonManualDerivedFromAuto);
        _ = typeof(MBSingletonAutoDerivedFromManual);
        _ = typeof(MBTrackManualDerivedFromAuto);
        _ = typeof(MBTrackAutoDerivedFromManual);
        Assert.Pass();
    }
}