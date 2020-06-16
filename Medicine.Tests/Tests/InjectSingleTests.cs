using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// ReSharper disable ClassNeverInstantiated.Global
namespace Medicine
{
    public sealed class InjectSingleTests
    {
        public sealed class InjectSingleMB : MonoBehaviour
        {
            [Inject.Single]
            public SingletonMB Singleton { get; }
        }

        public sealed class InjectSinglePOCO
        {
            [Inject.Single]
            public static SingletonMB Singleton { get; }
        }

        public sealed class InjectSingleStaticPOCO
        {
            [Inject.Single]
            public static SingletonMB Singleton { get; }
        }

        public static class InjectSingleStaticPOCS
        {
            [Inject.Single]
            public static SingletonMB Singleton { get; }
        }

        [Register.Single]
        public sealed class SingletonMB : MonoBehaviour { }

        [Test, TestMustExpectAllLogs]
        public void InjectSingleTest()
        {
            var singleton = new GameObject()
                .AddComponent<SingletonMB>();

            var component = new GameObject()
                .AddComponent<InjectSingleMB>();

            Assert.IsTrue(component.Singleton);
            Assert.AreSame(singleton, component.Singleton);

            Object.Destroy(component.gameObject);
            Object.Destroy(singleton.gameObject);
        }

        [Test, TestMustExpectAllLogs]
        public void InjectSingleDuplicateTest()
        {
            var singleton1 = new GameObject()
                .AddComponent<SingletonMB>();
            var singleton2 = new GameObject()
                .AddComponent<SingletonMB>();

            var component = new GameObject()
                .AddComponent<InjectSingleMB>();

            LogAssert.Expect(LogType.Error, new Regex("Failed to register singleton instance .*: a registered instance already exists.*"));
            Assert.AreSame(singleton1, component.Singleton);

            Object.Destroy(component.gameObject);
            Object.Destroy(singleton1.gameObject);
            Object.Destroy(singleton2.gameObject);
        }

        [Test, TestMustExpectAllLogs]
        public void InjectSingleMissingTest()
        {
            var component = new GameObject()
                .AddComponent<InjectSingleMB>();

            LogAssert.Expect(LogType.Error, new Regex("No registered singleton instance:.*"));
            Assert.IsFalse(component.Singleton);

            Object.Destroy(component.gameObject);
        }

        [Test, TestMustExpectAllLogs]
        public void InjectSingleStaticPOCOTest()
        {
            var singleton = new GameObject()
                .AddComponent<SingletonMB>();

            Assert.IsTrue(InjectSingleStaticPOCO.Singleton);

            Object.Destroy(singleton.gameObject);
        }

        [Test, TestMustExpectAllLogs]
        public void InjectSingleStaticPOCOMissingTest()
        {
            Assert.IsFalse(InjectSingleStaticPOCO.Singleton);
            LogAssert.Expect(LogType.Error, new Regex("No registered singleton instance:.*"));
        }

        [Test, TestMustExpectAllLogs]
        public void InjectSingleStaticPOCSTest()
        {
            var singleton = new GameObject()
                .AddComponent<SingletonMB>();

            Assert.IsTrue(InjectSingleStaticPOCS.Singleton);

            Object.Destroy(singleton.gameObject);
        }

        [Test, TestMustExpectAllLogs]
        public void InjectSingleStaticPOCSMissingTest()
        {
            Assert.IsFalse(InjectSingleStaticPOCS.Singleton);
            LogAssert.Expect(LogType.Error, new Regex("No registered singleton instance:.*"));
        }
    }
}
