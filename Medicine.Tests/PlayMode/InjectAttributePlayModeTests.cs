#nullable enable
using System.Diagnostics.CodeAnalysis;
using Medicine;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using static System.Reflection.BindingFlags;

[SuppressMessage("ReSharper", "ArrangeStaticMemberQualifier")]
[SuppressMessage("ReSharper", "RedundantNameQualifier")]
public partial class InjectAttributePlayModeTests
{
    [SetUp]
    public void Cleanup()
        => TestUtility.DestroyAllGameObjects();

    //////////////////////////////////////////////////////////////////////////////

    sealed partial class MBInjectBasic : MonoBehaviour
    {
        [Inject]
        void Awake()
            => Transform = GetComponent<Transform>();
    }

    [Test]
    public void Inject_Basic()
    {
        var obj = new GameObject(null, typeof(MBInjectBasic));
        Assert.That(obj.GetComponent<MBInjectBasic>().Transform, Is.Not.Null);
    }

    //////////////////////////////////////////////////////////////////////////////

    sealed partial class MBInjectExpressionBodied : MonoBehaviour
    {
        void Awake()
        {
            [Inject]
            void Init1()
                => Transform1 = GetComponent<Transform>();

            Init1();
        }

        [Inject]
        public void Init2()
            => Transform2 = GetComponent<Transform>();
    }

    [Test]
    public void Inject_LocalMethod()
    {
        var obj = new GameObject(null, typeof(MBInjectExpressionBodied));
        var mb = obj.GetComponent<MBInjectExpressionBodied>();
        mb.Init2();
        Assert.That(mb.Transform1, Is.Not.Null);
        Assert.That(mb.Transform2, Is.Not.Null);
    }

    //////////////////////////////////////////////////////////////////////////////

    sealed partial class MBInjectOptional : MonoBehaviour
    {
        void Awake()
        {
            [Inject]
            void Init1()
                => RB1 = GetComponent<Rigidbody>().Optional();

            [Inject]
            void Init2()
                => RB2 = GetComponent<Rigidbody>(); // optional

            Init1();
            Init2();
        }

        [Inject]
        void Init3()
            => RB3 = GetComponent<Rigidbody>(); // optional

        [Inject]
        void Init4()
            => RB4 = GetComponent<Rigidbody>().Optional();
    }

    [Test]
    public void Inject_Optional()
    {
        var obj = new GameObject(null, typeof(MBInjectOptional));
        var mb = obj.GetComponent<MBInjectOptional>();
        // accessing nonexistent component should not throw
        Assert.That(mb.RB1, Is.Null);
        Assert.That(mb.RB2, Is.Null);
        Assert.That(mb.RB3, Is.Null);
        Assert.That(mb.RB4, Is.Null);
    }

    //////////////////////////////////////////////////////////////////////////////

    sealed partial class MBInjectManual : MonoBehaviour
    {
        [Inject]
        public void DoInject()
            => Transform = GetComponent<Transform>();
    }

    [Test]
    public void Inject_Manual()
    {
        var obj = new GameObject(null, typeof(MBInjectManual));
        var mb = obj.GetComponent<MBInjectManual>();
        Assert.That(mb.Transform, Is.Null);
        mb.DoInject();
        Assert.That(mb.Transform, Is.Not.Null);
    }

    //////////////////////////////////////////////////////////////////////////////

    sealed partial class MBInjectMultiple : MonoBehaviour
    {
        [Inject]
        public void DoInject1()
            => Transform1 = GetComponent<Transform>();

        [Inject]
        public void DoInject2()
            => Transform2 = GetComponent<Transform>();

        [Inject(makePublic: false)]
        public void DoInject3()
            => Transform3 = GetComponent<Transform>();
    }

    [Test]
    public void Inject_Multiple()
    {
        var obj = new GameObject(null, typeof(MBInjectMultiple));
        var mb = obj.GetComponent<MBInjectMultiple>();
        Assert.That(mb.Transform1, Is.Null);
        Assert.That(mb.Transform2, Is.Null);
        Assert.That(mb, Has.Property("Transform3").With.Null);

        mb.DoInject1();
        Assert.That(mb.Transform1, Is.Not.Null);

        mb.DoInject2();
        Assert.That(mb.Transform2, Is.Not.Null);

        mb.DoInject3();
        Assert.That(typeof(MBInjectMultiple).GetProperty("Transform3", NonPublic | Instance), Is.Not.Null);
        Assert.That(mb, Has.Property("Transform3").With.Not.Null);
    }

    //////////////////////////////////////////////////////////////////////////////

    abstract partial class MBInjectInheritanceBase : MonoBehaviour
    {
        [Inject]
        protected void Awake()
            => Transform1 = GetComponent<Transform>();
    }

    sealed partial class MBInjectInheritanceDerived : MBInjectInheritanceBase
    {
        [Inject]
        new void Awake()
        {
            base.Awake();
            Transform2 = GetComponent<Transform>();
        }
    }

    [Test]
    public void Inject_Inheritance()
    {
        var obj = new GameObject(null, typeof(MBInjectInheritanceDerived));
        Assert.That(obj.GetComponent<MBInjectInheritanceDerived>().Transform1, Is.Not.Null);
        Assert.That(obj.GetComponent<MBInjectInheritanceDerived>().Transform2, Is.Not.Null);
    }

    //////////////////////////////////////////////////////////////////////////////

    abstract partial class MBInjectGenericBase<TInjected> : MonoBehaviour
    {
        [Inject]
        protected void Awake()
            => Generic = GetComponent<TInjected>();
    }

    sealed class MBInjectGenericDerived : MBInjectGenericBase<Transform> { }

    [Test]
    public void Inject_Generic()
    {
        var obj = new GameObject(null, typeof(MBInjectGenericDerived));
        Assert.That(obj.GetComponent<MBInjectGenericDerived>().Generic, Is.Not.Null);
    }

    //////////////////////////////////////////////////////////////////////////////

    static partial class StaticContainer
    {
        [Inject]
        public static void DoInject()
        {
            EnumerateInstances = Find.ComponentsInScene<MBTracked>(SceneManager.GetActiveScene());
            SingletonInstance = Find.Singleton<MBSingleton>();
            TrackedInstances = Find.Instances<MBTracked>();
        }
    }

    [Test]
    public void Inject_Static()
    {
        Assert.That(StaticContainer.SingletonInstance, Is.Null);
        Assert.That(StaticContainer.TrackedInstances, Has.Count.EqualTo(0));
        var go = new GameObject(null, typeof(MBSingleton), typeof(MBTracked));
        Assert.That(StaticContainer.SingletonInstance, Is.Not.Null);
        Assert.That(StaticContainer.TrackedInstances, Has.Count.EqualTo(1));
    }

    //////////////////////////////////////////////////////////////////////////////

    [Singleton]
    sealed partial class MBSingleton : MonoBehaviour
    {
        [Inject]
        void Awake()
        {
            VariantFind1 = Find.Singleton<MBSingleton>();
            VariantFind2 = Find.Singleton<MBSingleton>().Optional();
            VariantFind3 = Medicine.Find.Singleton<MBSingleton>();
            VariantFind4 = global::Medicine.Find.Singleton<MBSingleton>();
            VariantFind5 = global::Medicine.Find.Singleton<MBSingleton>().Optional();
            Variant1 = Instance;
            Variant2 = Instance.Optional();
            Variant3 = MBSingleton.Instance;
            Variant4 = InjectAttributePlayModeTests.MBSingleton.Instance;
            Variant5 = global::InjectAttributePlayModeTests.MBSingleton.Instance;
            Variant6 = global::InjectAttributePlayModeTests.MBSingleton.Instance.Optional();
        }
    }

    [Test]
    public void Inject_Singleton()
    {
        var obj = new GameObject(null, typeof(MBSingleton));
        var mb = obj.GetComponent<MBSingleton>();
        Assert.That(mb.VariantFind1, Is.Not.Null);
        Assert.That(mb.VariantFind2, Is.Not.Null);
        Assert.That(mb.VariantFind3, Is.Not.Null);
        Assert.That(mb.VariantFind4, Is.Not.Null);
        Assert.That(mb.VariantFind5, Is.Not.Null);
        Assert.That(mb.Variant1, Is.Not.Null);
        Assert.That(mb.Variant2, Is.Not.Null);
        Assert.That(mb.Variant3, Is.Not.Null);
        Assert.That(mb.Variant4, Is.Not.Null);
        Assert.That(mb.Variant5, Is.Not.Null);
        Assert.That(mb.Variant6, Is.Not.Null);
    }

    //////////////////////////////////////////////////////////////////////////////

    [Track]
    sealed partial class MBTracked : MonoBehaviour
    {
        [Inject]
        void Awake()
        {
            VariantFind1 = Find.Instances<MBTracked>();
            VariantFind2 = Medicine.Find.Instances<MBTracked>();
            VariantFind3 = global::Medicine.Find.Instances<MBTracked>();
            Variant1 = Instances;
            Variant2 = MBTracked.Instances;
            Variant3 = InjectAttributePlayModeTests.MBTracked.Instances;
            Variant4 = global::InjectAttributePlayModeTests.MBTracked.Instances;
        }
    }

    [Test]
    public void Inject_Tracked()
    {
        for (int i = 1; i < 5; ++i)
        {
            var obj = new GameObject(null, typeof(MBTracked));
            var mb = obj.GetComponent<MBTracked>();
            Assert.That(mb.VariantFind1, Has.Count.EqualTo(i));
            Assert.That(mb.VariantFind2, Has.Count.EqualTo(i));
            Assert.That(mb.VariantFind3, Has.Count.EqualTo(i));
            Assert.That(mb.Variant1, Has.Count.EqualTo(i));
            Assert.That(mb.Variant2, Has.Count.EqualTo(i));
            Assert.That(mb.Variant3, Has.Count.EqualTo(i));
            Assert.That(mb.Variant4, Has.Count.EqualTo(i));
        }
    }

    //////////////////////////////////////////////////////////////////////////////

    sealed partial class MBReferToVar : MonoBehaviour
    {
        [Inject]
        void Awake()
        {
            bool variable = true;
            colliders = GetComponentsInChildren<Collider>(variable);
        }
    }

    sealed partial class MBReferToVarDependent : MonoBehaviour
    {
        [Inject]
        void Awake()
        {
            int childCount = transform.childCount;
            bool includeInactive = childCount >= 0;
            colliders = GetComponentsInChildren<Collider>(includeInactive);
        }
    }

    sealed partial class MBReferToVarMultiDeclarator : MonoBehaviour
    {
        [Inject]
        void Awake()
        {
            int min = 0, max = 1;
            bool includeInactive = min < max;
            colliders = GetComponentsInChildren<Collider>(includeInactive);
        }
    }

    sealed partial class MBReferToVarChained : MonoBehaviour
    {
        [Inject]
        void Awake()
        {
            var includeInactive = transform.childCount > 0;
            var includeInactiveOrSelf = includeInactive || gameObject.activeSelf;
            colliders = GetComponentsInChildren<Collider>(includeInactiveOrSelf);
        }
    }

    [Test]
    public void Inject_ReferToVar()
    {
        T Spawn<T>() where T : Component
        {
            var go = new GameObject(typeof(T).Name);
            go.AddComponent<BoxCollider>();
            go.AddComponent<SphereCollider>();
            return go.AddComponent<T>();
        }

        Assert.That(Spawn<MBReferToVar>().colliders, Has.Length.EqualTo(2));
        Assert.That(Spawn<MBReferToVarDependent>().colliders, Has.Length.EqualTo(2));
        Assert.That(Spawn<MBReferToVarMultiDeclarator>().colliders, Has.Length.EqualTo(2));
        Assert.That(Spawn<MBReferToVarChained>().colliders, Has.Length.EqualTo(2));
    }

    //////////////////////////////////////////////////////////////////////////////

    sealed partial class MBInjectArrayFallback : MonoBehaviour
    {
        [Inject]
        void Awake()
        {
            colliders = GetNullArray();
        }

        static Collider[]? GetNullArray()
            => null;
    }

    [Test]
    public void Inject_ArrayFallbacksToEmpty()
    {
        var obj = new GameObject(null, typeof(MBInjectArrayFallback));
        var mb = obj.GetComponent<MBInjectArrayFallback>();
        Assert.That(mb.colliders, Is.Not.Null);
        Assert.That(mb.colliders, Is.Empty);
    }
}