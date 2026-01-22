using System.Threading;
using NUnit.Framework;
using Medicine;

public class LazyTests
{
    sealed class TestClass
    {
        public static int ConstructorCalls;
        readonly int Id;

        public TestClass(int id)
        {
            Interlocked.Increment(ref ConstructorCalls);
            Id = id;
        }

        public override string ToString() => $"TestClass #{Id}";
    }

    readonly struct TestStruct
    {
        public static int ConstructorCalls;
        public readonly int Id;

        public TestStruct(int id)
        {
            Interlocked.Increment(ref ConstructorCalls);
            Id = id;
        }

        public override string ToString() => $"TestStruct #{Id}";
    }

    [SetUp]
    public void ResetCounters()
    {
        TestClass.ConstructorCalls = 0;
        TestStruct.ConstructorCalls = 0;
    }

    [Test]
    public void LazyRef_IsNotEvaluated_Until_Value_Accessed()
    {
        var lazy = Lazy.From(() => new TestClass(42));

        Assert.That(
            TestClass.ConstructorCalls, Is.Zero,
            "Delegate must not be invoked yet."
        );

        _ = lazy.Value;
        Assert.That(
            TestClass.ConstructorCalls, Is.EqualTo(1),
            "Delegate should have been invoked exactly once."
        );
    }

    [Test]
    public void LazyRef_Value_Is_Cached()
    {
        var lazy = Lazy.From(() => new TestClass(10));

        var first = lazy.Value;
        var second = lazy.Value;

        Assert.That(
            second, Is.SameAs(first),
            "Subsequent accesses must return the cached reference."
        );

        Assert.That(TestClass.ConstructorCalls, Is.EqualTo(1));
    }

    [Test]
    public void LazyRef_Implicit_Conversion_Works()
    {
        var lazy = Lazy.From(() => new TestClass(5));
        TestClass instance = lazy;

        Assert.That(instance, Is.Not.Null);
        Assert.That(TestClass.ConstructorCalls, Is.EqualTo(1));
    }

    [Test]
    public void LazyRef_ToString_Shows_State_Correctly()
    {
        var lazy = Lazy.From(() => new TestClass(99));

        Assert.That(
            lazy.ToString(), Does.Contain("(unevaluated)"),
            "ToString should show that the value is not yet evaluated."
        );

        _ = lazy.Value;

        Assert.That(
            lazy.ToString(), Does.Contain("TestClass #99"),
            "Evaluated ToString should delegate to wrapped object."
        );
    }

    [Test]
    public void LazyRef_Value_Can_Be_Null()
    {
        var lazy = Lazy.From<object>(() => null);

        Assert.That(lazy.Value, Is.Null);
        Assert.That(lazy.ToString(), Does.Contain("(null)"));
    }

    [Test]
    public void LazyVal_IsNotEvaluated_Until_Value_Accessed()
    {
        var lazy = Lazy.From(() => new TestStruct(7));

        Assert.That(TestStruct.ConstructorCalls, Is.Zero);
        _ = lazy.Value;
        Assert.That(TestStruct.ConstructorCalls, Is.EqualTo(1));
    }

    [Test]
    public void LazyVal_Value_Is_Cached()
    {
        var lazy = Lazy.From(() => new TestStruct(3));

        var first = lazy.Value;
        var second = lazy.Value;

        Assert.That(second.Id, Is.EqualTo(first.Id));
        Assert.That(TestStruct.ConstructorCalls, Is.EqualTo(1));
    }

    [Test]
    public void LazyVal_Implicit_Conversion_Works()
    {
        var lazy = Lazy.From(() => new TestStruct(11));

        TestStruct value = lazy; // implicit conversion
        Assert.That(value.Id, Is.EqualTo(11));
        Assert.That(TestStruct.ConstructorCalls, Is.EqualTo(1));
    }

    [Test]
    public void LazyVal_ToString_Shows_State_Correctly()
    {
        var lazy = Lazy.From(() => new TestStruct(17));

        Assert.That(lazy.ToString(), Does.Contain("(unevaluated)"));

        _ = lazy.Value;
        Assert.That(lazy.ToString(), Does.Contain("TestStruct #17"));
    }

    [Test]
    public void Lazy_From_Selects_Correct_Overload()
    {
        var lazyRef = Lazy.From(() => new TestClass(1));
        var lazyVal = Lazy.From(() => new TestStruct(2));

        Assert.That(lazyRef, Is.InstanceOf<LazyRef<TestClass>>());
        Assert.That(lazyVal, Is.InstanceOf<LazyVal<TestStruct>>());
    }
}