using System;
using Medicine;
using NUnit.Framework;

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
}
