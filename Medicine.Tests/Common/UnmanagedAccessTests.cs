#if MODULE_BURST
using Medicine;
using NUnit.Framework;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Medicine.Internal;
using Unity.Collections;
using static System.Reflection.BindingFlags;

#pragma warning disable CS0414 // Field is assigned but its value is never used
[SuppressMessage("ReSharper", "FieldCanBeMadeReadOnly.Global")]
[SuppressMessage("ReSharper", "FieldCanBeMadeReadOnly.Local")]
[SuppressMessage("ReSharper", "ConvertToConstant.Global")]
[SuppressMessage("ReSharper", "ConvertToConstant.Local")]
[SuppressMessage("ReSharper", "HeapView.BoxingAllocation")]
public partial class UnmanagedAccessTests
{
    [SetUp]
    public void Cleanup()
        => TestUtility.DestroyAllGameObjects();

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

    [Test]
    public void UnmanagedAccess_Inheritance_CastHelpers()
    {
        var obj = new DerivedClass();
        UnmanagedRef<DerivedClass> derivedRef = obj;
        UnmanagedRef<BaseClass> baseRef = obj;

        var baseAccess = derivedRef.AccessRW().AsBaseClass();
        baseAccess.BaseField = 15;

        var derivedAccess = baseRef.AccessRW().AsDerivedClass();
        derivedAccess.DerivedField = 25;

        Assert.That(obj.BaseField, Is.EqualTo(15));
        Assert.That(obj.DerivedField, Is.EqualTo(25));
        Assert.That(derivedRef.AccessRO().AsBaseClass().BaseField, Is.EqualTo(15));
        Assert.That(baseRef.AccessRO().AsDerivedClass().DerivedField, Is.EqualTo(25));
    }

    [UnmanagedAccess]
    public partial class ReferenceFields
    {
        public BasicFields Other;
    }

    [Test]
    public void UnmanagedAccess_ReferenceFields_ProjectNestedAccess()
    {
        var other = new BasicFields { IntField = 55 };
        var obj = new ReferenceFields { Other = other };

        UnmanagedRef<ReferenceFields> unmanagedRef = obj;
        var access = unmanagedRef.AccessRW();

        Assert.That(access.Other.IntField, Is.EqualTo(55));

        access.Other.IntField = 99;

        Assert.That(other.IntField, Is.EqualTo(99));
        Assert.That(obj.Other.IntField, Is.EqualTo(99));
    }

    [UnmanagedAccess]
    public partial class UnmanagedArrayFields
    {
        public int[] Values = new[] { 1, 2, 3 };
    }

    [Test]
    public void UnmanagedAccess_UnmanagedArrayFields_ProjectNativeArray()
    {
        var obj = new UnmanagedArrayFields();
        UnmanagedRef<UnmanagedArrayFields> unmanagedRef = obj;
        var access = unmanagedRef.AccessRW();

        Assert.That(access.Values.Length, Is.EqualTo(3));
        Assert.That(access.Values[0], Is.EqualTo(1));
        Assert.That(access.Values[2], Is.EqualTo(3));

        var values = access.Values;
        values[1] = 42;

        Assert.That(obj.Values[1], Is.EqualTo(42));
    }

    [Test]
    public void UnmanagedAccess_UnmanagedArrayFields_AccessRO_UsesReadOnlyView()
    {
        var property
            = typeof(UnmanagedArrayFields.Unmanaged.AccessRO)
                .GetProperty(nameof(UnmanagedArrayFields.Values));

        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(NativeArray<int>.ReadOnly)));

        var obj = new UnmanagedArrayFields();
        var access = ((UnmanagedRef<UnmanagedArrayFields>)obj).AccessRO();

        Assert.That(access.Values.Length, Is.EqualTo(3));
        Assert.That(access.Values[1], Is.EqualTo(2));
    }

    [UnmanagedAccess]
    public partial class AccessedArrayFields
    {
        public BasicFields[] Others = Array.Empty<BasicFields>();
    }

    public struct CollectionStruct
    {
        public int Value;
    }

    [UnmanagedAccess]
    public partial class CollectionClass
    {
        public int Value;
    }

    [UnmanagedAccess]
    public partial class CollectionFields
    {
        public CollectionClass[] ClassArray = Array.Empty<CollectionClass>();
        public CollectionStruct[] StructArray = Array.Empty<CollectionStruct>();
        public List<CollectionClass> ClassList = new();
        public List<CollectionStruct> StructList = new();
    }

    public struct ManagedValueTypeData
    {
        public string Name;
        public BasicFields Other;
        public int Count;
    }

    [UnmanagedAccess]
    public partial class ManagedValueTypeFields
    {
        public ManagedValueTypeData Data;
    }

    [UnmanagedAccess]
    public partial class NullableValueTypeFields
    {
        public Vector3? CustomRangeForce;
    }

    [UnmanagedAccess]
    public partial class AccessROPartialForwardingFields
    {
        public int Value = 10;

        public static partial class Unmanaged
        {
            public readonly unsafe partial struct AccessRO
            {
                public int Doubled
                    => Value * 2;

                public int Add(int value)
                    => Value + value;

                public int Existing()
                    => Value + 1;
            }

            public readonly unsafe partial struct AccessRW
            {
                public int Existing()
                    => -1;
            }
        }
    }

    [Test]
    public void UnmanagedAccess_AccessedArrayFields_ProjectNativeArrayOfRefs()
    {
        var a = new BasicFields { IntField = 7 };
        var b = new BasicFields { IntField = 9 };
        var obj = new AccessedArrayFields { Others = new[] { a, b } };
        UnmanagedRef<AccessedArrayFields> unmanagedRef = obj;
        var access = unmanagedRef.AccessRW();

        Assert.That(access.Others.Length, Is.EqualTo(2));
        var first = access.Others[0];
        var second = access.Others[1];
        var firstAccess = first.AccessRW();
        var secondAccess = second.AccessRW();
        Assert.That(firstAccess.IntField, Is.EqualTo(7));
        Assert.That(secondAccess.IntField, Is.EqualTo(9));

        secondAccess.IntField = 33;

        Assert.That(b.IntField, Is.EqualTo(33));
        Assert.That(obj.Others[1].IntField, Is.EqualTo(33));
    }

    [Test]
    public void UnmanagedAccess_AccessedArrayFields_AccessRO_UsesReadOnlyNativeArrayOfRefs()
    {
        var property
            = typeof(AccessedArrayFields.Unmanaged.AccessRO)
                .GetProperty(nameof(AccessedArrayFields.Others));

        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(NativeArray<UnmanagedRef<BasicFields>>.ReadOnly)));

        var a = new BasicFields { IntField = 2 };
        var b = new BasicFields { IntField = 4 };
        var obj = new AccessedArrayFields { Others = new[] { a, b } };
        var access = ((UnmanagedRef<AccessedArrayFields>)obj).AccessRO();

        int sum = 0;
        foreach (var other in access.Others)
            sum += other.AccessRO().IntField;

        Assert.That(sum, Is.EqualTo(6));
    }

    [Test]
    public void UnmanagedAccess_CollectionFields_AccessRW_ProjectsArraysAndLists()
    {
        var a = new CollectionClass { Value = 3 };
        var b = new CollectionClass { Value = 5 };
        var obj = new CollectionFields
        {
            ClassArray = new[] { a, b },
            StructArray = new[] { new CollectionStruct { Value = 7 }, new CollectionStruct { Value = 11 } },
            ClassList = new() { a, b },
            StructList = new() { new CollectionStruct { Value = 13 }, new CollectionStruct { Value = 17 } },
        };

        var propertyClassArray = typeof(CollectionFields.Unmanaged.AccessRW).GetProperty(nameof(CollectionFields.ClassArray));
        var propertyStructArray = typeof(CollectionFields.Unmanaged.AccessRW).GetProperty(nameof(CollectionFields.StructArray));
        var propertyClassList = typeof(CollectionFields.Unmanaged.AccessRW).GetProperty(nameof(CollectionFields.ClassList));
        var propertyStructList = typeof(CollectionFields.Unmanaged.AccessRW).GetProperty(nameof(CollectionFields.StructList));

        Assert.That(propertyClassArray!.PropertyType, Is.EqualTo(typeof(NativeArray<UnmanagedRef<CollectionClass>>)));
        Assert.That(propertyStructArray!.PropertyType, Is.EqualTo(typeof(NativeArray<CollectionStruct>)));
        Assert.That(propertyClassList!.PropertyType, Is.EqualTo(typeof(CollectionClass.Unmanaged.ListAccess)));
        Assert.That(propertyStructList!.PropertyType, Is.EqualTo(typeof(ListAccess<CollectionStruct>)));

        UnmanagedRef<CollectionFields> unmanagedRef = obj;
        var access = unmanagedRef.AccessRW();

        var classArray = access.ClassArray;
        var classArrayElement = classArray[1];
        var classArrayElementAccess = classArrayElement.AccessRW();
        classArrayElementAccess.Value = 19;
        Assert.That(b.Value, Is.EqualTo(19));

        var structArray = access.StructArray;
        structArray[0] = new() { Value = 23 };
        Assert.That(obj.StructArray[0].Value, Is.EqualTo(23));

        var classList = access.ClassList;
        foreach (var classAccess in classList)
        {
            var writable = classAccess;
            writable.Value += 2;
        }

        Assert.That(a.Value, Is.EqualTo(5));
        Assert.That(b.Value, Is.EqualTo(21));

        var classListRefs = classList.AsNativeArray();
        var firstClassListRef = classListRefs[0];
        Assert.That(firstClassListRef.AccessRW().Value, Is.EqualTo(5));

        var structList = access.StructList;
        var structListArray = structList.AsNativeArray();
        structListArray[1] = new() { Value = 29 };
        Assert.That(obj.StructList[1].Value, Is.EqualTo(29));

        int structListSum = 0;
        foreach (var item in structList)
            structListSum += item.Value;

        Assert.That(structListSum, Is.EqualTo(42));

        Assert.That(classList.Count, Is.EqualTo(2));
        classList.Count = 1;
        Assert.That(obj.ClassList.Count, Is.EqualTo(1));
    }

    [Test]
    public void UnmanagedAccess_CollectionFields_AccessRO_ProjectsListsAsNativeArrays()
    {
        var a = new CollectionClass { Value = 31 };
        var b = new CollectionClass { Value = 37 };
        var obj = new CollectionFields
        {
            ClassArray = new[] { a, b },
            StructArray = new[] { new CollectionStruct { Value = 41 }, new CollectionStruct { Value = 43 } },
            ClassList = new() { a, b },
            StructList = new() { new CollectionStruct { Value = 47 }, new CollectionStruct { Value = 53 } },
        };

        var propertyClassArray = typeof(CollectionFields.Unmanaged.AccessRO).GetProperty(nameof(CollectionFields.ClassArray));
        var propertyStructArray = typeof(CollectionFields.Unmanaged.AccessRO).GetProperty(nameof(CollectionFields.StructArray));
        var propertyClassList = typeof(CollectionFields.Unmanaged.AccessRO).GetProperty(nameof(CollectionFields.ClassList));
        var propertyStructList = typeof(CollectionFields.Unmanaged.AccessRO).GetProperty(nameof(CollectionFields.StructList));

        Assert.That(propertyClassArray!.PropertyType, Is.EqualTo(typeof(NativeArray<UnmanagedRef<CollectionClass>>.ReadOnly)));
        Assert.That(propertyStructArray!.PropertyType, Is.EqualTo(typeof(NativeArray<CollectionStruct>.ReadOnly)));
        Assert.That(propertyClassList!.PropertyType, Is.EqualTo(typeof(NativeArray<UnmanagedRef<CollectionClass>>)));
        Assert.That(propertyStructList!.PropertyType, Is.EqualTo(typeof(NativeArray<CollectionStruct>)));

        var access = ((UnmanagedRef<CollectionFields>)obj).AccessRO();

        var classArrayElement = access.ClassArray[0];
        Assert.That(classArrayElement.AccessRO().Value, Is.EqualTo(31));
        Assert.That(access.StructArray[1].Value, Is.EqualTo(43));

        var classListElement = access.ClassList[1];
        Assert.That(classListElement.AccessRO().Value, Is.EqualTo(37));
        Assert.That(access.StructList[0].Value, Is.EqualTo(47));
    }

    [Test]
    public void UnmanagedAccess_ManagedValueTypeFields_ProjectRefs()
    {
        var other = new BasicFields { IntField = 12 };
        var obj = new ManagedValueTypeFields
        {
            Data = new()
            {
                Name = "fuel",
                Other = other,
                Count = 3,
            },
        };

        UnmanagedRef<ManagedValueTypeFields> unmanagedRef = obj;
        var access = unmanagedRef.AccessRW();

        Assert.That(access.Data.Name, Is.EqualTo("fuel"));
        Assert.That(access.Data.Other.IntField, Is.EqualTo(12));
        Assert.That(access.Data.Count, Is.EqualTo(3));

        access.Data.Name = "battery";
        access.Data.Other = new BasicFields { IntField = 27 };
        access.Data.Count = 8;

        Assert.That(obj.Data.Name, Is.EqualTo("battery"));
        Assert.That(obj.Data.Other.IntField, Is.EqualTo(27));
        Assert.That(obj.Data.Count, Is.EqualTo(8));
    }

    [Test]
    public void UnmanagedAccess_NullableValueTypeFields_ProjectRefs()
    {
        var obj = new NullableValueTypeFields
        {
            CustomRangeForce = new Vector3(1, 2, 3),
        };

        UnmanagedRef<NullableValueTypeFields> unmanagedRef = obj;
        var accessRW = unmanagedRef.AccessRW();

        Assert.That(accessRW.CustomRangeForce.HasValue, Is.True);
        Assert.That(accessRW.CustomRangeForce.Value, Is.EqualTo(new Vector3(1, 2, 3)));

        accessRW.CustomRangeForce = new Vector3(4, 5, 6);

        Assert.That(obj.CustomRangeForce.HasValue, Is.True);
        Assert.That(obj.CustomRangeForce.Value, Is.EqualTo(new Vector3(4, 5, 6)));

        var accessRO = unmanagedRef.AccessRO();
        Assert.That(accessRO.CustomRangeForce.HasValue, Is.True);
        Assert.That(accessRO.CustomRangeForce.Value, Is.EqualTo(new Vector3(4, 5, 6)));

        accessRW.CustomRangeForce = null;

        Assert.That(obj.CustomRangeForce.HasValue, Is.False);
        Assert.That(accessRO.CustomRangeForce.HasValue, Is.False);
    }

    [Test]
    public void UnmanagedAccess_AccessRW_ForwardsUserAccessROMembers()
    {
        var obj = new AccessROPartialForwardingFields { Value = 9 };
        var access = ((UnmanagedRef<AccessROPartialForwardingFields>)obj).AccessRW();

        Assert.That(access.AsReadOnly().Value, Is.EqualTo(9));
        Assert.That(access.Doubled, Is.EqualTo(18));
        Assert.That(access.Add(4), Is.EqualTo(13));

        access.Value = 11;

        Assert.That(access.AsReadOnly().Value, Is.EqualTo(11));
        Assert.That(access.Doubled, Is.EqualTo(22));
        Assert.That(access.Existing(), Is.EqualTo(-1));
    }

    [UnmanagedAccess]
    public partial class NullArrayFields
    {
        public int[] Values;
        public BasicFields[] Others;
    }

    [Test]
    public void UnmanagedAccess_NullArrayFields_ReturnDefaultViews()
    {
        var obj = new NullArrayFields();
        UnmanagedRef<NullArrayFields> unmanagedRef = obj;
        var accessRW = unmanagedRef.AccessRW();
        var accessRO = unmanagedRef.AccessRO();

        Assert.That(accessRW.Values.IsCreated, Is.False);
        Assert.That(accessRO.Values.Length, Is.EqualTo(0));
        Assert.That(accessRW.Others.Length, Is.EqualTo(0));
        Assert.That(accessRO.Others.Length, Is.EqualTo(0));
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

#if UNITY_6000_4_OR_NEWER
        Assert.That(unmanagedRef.GetEntityID(), Is.EqualTo(component.GetEntityId()));
        Assert.That(access.EntityID, Is.EqualTo(component.GetEntityId()));
#else
        Assert.That(unmanagedRef.GetInstanceID(), Is.EqualTo(component.GetInstanceID()));
        Assert.That(access.InstanceID, Is.EqualTo(component.GetInstanceID()));
#endif

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

        static bool HasIsReadOnlyAttribute(ParameterInfo parameterInfo)
            => parameterInfo.GetRequiredCustomModifiers().Any(modifier => modifier.Name == "IsReadOnlyAttribute") ||
               parameterInfo.GetCustomAttributes(inherit: false).Any(attribute => attribute.GetType().Name == "IsReadOnlyAttribute");
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
        public int[] AutoValues { get; set; } = new[] { 4, 5, 6 };
        public BasicFields[] AutoRefs { get; set; } = new[] { new BasicFields { IntField = 12 } };
        public int ManualField = 3;
    }

    [Test]
    public void UnmanagedAccess_AutoProperties_ReadWrite()
    {
        var other = new BasicFields { IntField = 9 };
        var otherArrayA = new BasicFields { IntField = 11 };
        var otherArrayB = new BasicFields { IntField = 13 };
        var obj = new AutoPropertyFields
        {
            AutoInt = 5,
            AutoRef = other,
            AutoValues = new[] { 8, 9, 10 },
            AutoRefs = new[] { otherArrayA, otherArrayB },
        };
        UnmanagedRef<AutoPropertyFields> unmanagedRef = obj;
        var access = unmanagedRef.AccessRW();

        Assert.That(access.AutoInt, Is.EqualTo(5));
        Assert.That(access.AutoRef.IntField, Is.EqualTo(9));
        Assert.That(access.AutoValues[1], Is.EqualTo(9));
        var firstAutoRef = access.AutoRefs[0];
        var secondAutoRef = access.AutoRefs[1];
        var firstAutoRefAccess = firstAutoRef.AccessRW();
        var secondAutoRefAccess = secondAutoRef.AccessRW();
        Assert.That(firstAutoRefAccess.IntField, Is.EqualTo(11));
        Assert.That(secondAutoRefAccess.IntField, Is.EqualTo(13));

        access.AutoInt = 42;
        access.AutoRef.IntField = 99;
        var autoValues = access.AutoValues;
        autoValues[2] = 77;
        secondAutoRefAccess.IntField = 21;

        Assert.That(obj.AutoInt, Is.EqualTo(42));
        Assert.That(obj.AutoRef.IntField, Is.EqualTo(99));
        Assert.That(obj.AutoValues[2], Is.EqualTo(77));
        Assert.That(obj.AutoRefs[1].IntField, Is.EqualTo(21));
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
        Assert.That(manualFieldProperty!.GetCustomAttributes(typeof(DeclaredAtAttribute), false), Is.Not.Empty);

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
    public void UnmanagedAccess_TrackedAccessArray_RangeEnumeration_ReadWrite()
    {
        TestUtility.DestroyAllGameObjects();

        try
        {
            for (int i = 0; i < 6; i++)
            {
                var gameObject = new GameObject($"TrackedAccessRangeRW_{i}", typeof(TrackedAccessComponent));
                gameObject.GetComponent<TrackedAccessComponent>().Value = i + 1;
            }

            Assert.That(TrackedAccessComponent.Instances.Count, Is.EqualTo(6));
            var accessArray = TrackedAccessComponent.Unmanaged.Instances;
            Assert.That(accessArray.Length, Is.EqualTo(6));
            var before = new int[accessArray.Length];
            for (int i = 0; i < before.Length; i++)
                before[i] = accessArray[i].Value;

            var slice = accessArray[1..4];
            Assert.That(slice.Length, Is.EqualTo(3));

            int sum = 0;
            foreach (var access in slice)
            {
                sum += access.Value;
                access.Value += 100;
            }

            Assert.That(sum, Is.EqualTo(before[1] + before[2] + before[3]));

            for (int i = 0; i < accessArray.Length; i++)
            {
                int expected = before[i] + (i is >= 1 and < 4 ? 100 : 0);
                Assert.That(accessArray[i].Value, Is.EqualTo(expected));
            }
        }
        finally
        {
            TestUtility.DestroyAllGameObjects();
        }
    }

    [Test]
    public void UnmanagedAccess_TrackedAccessArray_RangeEnumeration_ReadOnly()
    {
        TestUtility.DestroyAllGameObjects();

        try
        {
            for (int i = 0; i < 6; i++)
            {
                var gameObject = new GameObject($"TrackedAccessRangeRO_{i}", typeof(TrackedAccessComponent));
                gameObject.GetComponent<TrackedAccessComponent>().Value = i + 10;
            }

            Assert.That(TrackedAccessComponent.Instances.Count, Is.EqualTo(6));
            var accessArray = TrackedAccessComponent.Unmanaged.Instances;
            Assert.That(accessArray.Length, Is.EqualTo(6));
            var readOnly = accessArray.AsReadOnly();
            var slice = readOnly[2..5];

            Assert.That(slice.Length, Is.EqualTo(3));
            Assert.That(slice[0].Value, Is.EqualTo(readOnly[2].Value));
            Assert.That(slice[2].Value, Is.EqualTo(readOnly[4].Value));

            int sum = 0;
            foreach (var access in slice)
                sum += access.Value;

            Assert.That(sum, Is.EqualTo(readOnly[2].Value + readOnly[3].Value + readOnly[4].Value));
        }
        finally
        {
            TestUtility.DestroyAllGameObjects();
        }
    }

    [Test]
    public void UnmanagedAccess_TrackedAccessArray_RangeIndexingAndNestedSlices()
    {
        TestUtility.DestroyAllGameObjects();

        try
        {
            for (int i = 0; i < 6; i++)
            {
                var gameObject = new GameObject($"TrackedAccessRangeNested_{i}", typeof(TrackedAccessComponent));
                gameObject.GetComponent<TrackedAccessComponent>().Value = i * 10;
            }

            Assert.That(TrackedAccessComponent.Instances.Count, Is.EqualTo(6));
            var accessArray = TrackedAccessComponent.Unmanaged.Instances;
            Assert.That(accessArray.Length, Is.EqualTo(6));
            var slice = accessArray[1..5];
            var nested = slice[1..3];

            Assert.That(slice.Length, Is.EqualTo(4));
            Assert.That(nested.Length, Is.EqualTo(2));
            Assert.That(slice[0].Value, Is.EqualTo(accessArray[1].Value));
            Assert.That(slice[3].Value, Is.EqualTo(accessArray[4].Value));
            Assert.That(nested[0].Value, Is.EqualTo(accessArray[2].Value));
            Assert.That(nested[1].Value, Is.EqualTo(accessArray[3].Value));
        }
        finally
        {
            TestUtility.DestroyAllGameObjects();
        }
    }

    [Test]
    public void UnmanagedAccess_TrackedAccessArray_RangeIndexer_InvalidRangesThrow()
    {
        TestUtility.DestroyAllGameObjects();

        try
        {
            for (int i = 0; i < 4; i++)
                _ = new GameObject($"TrackedAccessRangeInvalid_{i}", typeof(TrackedAccessComponent));

            var accessArray = TrackedAccessComponent.Unmanaged.Instances;
            var readOnly = accessArray.AsReadOnly();

            Assert.Throws<ArgumentOutOfRangeException>(() => _ = accessArray[3..2]);
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = accessArray[new Range(0, accessArray.Length + 1)]);
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = readOnly[new Range(0, readOnly.Length + 1)]);
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
