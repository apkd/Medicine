using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Medicine
{
    public sealed class InjectGenericTests
    {
        public abstract class TestCompGenericBase : MonoBehaviour
        {
            [Inject]
            public TestComp TestCompInBase { get; }

            [Inject.FromChildren]
            public TestComp[] TestCompManyInBase { get; }

            [Inject.Single]
            public SingletonMB TestSingletonInBase { get; }
        }

        public abstract class TestCompGeneric<TSomeGeneric> : TestCompGenericBase
        {
            // ReSharper disable once NotAccessedField.Local
            [SerializeField]
            TSomeGeneric field;

            [Inject]
            public TestComp TestComp { get; }

            [Inject.FromChildren]
            public TestComp[] TestCompMany { get; }

            [Inject.Single]
            public SingletonMB TestSingleton { get; }
        }

        public sealed class TestCompGenericClosed : TestCompGeneric<float>
        {
            [Inject]
            public TestComp TestCompInDerived { get; }

            [Inject.FromChildren]
            public TestComp[] TestCompManyInDerived { get; }

            [Inject.Single]
            public SingletonMB TestSingletonInDerived { get; }
        }

        [Register.Single]
        public sealed class SingletonMB : MonoBehaviour { }

        [Test, TestMustExpectAllLogs]
        public void InjectTest()
        {
            var instance = new GameObject()
                .WithComponent<TestCompDerived>()
                .WithComponent<SingletonMB>()
                .AddComponent<TestCompGenericClosed>();

            Assert.IsTrue(instance.TestComp);
            Assert.IsNotEmpty(instance.TestCompMany);

            Assert.IsTrue(instance.TestCompInDerived);
            Assert.IsNotEmpty(instance.TestCompManyInDerived);

            Assert.IsTrue(instance.TestCompInBase);
            Assert.IsNotEmpty(instance.TestCompManyInBase);

            Assert.IsTrue(instance.TestSingleton);
            Assert.IsTrue(instance.TestSingletonInDerived);
            Assert.IsTrue(instance.TestSingletonInBase);
        }
    }
}
