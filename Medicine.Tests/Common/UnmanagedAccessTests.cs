#if MODULE_BURST
using Medicine;
using NUnit.Framework;
using UnityEngine;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Medicine.Internal;
using static System.Reflection.BindingFlags;

[SuppressMessage("ReSharper", "FieldCanBeMadeReadOnly.Global")]
[SuppressMessage("ReSharper", "FieldCanBeMadeReadOnly.Local")]
[SuppressMessage("ReSharper", "ConvertToConstant.Global")]
[SuppressMessage("ReSharper", "ConvertToConstant.Local")]
[SuppressMessage("ReSharper", "HeapView.BoxingAllocation")]
public partial class UnmanagedAccessTests
{
    static bool HasIsReadOnlyAttribute(ParameterInfo parameterInfo)
        => parameterInfo.GetRequiredCustomModifiers().Any(modifier => modifier.Name == "IsReadOnlyAttribute") ||
           parameterInfo.GetCustomAttributes(inherit: false).Any(attribute => attribute.GetType().Name == "IsReadOnlyAttribute");

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

        Assert.That(unmanagedRef.IsInvalid(), Is.False);
        Assert.That(unmanagedRef.IsValid(), Is.True);
        Assert.That(access.IsInvalid, Is.False);
        Assert.That(access.IsValid, Is.True);

        Assert.That(access.SomeValue, Is.EqualTo(5));
        UnityEngine.Object.DestroyImmediate(gameObject);

        Assert.That(unmanagedRef.IsInvalid(), Is.True);
        Assert.That(unmanagedRef.IsValid(), Is.False);
        Assert.That(access.IsValid, Is.False);
        Assert.That(access.IsInvalid, Is.True);

        unmanagedRef = default;

        Assert.That(unmanagedRef.IsInvalid(), Is.True);
        Assert.That(unmanagedRef.IsValid(), Is.False);
        Assert.That(access.IsValid, Is.False);
        Assert.That(access.IsInvalid, Is.True);
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

    [Test]
    public void UnmanagedAccess_BasicFields_ReadOnly()
    {
        var obj = new BasicFields();
        UnmanagedRef<BasicFields> unmanagedRef = obj;
        var access = unmanagedRef.AccessRO();

        Assert.That(access.IntField, Is.EqualTo(10));
        Assert.That(access.FloatField, Is.EqualTo(20.0f));
        Assert.That(access.BoolField, Is.True);
        Assert.That(access.InternalDoubleField, Is.EqualTo(30.0));
        Assert.That(access.privateIntField, Is.EqualTo(40));
    }

    [UnmanagedAccess]
    public partial class LayoutSwapFields
    {
        public int First = 1;
        public int Second = 2;
    }

    [Test]
    public void UnmanagedAccess_ExplicitLayoutRef_SwapsFields()
    {
        var obj = new LayoutSwapFields { First = 10, Second = 20 };
        UnmanagedRef<LayoutSwapFields> unmanagedRef = obj;
        var swappedLayout = new LayoutSwapFields.Unmanaged.Layout
        {
            First = LayoutSwapFields.Unmanaged.ClassLayout.Second,
            Second = LayoutSwapFields.Unmanaged.ClassLayout.First,
        };

        var accessRO = unmanagedRef.AccessRO(ref swappedLayout);
        Assert.That(accessRO.First, Is.EqualTo(20));
        Assert.That(accessRO.Second, Is.EqualTo(10));

        var accessRW = unmanagedRef.AccessRW(ref swappedLayout);
        accessRW.First = 100;
        accessRW.Second = 200;

        Assert.That(obj.First, Is.EqualTo(200));
        Assert.That(obj.Second, Is.EqualTo(100));
    }

    [UnmanagedAccess]
    public partial class ReadOnlyFields
    {
        public readonly int ReadOnlyValue = 5;
        public int WritableValue = 7;
    }

    [Test]
    public void UnmanagedAccess_AccessRO_ReturnsReadOnlyRefs()
    {
#if !ENABLE_IL2CPP
        var accessROProperty
            = typeof(ReadOnlyFields.Unmanaged.AccessRO)
                .GetProperty(nameof(ReadOnlyFields.WritableValue));

        Assert.That(accessROProperty, Is.Not.Null);
        Assert.That(HasIsReadOnlyAttribute(accessROProperty!.GetMethod!.ReturnParameter), Is.True);

        var accessRWReadOnlyProperty
            = typeof(ReadOnlyFields.Unmanaged.AccessRW)
                .GetProperty(nameof(ReadOnlyFields.ReadOnlyValue));

        Assert.That(accessRWReadOnlyProperty, Is.Not.Null);
        Assert.That(HasIsReadOnlyAttribute(accessRWReadOnlyProperty!.GetMethod!.ReturnParameter), Is.True);

        var accessRWWritableProperty
            = typeof(ReadOnlyFields.Unmanaged.AccessRW)
                .GetProperty(nameof(ReadOnlyFields.WritableValue));

        Assert.That(accessRWWritableProperty, Is.Not.Null);
        Assert.That(HasIsReadOnlyAttribute(accessRWWritableProperty!.GetMethod!.ReturnParameter), Is.False);
#else
        Assert.Inconclusive("Fails to compile under IL2CPP for some reason");
#endif
    }

    [UnmanagedAccess(includePrivate: false, includePublic: true)]
    public partial class FilterPublicOnlyFields
    {
        public int PublicField = 1;
        int privateField = 2;
    }

    [UnmanagedAccess(includePrivate: true, includePublic: false)]
    public partial class FilterPrivateOnlyFields
    {
        public int PublicField = 1;
        int privateField = 2;
    }

    [UnmanagedAccess("IncludedOne", "IncludedTwo")]
    public partial class FilterMemberNamesFields
    {
        public int IncludedOne = 10;
        public int IncludedTwo = 20;
        public int Excluded = 30;
    }

    [UnmanagedAccess(safetyChecks: false)]
    public partial class NoSafetyChecksFields
    {
        public int Value = 1;
    }

    [Test]
    public void UnmanagedAccess_Filters_PublicOnly()
    {
        var accessType = typeof(FilterPublicOnlyFields.Unmanaged.AccessRW);
        Assert.That(accessType.GetProperty("PublicField"), Is.Not.Null);
        Assert.That(accessType.GetProperty("privateField"), Is.Null);
    }

    [Test]
    public void UnmanagedAccess_Filters_PrivateOnly()
    {
        var accessType = typeof(FilterPrivateOnlyFields.Unmanaged.AccessRW);
        Assert.That(accessType.GetProperty("PublicField"), Is.Null);
        Assert.That(accessType.GetProperty("privateField"), Is.Not.Null);
    }

    [Test]
    public void UnmanagedAccess_Filters_MemberNames()
    {
        var accessType = typeof(FilterMemberNamesFields.Unmanaged.AccessRW);
        Assert.That(accessType.GetProperty(nameof(FilterMemberNamesFields.IncludedOne)), Is.Not.Null);
        Assert.That(accessType.GetProperty(nameof(FilterMemberNamesFields.IncludedTwo)), Is.Not.Null);
        Assert.That(accessType.GetProperty(nameof(FilterMemberNamesFields.Excluded)), Is.Null);
    }

    [Test]
    public void UnmanagedAccess_SafetyChecksDisabled_SkipsCheckMethods()
    {
        var accessRWType = typeof(NoSafetyChecksFields.Unmanaged.AccessRW);
        var accessROType = typeof(NoSafetyChecksFields.Unmanaged.AccessRO);

        Assert.That(accessRWType.GetMethod("CheckNull", Instance | NonPublic), Is.Null);
        Assert.That(accessROType.GetMethod("CheckNull", Instance | NonPublic), Is.Null);
    }

    [UnmanagedAccess]
    public partial class AutoPropertyFields
    {
        public int AutoInt { get; set; } = 7;
        public BasicFields AutoRef { get; set; } = new() { IntField = 55 };
        public int ManualField = 3;
    }

    [Test]
    public void UnmanagedAccess_AutoProperties_ReadWrite()
    {
        var other = new BasicFields { IntField = 9 };
        var obj = new AutoPropertyFields { AutoInt = 5, AutoRef = other };
        UnmanagedRef<AutoPropertyFields> unmanagedRef = obj;
        var access = unmanagedRef.AccessRW();

        Assert.That(access.AutoInt, Is.EqualTo(5));
        Assert.That(access.AutoRef.AccessRO().IntField, Is.EqualTo(9));

        access.AutoInt = 42;
        var newOther = new BasicFields { IntField = 99 };
        access.AutoRef = newOther;

        Assert.That(obj.AutoInt, Is.EqualTo(42));
        Assert.That(obj.AutoRef, Is.SameAs(newOther));
    }

    [Test]
    public void UnmanagedAccess_AutoProperties_DeclaredAtMetadata()
    {
        var autoIntProperty
            = typeof(AutoPropertyFields.Unmanaged.AccessRW)
                .GetProperty(nameof(AutoPropertyFields.AutoInt));

        Assert.That(autoIntProperty, Is.Not.Null);
        Assert.That(autoIntProperty!.GetCustomAttributes(typeof(DeclaredAtAttribute), false), Is.Not.Empty);

        var manualFieldProperty
            = typeof(AutoPropertyFields.Unmanaged.AccessRW)
                .GetProperty(nameof(AutoPropertyFields.ManualField));

        Assert.That(manualFieldProperty, Is.Not.Null);
        Assert.That(manualFieldProperty!.GetCustomAttributes(typeof(DeclaredAtAttribute), false), Is.Empty);

        var autoIntReadOnlyProperty
            = typeof(AutoPropertyFields.Unmanaged.AccessRO)
                .GetProperty(nameof(AutoPropertyFields.AutoInt));

        Assert.That(autoIntReadOnlyProperty, Is.Not.Null);
        Assert.That(autoIntReadOnlyProperty!.GetCustomAttributes(typeof(DeclaredAtAttribute), false), Is.Not.Empty);
    }

    [Track]
    [UnmanagedAccess]
    public partial class TrackedAccessComponent : MonoBehaviour
    {
        public int Value = 1;
    }

    [Test]
    public void UnmanagedAccess_TrackedAccessArray_Enumerators()
    {
        TestUtility.DestroyAllGameObjects();

        try
        {
            var go1 = new GameObject("TrackedAccess1", typeof(TrackedAccessComponent));
            var go2 = new GameObject("TrackedAccess2", typeof(TrackedAccessComponent));
            var c1 = go1.GetComponent<TrackedAccessComponent>();
            var c2 = go2.GetComponent<TrackedAccessComponent>();
            c1.Value = 1;
            c2.Value = 2;

            Assert.That(TrackedAccessComponent.Instances.Count, Is.EqualTo(2));

            var accessArray = TrackedAccessComponent.Unmanaged.Instances;
            Assert.That(accessArray.Length, Is.EqualTo(2));

            int sum = 0;
            foreach (var access in accessArray)
            {
                sum += access.Value;
                access.Value += 10;
            }

            Assert.That(sum, Is.EqualTo(3));
            Assert.That(new[] { c1.Value, c2.Value }, Is.EquivalentTo(new[] { 11, 12 }));

            var readOnly = accessArray.AsReadOnly();
            Assert.That(readOnly.Length, Is.EqualTo(accessArray.Length));

            int readOnlySum = 0;
            foreach (var access in readOnly)
                readOnlySum += access.Value;

            Assert.That(readOnlySum, Is.EqualTo(23));
            Assert.That(new[] { accessArray[0].Value, accessArray[1].Value }, Is.EquivalentTo(new[] { 11, 12 }));
            Assert.That(new[] { readOnly[0].Value, readOnly[1].Value }, Is.EquivalentTo(new[] { 11, 12 }));
        }
        finally
        {
            TestUtility.DestroyAllGameObjects();
        }
    }

    [Test]
    public void UnmanagedRef_Read_UsesLayoutOffsets()
    {
        var obj = new BasicFields();
        UnmanagedRef<BasicFields> unmanagedRef = obj;
        ref var layout = ref BasicFields.Unmanaged.ClassLayout;

        Assert.That(unmanagedRef.Read<int>(layout.IntField), Is.EqualTo(10));
        Assert.That(unmanagedRef.Read<float>(layout.FloatField), Is.EqualTo(20.0f));

        unmanagedRef.Read<int>(layout.IntField) = 77;
        unmanagedRef.Read<double>(layout.InternalDoubleField) = 123.0;

        Assert.That(obj.IntField, Is.EqualTo(77));
        Assert.That(obj.InternalDoubleField, Is.EqualTo(123.0));
    }

    [Test]
    public void UnmanagedRef_Equals_UsesPointerValue()
    {
        var obj = new BasicFields();
        UnmanagedRef<BasicFields> unmanagedRefA = obj;
        UnmanagedRef<BasicFields> unmanagedRefB = obj;

        Assert.That(unmanagedRefA.Equals(unmanagedRefB), Is.True);
        Assert.That(unmanagedRefA.Equals(new(IntPtr.Zero)), Is.False);
    }
}
#endif