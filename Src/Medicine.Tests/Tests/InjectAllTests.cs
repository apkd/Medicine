using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// ReSharper disable ClassNeverInstantiated.Global
namespace Medicine
{
    public sealed class InjectAllTests
    {
        public sealed class InjectAllMB : MonoBehaviour
        {
            [Inject.All]
            public SomeMB[] Instances { get; }
        }

        public sealed class InjectAllPOCO
        {
            [Inject.All]
            public static SomeMB[] Instances { get; }
        }

        public sealed class InjectAllStaticPOCO
        {
            [Inject.All]
            public static SomeMB[] Instances { get; }
        }

        public static class InjectAllStaticPOCS
        {
            [Inject.All]
            public static SomeMB[] Instances { get; }
        }

        [Register.All]
        public sealed class SomeMB : MonoBehaviour { }

        [Test, TestMustExpectAllLogs]
        public void InjectAllTest()
        {
            var component = new GameObject().AddComponent<InjectAllMB>();
            Assert.AreEqual(0, component.Instances.Length);

            var a = new GameObject().AddComponent<SomeMB>();
            Assert.AreEqual(1, component.Instances.Length);

            var b = new GameObject().AddComponent<SomeMB>();
            Assert.AreEqual(2, component.Instances.Length);

            var c = new GameObject().AddComponent<SomeMB>();
            Assert.AreEqual(3, component.Instances.Length);

            Object.Destroy(a.gameObject);
            Assert.AreEqual(2, component.Instances.Length);

            Object.Destroy(b.gameObject);
            Assert.AreEqual(1, component.Instances.Length);

            Object.Destroy(c.gameObject);
            Assert.AreEqual(0, component.Instances.Length);

            Object.Destroy(component.gameObject);
        }

        [Test, TestMustExpectAllLogs]
        public void InjectAllStaticPOCOTest()
        {
            Assert.AreEqual(0, InjectAllStaticPOCO.Instances.Length);

            var a = new GameObject().AddComponent<SomeMB>();
            Assert.AreEqual(1, InjectAllStaticPOCO.Instances.Length);

            var b = new GameObject().AddComponent<SomeMB>();
            Assert.AreEqual(2, InjectAllStaticPOCO.Instances.Length);

            var c = new GameObject().AddComponent<SomeMB>();
            Assert.AreEqual(3, InjectAllStaticPOCO.Instances.Length);

            Object.Destroy(a.gameObject);
            Assert.AreEqual(2, InjectAllStaticPOCO.Instances.Length);

            Object.Destroy(b.gameObject);
            Assert.AreEqual(1, InjectAllStaticPOCO.Instances.Length);

            Object.Destroy(c.gameObject);
            Assert.AreEqual(0, InjectAllStaticPOCO.Instances.Length);
        }

        [Test, TestMustExpectAllLogs]
        public void InjectAllStaticPOCSTest()
        {
            Assert.AreEqual(0, InjectAllStaticPOCS.Instances.Length);

            var a = new GameObject().AddComponent<SomeMB>();
            Assert.AreEqual(1, InjectAllStaticPOCS.Instances.Length);

            var b = new GameObject().AddComponent<SomeMB>();
            Assert.AreEqual(2, InjectAllStaticPOCS.Instances.Length);

            var c = new GameObject().AddComponent<SomeMB>();
            Assert.AreEqual(3, InjectAllStaticPOCS.Instances.Length);

            Object.Destroy(a.gameObject);
            Assert.AreEqual(2, InjectAllStaticPOCS.Instances.Length);

            Object.Destroy(b.gameObject);
            Assert.AreEqual(1, InjectAllStaticPOCS.Instances.Length);

            Object.Destroy(c.gameObject);
            Assert.AreEqual(0, InjectAllStaticPOCS.Instances.Length);
        }
    }
}
