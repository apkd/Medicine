#if MODULE_BURST
using Medicine;
using NUnit.Framework;
using UnityEngine;
using System;
using System.Diagnostics.CodeAnalysis;

[SuppressMessage("ReSharper", "FieldCanBeMadeReadOnly.Global")]
[SuppressMessage("ReSharper", "FieldCanBeMadeReadOnly.Local")]
[SuppressMessage("ReSharper", "ConvertToConstant.Global")]
[SuppressMessage("ReSharper", "ConvertToConstant.Local")]
[SuppressMessage("ReSharper", "HeapView.BoxingAllocation")]
public partial class UnmanagedAccessTests
{
    [UnmanagedAccess]
    public partial class BasicFields
    {
        public int IntField = 10;
        public float FloatField = 20.0f;
        public bool BoolField = true;
        internal double InternalDoubleField = 30.0;
        int privateIntField = 40;

        public int GetPrivateInt() => privateIntField;
    }

    [Test]
    public void UnmanagedAccess_BasicFields_ReadWrite()
    {
        var obj = new BasicFields();
        UnmanagedRef<BasicFields> unmanagedRef = obj;
        var access = unmanagedRef.AccessRW();

        Assert.That(access.IntField, Is.EqualTo(10));
        Assert.That(access.FloatField, Is.EqualTo(20.0f));
        Assert.That(access.BoolField, Is.True);
        Assert.That(access.InternalDoubleField, Is.EqualTo(30.0));
        Assert.That(access.privateIntField, Is.EqualTo(40));

        access.IntField = 100;
        access.FloatField = 200.0f;
        access.BoolField = false;
        access.InternalDoubleField = 300.0;
        access.privateIntField = 400;

        Assert.That(obj.IntField, Is.EqualTo(100));
        Assert.That(obj.FloatField, Is.EqualTo(200.0f));
        Assert.That(obj.BoolField, Is.False);
        Assert.That(obj.InternalDoubleField, Is.EqualTo(300.0));
        Assert.That(obj.GetPrivateInt(), Is.EqualTo(400));
    }

    [UnmanagedAccess]
    public partial class BaseClass
    {
        public int BaseField = 1;
    }

    [UnmanagedAccess]
    public partial class DerivedClass : BaseClass
    {
        public int DerivedField = 2;
    }

    [Test]
    public void UnmanagedAccess_Inheritance_AccessBaseFields()
    {
        var obj = new DerivedClass();
        UnmanagedRef<DerivedClass> unmanagedRef = obj;
        var access = unmanagedRef.AccessRW();

        Assert.That(access.BaseField, Is.EqualTo(1));
        Assert.That(access.DerivedField, Is.EqualTo(2));

        access.BaseField = 10;
        access.DerivedField = 20;

        Assert.That(obj.BaseField, Is.EqualTo(10));
        Assert.That(obj.DerivedField, Is.EqualTo(20));
    }

    [UnmanagedAccess]
    public partial class ReferenceFields
    {
        public BasicFields Other;
    }

    [Test]
    public void UnmanagedAccess_ReferenceFields_StoredAsUnmanagedRef()
    {
        var other = new BasicFields { IntField = 55 };
        var obj = new ReferenceFields { Other = other };
        
        UnmanagedRef<ReferenceFields> unmanagedRef = obj;
        var access = unmanagedRef.AccessRW();

        Assert.That(access.Other.Ptr, Is.EqualTo(((UnmanagedRef<BasicFields>)other).Ptr));
        Assert.That(access.Other.AccessRO().IntField, Is.EqualTo(55));

        var newOther = new BasicFields { IntField = 99 };
        access.Other = newOther;

        Assert.That(obj.Other, Is.SameAs(newOther));
    }

    [UnmanagedAccess]
    public partial class UnityObjectAccess : MonoBehaviour
    {
        public int SomeValue = 5;

        [Inject]
        void Awake()
        {
            Transform = GetComponent<Transform>();
            GameObject = gameObject;
        }
    }

    [Test]
    public void UnmanagedAccess_UnityObject()
    {
        var gameObject = new GameObject("Test", typeof(UnityObjectAccess));
        var component = gameObject.GetComponent<UnityObjectAccess>();

        UnmanagedRef<UnityObjectAccess> unmanagedRef = component;
        var access = unmanagedRef.AccessRW();

        Assert.That(unmanagedRef.GetInstanceID(), Is.EqualTo(component.GetInstanceID()));
        Assert.That(access.InstanceID, Is.EqualTo(component.GetInstanceID()));

        Assert.That(unmanagedRef.IsDestroyed(), Is.False);
        Assert.That(access.IsDestroyed, Is.False);

        Assert.That(access.SomeValue, Is.EqualTo(5));
        UnityEngine.Object.DestroyImmediate(gameObject);
        Assert.That(access.IsDestroyed, Is.True);
    }

    [Test]
    public void UnmanagedAccess_UnityObject_SafetyChecks()
    {
        var gameObject = new GameObject("Test", typeof(UnityObjectAccess));
        var component = gameObject.GetComponent<UnityObjectAccess>();
        UnmanagedRef<UnityObjectAccess> unmanagedRef = component;
        var access = unmanagedRef.AccessRW();

        UnityEngine.Object.DestroyImmediate(gameObject);
        Assert.Throws<InvalidOperationException>(() => _ = access.SomeValue);
    }

    [Test]
    public void UnmanagedAccess_NullReference_SafetyChecks()
    {
        var nullRef = new UnmanagedRef<BasicFields>(IntPtr.Zero);
        var access = nullRef.AccessRW();
        Assert.Throws<InvalidOperationException>(() => _ = access.IntField);
    }

    [Test]
    public void UnmanagedAccess_Layout_Initialized()
    {
        Assert.That(BasicFields.Unmanaged.ClassLayout.IntField, Is.GreaterThan(0));
        Assert.That(BasicFields.Unmanaged.ClassLayout.FloatField, Is.GreaterThan(0));
        Assert.That(BasicFields.Unmanaged.ClassLayout.privateIntField, Is.GreaterThan(0));
        Assert.That(BasicFields.Unmanaged.ClassLayout.IntField, Is.Not.EqualTo(BasicFields.Unmanaged.ClassLayout.FloatField));
        Assert.That(BasicFields.Unmanaged.ClassLayout.IntField, Is.Not.EqualTo(BasicFields.Unmanaged.ClassLayout.privateIntField));
    }
}
#endif