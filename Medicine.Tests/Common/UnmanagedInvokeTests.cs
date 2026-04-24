#if MODULE_BURST
using Medicine;
using NUnit.Framework;

public partial class UnmanagedInvokeTests
{
    public sealed class Payload
    {
        public int Value;
    }

    public static partial class StaticHost
    {
        [UnmanagedInvoke]
        public static int Add(Payload payload, int value)
            => payload.Value + value;

        [UnmanagedInvoke]
        public static Payload Echo(Payload payload)
            => payload;

        [UnmanagedInvoke]
        public static UnmanagedRef<Payload> EchoRef(UnmanagedRef<Payload> payload)
            => payload;

        [UnmanagedInvoke]
        public static void Replace(ref Payload current, out Payload previous, Payload next)
        {
            previous = current;
            current = next;
        }
    }

    [UnmanagedAccess]
    public partial class InstanceHost
    {
        public int Value;

        [UnmanagedInvoke]
        public int Add(Payload payload, ref int value)
        {
            value += payload.Value;
            return Value + value;
        }

        [UnmanagedInvoke]
        public virtual Payload Echo(Payload payload)
            => payload;
    }

    [Test]
    public void UnmanagedInvoke_StaticMethod_InvokesManagedMethod()
    {
        var payload = new Payload { Value = 4 };
        var result = StaticHost.AddUnmanaged(new UnmanagedRef<Payload>(payload), 3);

        Assert.That(result, Is.EqualTo(7));
    }

    [Test]
    public void UnmanagedInvoke_StaticMethod_ReturnsManagedReference()
    {
        var payload = new Payload { Value = 5 };
        var result = StaticHost.EchoUnmanaged(new UnmanagedRef<Payload>(payload));

        Assert.That(result.Resolve(), Is.SameAs(payload));
    }

    [Test]
    public void UnmanagedInvoke_StaticMethod_ReturnsUnmanagedRef()
    {
        var payload = new Payload { Value = 6 };
        var result = StaticHost.EchoRefUnmanaged(new UnmanagedRef<Payload>(payload));

        Assert.That(result.Resolve(), Is.SameAs(payload));
    }

    [Test]
    public void UnmanagedInvoke_StaticMethod_CopiesRefAndOutManagedReferences()
    {
        var current = new Payload { Value = 1 };
        var next = new Payload { Value = 2 };
        var currentRef = new UnmanagedRef<Payload>(current);

        StaticHost.ReplaceUnmanaged(ref currentRef, out var previousRef, new UnmanagedRef<Payload>(next));

        Assert.That(previousRef.Resolve(), Is.SameAs(current));
        Assert.That(currentRef.Resolve(), Is.SameAs(next));
    }

    [Test]
    public void UnmanagedInvoke_InstanceMethod_InvokesFromAccessRW()
    {
        var host = new InstanceHost { Value = 10 };
        var payload = new Payload { Value = 4 };
        var value = 3;
        var hostRef = new UnmanagedRef<InstanceHost>(host);
        var access = hostRef.AccessRW();

        var result = access.AddUnmanaged(new UnmanagedRef<Payload>(payload), ref value);

        Assert.That(value, Is.EqualTo(7));
        Assert.That(result, Is.EqualTo(17));
    }

    [Test]
    public void UnmanagedInvoke_InstanceMethod_InvokesFromAccessRO()
    {
        var host = new InstanceHost { Value = 10 };
        var payload = new Payload { Value = 8 };
        var hostRef = new UnmanagedRef<InstanceHost>(host);
        var access = hostRef.AccessRO();

        var result = access.EchoUnmanaged(new UnmanagedRef<Payload>(payload));

        Assert.That(result.Resolve(), Is.SameAs(payload));
    }

    [Test]
    public void UnmanagedInvoke_AccessStructs_ConvertToUnmanagedRef()
    {
        var host = new InstanceHost { Value = 12 };
        var hostRef = new UnmanagedRef<InstanceHost>(host);

        UnmanagedRef<InstanceHost> rwRef = hostRef.AccessRW();
        UnmanagedRef<InstanceHost> roRef = hostRef.AccessRO();

        Assert.That(rwRef.Resolve(), Is.SameAs(host));
        Assert.That(roRef.Resolve(), Is.SameAs(host));
    }
}
#endif
