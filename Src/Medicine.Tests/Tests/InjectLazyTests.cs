using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Medicine
{
    public sealed class InjectLazyTests
    {
        public sealed class InjectMB : MonoBehaviour
        {
            [Inject.Lazy]
            public TestComp TestComp { get; }
        }

        [Test, TestMustExpectAllLogs]
        public void InjectTest()
        {
            var component = new GameObject()
                .WithComponent<TestCompDerived>()
                .AddComponent<InjectMB>()
                .TestComp;

            Assert.IsTrue(component);
        }

        [Test, TestMustExpectAllLogs]
        public void InjectMissingTest()
        {
            var component = new GameObject()
                .AddComponent<InjectMB>()
                .TestComp;

            Assert.IsFalse(component);
        }

        public sealed class InjectFromChildrenMB : MonoBehaviour
        {
            [Inject.FromChildren.Lazy]
            public TestComp TestComp { get; }
        }

        [Test, TestMustExpectAllLogs]
        public void InjectFromChildrenTest()
        {
            var component = new GameObject()
                .WithChild(x => x.WithComponent<TestCompDerived>())
                .AddComponent<InjectFromChildrenMB>()
                .TestComp;

            Assert.IsTrue(component);
        }

        [Test, TestMustExpectAllLogs]
        public void InjectFromChildrenMissingTest()
        {
            var component = new GameObject()
                .AddComponent<InjectFromChildrenMB>()
                .TestComp;

            Assert.IsFalse(component);
        }

        public sealed class InjectFromParentsMB : MonoBehaviour
        {
            [Inject.FromParents.Lazy]
            public TestComp TestComp { get; }
        }

        [Test, TestMustExpectAllLogs]
        public void InjectFromParentsTest()
        {
            var component = new GameObject()
                .WithParent(x => x.WithComponent<TestCompDerived>())
                .AddComponent<InjectFromParentsMB>()
                .TestComp;

            Assert.IsTrue(component);
        }

        [Test, TestMustExpectAllLogs]
        public void InjectFromParentsMissingTest()
        {
            var component = new GameObject()
                .AddComponent<InjectFromParentsMB>()
                .TestComp;

            Assert.IsFalse(component);
        }

        public sealed class InjectArrayMB : MonoBehaviour
        {
            [Inject.Lazy]
            public TestComp[] Colliders { get; }
        }

        [Test, TestMustExpectAllLogs]
        public void InjectArrayTest()
        {
            var components = new GameObject()
                .WithComponent<TestCompDerived>()
                .WithComponent<TestCompDerived>()
                .WithComponent<TestCompDerived>()
                .AddComponent<InjectArrayMB>()
                .Colliders;

            Assert.AreEqual(3, components.Length);
        }

        [Test, TestMustExpectAllLogs]
        public void InjectArrayMissingTest()
        {
            var components = new GameObject()
                .AddComponent<InjectArrayMB>()
                .Colliders;

            Assert.AreEqual(0, components.Length);
        }

        public sealed class InjectFromChildrenArrayMB : MonoBehaviour
        {
            [Inject.FromChildren.Lazy]
            public TestComp[] Colliders { get; }
        }

        [Test, TestMustExpectAllLogs]
        public void InjectFromChildrenArrayTest()
        {
            var components = new GameObject()
                .WithComponent<TestCompDerived>()
                .WithChild(
                    x => x
                        .WithComponent<TestCompDerived>()
                        .WithComponent<TestCompDerived>()
                        .WithComponent<TestCompDerived>())
                .AddComponent<InjectFromChildrenArrayMB>()
                .Colliders;

            Assert.AreEqual(4, components.Length);
        }

        [Test, TestMustExpectAllLogs]
        public void InjectFromChildrenArrayMissingTest()
        {
            var components = new GameObject()
                .AddComponent<InjectFromChildrenArrayMB>()
                .Colliders;

            Assert.AreEqual(0, components.Length);
        }

        public sealed class InjectFromParentsArrayMB : MonoBehaviour
        {
            [Inject.FromParents.Lazy]
            public TestComp[] Colliders { get; }
        }

        [Test, TestMustExpectAllLogs]
        public void InjectFromParentsArrayTest()
        {
            var components = new GameObject()
                .WithComponent<TestCompDerived>()
                .WithParent(
                    x => x
                        .WithComponent<TestCompDerived>()
                        .WithComponent<TestCompDerived>()
                        .WithComponent<TestCompDerived>())
                .AddComponent<InjectFromParentsArrayMB>()
                .Colliders;

            Assert.AreEqual(4, components.Length);
        }

        [Test, TestMustExpectAllLogs]
        public void InjectFromParentsArrayMissingTest()
        {
            var components = new GameObject()
                .AddComponent<InjectFromParentsArrayMB>()
                .Colliders;

            Assert.AreEqual(0, components.Length);
        }
    }
}
