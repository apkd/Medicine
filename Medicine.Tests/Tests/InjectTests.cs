using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Medicine
{
    public sealed class InjectTests
    {
        public sealed class InjectMB : MonoBehaviour
        {
            [Inject]
            public TestComp TestComp { get; }
        }

        public sealed class InjectOptionalMB : MonoBehaviour
        {
            [Inject(Optional = true)]
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
            LogAssert.Expect(LogType.Error, new Regex("Failed to initialize.*"));
        }

        [Test, TestMustExpectAllLogs]
        public void InjectMissingOptionalTest()
        {
            var component = new GameObject()
                .AddComponent<InjectOptionalMB>()
                .TestComp;

            Assert.IsFalse(component);
        }

        public sealed class InjectFromChildrenMB : MonoBehaviour
        {
            [Inject.FromChildren]
            public TestComp TestComp { get; }
            
            [Inject.FromChildren(IncludeInactive = true)]
            public TestComp TestCompIncludeInactive { get; }
        }

        public sealed class InjectFromChildrenOptionalMB : MonoBehaviour
        {
            [Inject.FromChildren(Optional = true)]
            public TestComp TestComp { get; }
            
            [Inject.FromChildren(Optional = true, IncludeInactive = true)]
            public TestComp TestCompIncludingInactive { get; }
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
        public void InjectFromChildrenIncludingInactiveTest()
        {
            var gameObject = new GameObject()
                .WithChild(x => x.WithComponent<TestCompDerived>().SetActive(false))
                .AddComponent<InjectFromChildrenMB>();
            
            Assert.IsFalse(gameObject.TestComp);
            Assert.IsTrue(gameObject.TestCompIncludeInactive);
            LogAssert.Expect(LogType.Error, new Regex("Failed to initialize.*"));
        }

        [Test, TestMustExpectAllLogs]
        public void InjectFromChildrenMissingTest()
        {
            var component = new GameObject()
                .AddComponent<InjectFromChildrenMB>()
                .TestComp;

            Assert.IsFalse(component);
            LogAssert.Expect(LogType.Error, new Regex("Failed to initialize.*"));
            LogAssert.Expect(LogType.Error, new Regex("Failed to initialize.*"));
        }

        [Test, TestMustExpectAllLogs]
        public void InjectFromChildrenMissingOptionalTest()
        {
            var component = new GameObject()
                .AddComponent<InjectFromChildrenOptionalMB>()
                .TestComp;

            Assert.IsFalse(component);
        }

        public sealed class InjectFromParentsMB : MonoBehaviour
        {
            [Inject.FromParents]
            public TestComp TestComp { get; }
        }

        public sealed class InjectFromParentsOptionalMB : MonoBehaviour
        {
            [Inject.FromParents(Optional = true)]
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
            LogAssert.Expect(LogType.Error, new Regex("Failed to initialize.*"));
        }

        [Test, TestMustExpectAllLogs]
        public void InjectFromParentsMissingOptionalTest()
        {
            var component = new GameObject()
                .AddComponent<InjectFromParentsOptionalMB>()
                .TestComp;

            Assert.IsFalse(component);
        }

        public sealed class InjectArrayMB : MonoBehaviour
        {
            [Inject]
            public TestComp[] Colliders { get; }
        }

        public sealed class InjectArrayOptionalMB : MonoBehaviour
        {
            [Inject(Optional = true)]
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
            LogAssert.Expect(LogType.Error, new Regex("Failed to initialize.*"));
        }

        [Test, TestMustExpectAllLogs]
        public void InjectArrayMissingOptionalTest()
        {
            var components = new GameObject()
                .AddComponent<InjectArrayOptionalMB>()
                .Colliders;

            Assert.AreEqual(0, components.Length);
        }

        public sealed class InjectFromChildrenArrayMB : MonoBehaviour
        {
            [Inject.FromChildren]
            public TestComp[] Colliders { get; }         
            
            [Inject.FromChildren(IncludeInactive = true)]
            public TestComp[] CollidersIncludingInactive { get; }

        }

        public sealed class InjectFromChildrenArrayOptionalMB : MonoBehaviour
        {
            [Inject.FromChildren(Optional = true)]
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
        public void InjectFromChildrenArrayIncludingInactiveTest()
        {
            var gameObject = new GameObject()
                .WithComponent<TestCompDerived>()
                .WithChild(
                    x => x
                        .WithComponent<TestCompDerived>()
                        .WithComponent<TestCompDerived>()
                        .WithComponent<TestCompDerived>()
                        .SetActive(false))
                .AddComponent<InjectFromChildrenArrayMB>();

            Assert.AreEqual(1, gameObject.Colliders.Length);
            Assert.AreEqual(4, gameObject.CollidersIncludingInactive.Length);
        }

        [Test, TestMustExpectAllLogs]
        public void InjectFromChildrenArrayMissingTest()
        {
            var components = new GameObject()
                .AddComponent<InjectFromChildrenArrayMB>()
                .Colliders;

            Assert.AreEqual(0, components.Length);
            LogAssert.Expect(LogType.Error, new Regex("Failed to initialize.*"));
            LogAssert.Expect(LogType.Error, new Regex("Failed to initialize.*"));
        }

        [Test, TestMustExpectAllLogs]
        public void InjectFromChildrenArrayMissingOptionalTest()
        {
            var components = new GameObject()
                .AddComponent<InjectFromChildrenArrayOptionalMB>()
                .Colliders;

            Assert.AreEqual(0, components.Length);
        }

        public sealed class InjectFromParentsArrayMB : MonoBehaviour
        {
            [Inject.FromParents]
            public TestComp[] Colliders { get; }
        }

        public sealed class InjectFromParentsArrayOptionalMB : MonoBehaviour
        {
            [Inject.FromParents(Optional = true)]
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
            LogAssert.Expect(LogType.Error, new Regex("Failed to initialize.*"));
        }

        [Test, TestMustExpectAllLogs]
        public void InjectFromParentsArrayMissingOptionalTest()
        {
            var components = new GameObject()
                .AddComponent<InjectFromParentsArrayOptionalMB>()
                .Colliders;

            Assert.AreEqual(0, components.Length);
        }
    }
}
