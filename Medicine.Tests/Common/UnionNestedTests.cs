using System;
using System.Runtime.InteropServices;
using Medicine;
using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;

public partial class UnionNestedTests
{
    [UnionHeader]
    public partial struct RootState
    {
        public interface IDerived
        {
            bool IsReady(int value);
        }

        public TypeIDs TypeID;
        public int RootCounter;
        public int RootGetOnly => 101;
        public int RootPublicGetPrivateSet { get; private set; }
        public int RootPrivateGetPublicSet { private get; set; }

        int rootSetOnlyValue;
        public int RootSetOnly { set => rootSetOnlyValue = value; }

        public readonly int ReadRootSetOnly()
            => rootSetOnlyValue;

        public readonly int ReadRootPrivateGetPublicSet()
            => RootPrivateGetPublicSet;

        public void SetRootPublicGetPrivateSet(int value)
            => RootPublicGetPrivateSet = value;
    }

    [UnionHeader]
    public partial struct ChildState
    {
        public interface IDerivedChild : RootState.IDerived
        {
            bool CanRun(int value);

            void Run(int value) { }
        }

        public RootState Header;
        public int Counter;
        public int ChildGetOnly => 201;
        public int ChildPublicGetPrivateSet { get; private set; }
        public int ChildPrivateGetPublicSet { private get; set; }

        int childSetOnlyValue;
        public int ChildSetOnly { set => childSetOnlyValue = value; }

        public readonly int ReadChildSetOnly()
            => childSetOnlyValue;

        public readonly int ReadChildPrivateGetPublicSet()
            => ChildPrivateGetPublicSet;

        public void SetChildPublicGetPrivateSet(int value)
            => ChildPublicGetPrivateSet = value;
    }

    [Union]
    public partial struct PlainState : RootState.IDerived
    {
        public RootState Header;
        public int Value;

        public bool IsReady(int value)
            => false;
    }

    [Union]
    public partial struct ChildAState : ChildState.IDerivedChild
    {
        public ChildState Header;
        public int A;

        bool RootState.IDerived.IsReady(int value)
            => true;

        bool ChildState.IDerivedChild.CanRun(int value)
            => true;

        void ChildState.IDerivedChild.Run(int value)
            => A++;
    }

    [Union]
    public partial struct ChildBState : ChildState.IDerivedChild
    {
        public ChildState Header;
        public int B;

        public bool IsReady(int value)
            => false;

        public bool CanRun(int value)
            => false;

        public void Run(int value)
            => Header.Counter++;
    }

    [Test]
    public void GeneratedTypesAndMembersExist_ForNestedUnionFamily()
    {
        _ = typeof(RootState.TypeIDs);
        _ = typeof(RootStateExtensions);
        _ = typeof(ChildStateExtensions);

        Assert.That(typeof(ChildState).GetNestedType("TypeIDs"), Is.Null);
        Assert.That((int)RootState.TypeIDs.Unset, Is.EqualTo(0));
        Assert.That(RootState.TypeIDs.PlainState, Is.Not.EqualTo(RootState.TypeIDs.Unset));
        Assert.That(RootState.TypeIDs.ChildAState, Is.Not.EqualTo(RootState.TypeIDs.Unset));
        Assert.That(RootState.TypeIDs.ChildBState, Is.Not.EqualTo(RootState.TypeIDs.Unset));
        Assert.That(RootState.TypeIDs.PlainState, Is.Not.EqualTo(RootState.TypeIDs.ChildAState));
        Assert.That(RootState.TypeIDs.PlainState, Is.Not.EqualTo(RootState.TypeIDs.ChildBState));
        Assert.That(RootState.TypeIDs.ChildAState, Is.Not.EqualTo(RootState.TypeIDs.ChildBState));
    }

    [Test]
    public void VariantTypeIdConstants_AreSharedFromRootHeader()
    {
        Assert.That(PlainState.TypeID, Is.EqualTo(RootState.TypeIDs.PlainState));
        Assert.That(ChildAState.TypeID, Is.EqualTo(RootState.TypeIDs.ChildAState));
        Assert.That(ChildBState.TypeID, Is.EqualTo(RootState.TypeIDs.ChildBState));
    }

    [Test]
    public void RootDispatch_Works_ForImplicitAndExplicitVariants()
    {
        var plain = CreatePlainState();
        var childA = CreateChildAState();
        var childB = CreateChildBState();

        ref var plainRoot = ref plain.Header;
        ref var childARoot = ref childA.Header.Header;
        ref var childBRoot = ref childB.Header.Header;

        Assert.That(plainRoot.IsReady(10), Is.False);
        Assert.That(childARoot.IsReady(10), Is.True);
        Assert.That(childBRoot.IsReady(10), Is.False);
    }

    [Test]
    public void ChildDispatch_Works_ForImplicitAndExplicitVariants()
    {
        var childA = CreateChildAState();
        var childB = CreateChildBState();

        ref var childAHeader = ref childA.Header;
        ref var childBHeader = ref childB.Header;

        Assert.That(childAHeader.CanRun(3), Is.True);
        Assert.That(childBHeader.CanRun(3), Is.False);

        var beforeA = childA.A;
        childAHeader.Run(3);
        Assert.That(childA.A, Is.EqualTo(beforeA + 1));

        var beforeCounter = childBHeader.Counter;
        childBHeader.Run(3);
        Assert.That(childBHeader.Counter, Is.EqualTo(beforeCounter + 1));
    }

    [Test]
    public void NestedHeader_Forwards_TypeMetadata_FromRoot()
    {
        var child = CreateChildBState();

        ref var root = ref child.Header.Header;
        ref var nested = ref child.Header;

        Assert.That(nested.TypeID, Is.EqualTo(root.TypeID));
        Assert.That(nested.TypeName, Is.EqualTo(root.TypeName));
        Assert.That(nested.SizeInBytes, Is.EqualTo(root.SizeInBytes));
        Assert.That(nested.SizeInBytes, Is.GreaterThan(0));
    }

    [Test]
    public void ParentHelpers_DetectChildVariants_AndRejectOthers()
    {
        var plain = CreatePlainState();
        var childA = CreateChildAState();
        var childB = CreateChildBState();

        Assert.That(plain.Header.IsChildState(), Is.False);
        Assert.That(childA.Header.Header.IsChildState(), Is.True);
        Assert.That(childB.Header.Header.IsChildState(), Is.True);

        var unknown = new RootState { TypeID = (RootState.TypeIDs)200 };
        Assert.That(unknown.IsChildState(), Is.False);
    }

    [Test]
    public void AsChildState_ReinterpretsReference_ToNestedHeader()
    {
        var child = CreateChildBState();
        ref var root = ref child.Header.Header;

        ref var nested = ref root.AsChildState();
        nested.Counter = 42;

        Assert.That(child.Header.Counter, Is.EqualTo(42));
    }

    [Test]
    public void AsDerivedAccessors_Work_FromRootAndNestedHeaders()
    {
        var plain = CreatePlainState();
        var childA = CreateChildAState();
        var childB = CreateChildBState();

        ref var asPlain = ref plain.Header.AsPlainState();
        asPlain.Value = 99;
        Assert.That(plain.Value, Is.EqualTo(99));

        ref var asA = ref childA.Header.Header.AsChildAState();
        asA.A = 7;
        Assert.That(childA.A, Is.EqualTo(7));

        ref var asBFromRoot = ref childB.Header.Header.AsChildBState();
        asBFromRoot.B = 5;
        Assert.That(childB.B, Is.EqualTo(5));

        ref var asBFromChild = ref childB.Header.AsChildBState();
        asBFromChild.B = 11;
        Assert.That(childB.B, Is.EqualTo(11));
    }

    [Test]
    public void NestedHeaderFieldProperties_ForwardToHeaderStorage()
    {
        var childA = CreateChildAState();

        childA.Counter = 10;
        Assert.That(childA.Header.Counter, Is.EqualTo(10));

        childA.Header.Counter = 13;
        Assert.That(childA.Counter, Is.EqualTo(13));
    }

    [Test]
    public void RootHeaderPropertyForwarding_Works_ForNestedUnionVariants()
    {
        var plain = CreatePlainState();
        var childA = CreateChildAState();

        plain.RootCounter = 8;
        Assert.That(plain.Header.RootCounter, Is.EqualTo(8));

        childA.RootCounter = 15;
        Assert.That(childA.Header.Header.RootCounter, Is.EqualTo(15));

        childA.Header.Header.RootCounter = 19;
        Assert.That(childA.RootCounter, Is.EqualTo(19));
    }

    [Test]
    public void RootHeaderPropertyForwarding_HandlesAccessorVariants_ForNestedUnionVariants()
    {
        var childA = CreateChildAState();

        Assert.That(childA.RootGetOnly, Is.EqualTo(101));

        childA.RootSetOnly = 21;
        Assert.That(childA.Header.Header.ReadRootSetOnly(), Is.EqualTo(21));

        childA.RootPrivateGetPublicSet = 31;
        Assert.That(childA.Header.Header.ReadRootPrivateGetPublicSet(), Is.EqualTo(31));

        childA.Header.Header.SetRootPublicGetPrivateSet(41);
        Assert.That(childA.RootPublicGetPrivateSet, Is.EqualTo(41));
    }

    [Test]
    public void ChildHeaderPropertyForwarding_HandlesAccessorVariants_ForNestedUnionVariants()
    {
        var childA = CreateChildAState();

        Assert.That(childA.ChildGetOnly, Is.EqualTo(201));

        childA.ChildSetOnly = 27;
        Assert.That(childA.Header.ReadChildSetOnly(), Is.EqualTo(27));

        childA.ChildPrivateGetPublicSet = 37;
        Assert.That(childA.Header.ReadChildPrivateGetPublicSet(), Is.EqualTo(37));

        childA.Header.SetChildPublicGetPrivateSet(47);
        Assert.That(childA.ChildPublicGetPrivateSet, Is.EqualTo(47));
    }

    [Test]
    public void NestedForwardedProperties_ExposeExpectedAccessorShapes()
    {
        var rootGetOnly = typeof(ChildAState).GetProperty(nameof(RootState.RootGetOnly));
        var rootGetPrivateSet = typeof(ChildAState).GetProperty(nameof(RootState.RootPublicGetPrivateSet));
        var rootPrivateGetSet = typeof(ChildAState).GetProperty(nameof(RootState.RootPrivateGetPublicSet));
        var rootSetOnly = typeof(ChildAState).GetProperty(nameof(RootState.RootSetOnly));
        var childGetOnly = typeof(ChildAState).GetProperty(nameof(ChildState.ChildGetOnly));
        var childGetPrivateSet = typeof(ChildAState).GetProperty(nameof(ChildState.ChildPublicGetPrivateSet));
        var childPrivateGetSet = typeof(ChildAState).GetProperty(nameof(ChildState.ChildPrivateGetPublicSet));
        var childSetOnly = typeof(ChildAState).GetProperty(nameof(ChildState.ChildSetOnly));

        Assert.That(rootGetOnly, Is.Not.Null);
        Assert.That(rootGetOnly!.CanRead, Is.True);
        Assert.That(rootGetOnly.CanWrite, Is.False);

        Assert.That(rootGetPrivateSet, Is.Not.Null);
        Assert.That(rootGetPrivateSet!.CanRead, Is.True);
        Assert.That(rootGetPrivateSet.CanWrite, Is.False);

        Assert.That(rootPrivateGetSet, Is.Not.Null);
        Assert.That(rootPrivateGetSet!.CanRead, Is.False);
        Assert.That(rootPrivateGetSet.CanWrite, Is.True);

        Assert.That(rootSetOnly, Is.Not.Null);
        Assert.That(rootSetOnly!.CanRead, Is.False);
        Assert.That(rootSetOnly.CanWrite, Is.True);

        Assert.That(childGetOnly, Is.Not.Null);
        Assert.That(childGetOnly!.CanRead, Is.True);
        Assert.That(childGetOnly.CanWrite, Is.False);

        Assert.That(childGetPrivateSet, Is.Not.Null);
        Assert.That(childGetPrivateSet!.CanRead, Is.True);
        Assert.That(childGetPrivateSet.CanWrite, Is.False);

        Assert.That(childPrivateGetSet, Is.Not.Null);
        Assert.That(childPrivateGetSet!.CanRead, Is.False);
        Assert.That(childPrivateGetSet.CanWrite, Is.True);

        Assert.That(childSetOnly, Is.Not.Null);
        Assert.That(childSetOnly!.CanRead, Is.False);
        Assert.That(childSetOnly.CanWrite, Is.True);
    }

    [Test]
    public void UnknownTypeIds_ExposeUnknownMetadata_AndThrowOnDispatch()
    {
        var child = new ChildState
        {
            Header =
            {
                TypeID = (RootState.TypeIDs)200,
            },
        };

        var root = child.Header;

        Assert.That(root.TypeName, Is.EqualTo("Unknown (TypeID=200)"));
        Assert.That(root.SizeInBytes, Is.EqualTo(-1));
        Assert.That(child.TypeName, Is.EqualTo("Unknown (TypeID=200)"));
        Assert.That(child.SizeInBytes, Is.EqualTo(-1));

        var rootEx = Assert.Throws<InvalidOperationException>(() => root.IsReady(1));
        Assert.That(rootEx!.Message, Does.Contain("Unknown RootState type ID"));

        var childEx = Assert.Throws<InvalidOperationException>(() => child.CanRun(1));
        Assert.That(childEx!.Message, Does.Contain("Unknown ChildState type ID"));
    }

#if DEBUG
    [Test]
    public void AsChildState_ThrowsOnNonChildVariant()
    {
        var plain = CreatePlainState();
        var root = plain.Header;

        var ex = Assert.Throws<InvalidOperationException>(() => root.AsChildState());

        Assert.That(ex!.Message, Does.Contain("Cannot cast RootState to ChildState"));
    }

    [Test]
    public void AsChildBState_ThrowsOnChildAVariant()
    {
        var childA = CreateChildAState();
        var child = childA.Header;

        var ex = Assert.Throws<InvalidOperationException>(() => child.AsChildBState());

        Assert.That(ex!.Message, Does.Contain("expected ChildBState"));
        Assert.That(ex.Message, Does.Contain("got ChildAState"));
    }
#endif

    static PlainState CreatePlainState()
        => new()
        {
            Header = { TypeID = RootState.TypeIDs.PlainState },
            Value = 1,
        };

    static ChildAState CreateChildAState()
        => new()
        {
            Header =
            {
                Header = { TypeID = RootState.TypeIDs.ChildAState },
                Counter = 2,
            },
            A = 3,
        };

    static ChildBState CreateChildBState()
        => new()
        {
            Header =
            {
                Header = { TypeID = RootState.TypeIDs.ChildBState },
                Counter = 4,
            },
            B = 5,
        };

    [Test]
    public void Wrapper_IsGenerated_ForNestedHeaders_WithExplicitLayout()
    {
        var rootWrapperType = typeof(RootState).GetNestedType("Wrapper");
        Assert.That(rootWrapperType, Is.Not.Null);

        var rootLayout = rootWrapperType!.StructLayoutAttribute;
        Assert.That(rootLayout, Is.Not.Null);
        Assert.That(rootLayout!.Value, Is.EqualTo(LayoutKind.Explicit));

        var expectedRootSize = Math.Max(
            UnsafeUtility.SizeOf<PlainState>(),
            Math.Max(UnsafeUtility.SizeOf<ChildAState>(), UnsafeUtility.SizeOf<ChildBState>())
        );
        Assert.That(rootLayout.Size, Is.EqualTo(expectedRootSize));

        var childWrapperType = typeof(ChildState).GetNestedType("Wrapper");
        Assert.That(childWrapperType, Is.Not.Null);

        var childLayout = childWrapperType!.StructLayoutAttribute;
        Assert.That(childLayout, Is.Not.Null);
        Assert.That(childLayout!.Value, Is.EqualTo(LayoutKind.Explicit));

        var expectedChildSize = Math.Max(UnsafeUtility.SizeOf<ChildAState>(), UnsafeUtility.SizeOf<ChildBState>());
        Assert.That(childLayout.Size, Is.EqualTo(expectedChildSize));
    }

    [Test]
    public void ChildWrapper_ForwardsInheritedAndDeclaredInterfaceMembers()
    {
        var wrapper = new ChildState.Wrapper
        {
            Header = new()
            {
                Header = { TypeID = RootState.TypeIDs.ChildAState },
            },
        };

        ChildState.IDerivedChild polymorphic = wrapper;
        Assert.That(polymorphic.IsReady(10), Is.True);
        Assert.That(polymorphic.CanRun(10), Is.True);

        ref var asChildA = ref wrapper.AsChildAState();
        Assert.That(asChildA.A, Is.EqualTo(0));

        var childA = CreateChildAState();
        ref var wrappedChildA = ref childA.Wrap();
        wrappedChildA.Counter = 12;
        Assert.That(childA.Header.Counter, Is.EqualTo(12));

        ref var wrappedChildHeader = ref childA.Header.Wrap();
        wrappedChildHeader.Counter = 14;
        Assert.That(childA.Header.Counter, Is.EqualTo(14));

        ref var asRootFromChild = ref childA.Header.AsRootState();
        asRootFromChild.RootCounter = 22;
        Assert.That(childA.Header.Header.RootCounter, Is.EqualTo(22));

        ref var asRootWrapperFromChild = ref wrappedChildHeader.AsRootState();
        asRootWrapperFromChild.RootCounter = 24;
        Assert.That(childA.Header.Header.RootCounter, Is.EqualTo(24));

        wrapper.Counter = 6;
        Assert.That(wrapper.Header.Counter, Is.EqualTo(6));
        Assert.That(wrapper.ChildGetOnly, Is.EqualTo(201));

        wrapper.ChildSetOnly = 17;
        Assert.That(wrapper.Header.ReadChildSetOnly(), Is.EqualTo(17));

        wrapper.ChildPrivateGetPublicSet = 23;
        Assert.That(wrapper.Header.ReadChildPrivateGetPublicSet(), Is.EqualTo(23));

        wrapper.Header.SetChildPublicGetPrivateSet(29);
        Assert.That(wrapper.ChildPublicGetPrivateSet, Is.EqualTo(29));

        wrapper.Header.Counter = 3;
        polymorphic.Run(10);
        Assert.That(wrapper.Header.Counter, Is.EqualTo(3));
    }
}
