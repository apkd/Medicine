#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using NUnit.Framework;
using Medicine;
using Medicine.Internal;

[SuppressMessage("ReSharper", "ExpressionIsAlwaysNull")]
public class AsSpanUnsafeTests
{
    [Test]
    public void List_AsSpanUnsafe_Empty()
    {
        var list = new List<int>();
        var span = list.AsSpanUnsafe();
        Assert.That(span == default, Is.True);
    }

    [Test]
    public void List_AsSpanUnsafe_ReadWrite_ReflectsBackedArray()
    {
        var list = new List<int> { 1, 2, 3 };
        var span = list.AsSpanUnsafe();

        Assert.That(span == default, Is.False);

        Assert.That(span[0], Is.EqualTo(1));

        // write to second element
        span[1] = 42;
        Assert.That(list[1], Is.EqualTo(42));
    }

    [Test]
    public void List_AsSpanUnsafe_CapacityGreaterThanCount_UsesCount()
    {
        var list = new List<int>(capacity: 8) { 1, 2, 3 };
        var span = list.AsSpanUnsafe();

        Assert.That(list.Capacity, Is.GreaterThan(list.Count));
        Assert.That(span.Length, Is.EqualTo(list.Count));
        Assert.That(span[2], Is.EqualTo(3));
    }

    [Test]
    public void List_AsSpanUnsafe_ListEdits_ReflectedInSpan()
    {
        var list = new List<int> { 1, 2, 3 };
        var span = list.AsSpanUnsafe();

        list[1] = 99;
        Assert.That(span[1], Is.EqualTo(99));
    }

    [Test]
    public void Array_AsSpanUnsafe_Null_ReturnsDefault()
    {
        int[]? array = null;
        var span = array!.AsSpanUnsafe();
        Assert.That(span == default, Is.True);
    }

    [Test]
    public void Array_AsSpanUnsafe_EmptyArray_ReturnsDefault()
    {
        var array = Array.Empty<int>();
        var span = array.AsSpanUnsafe();
        Assert.That(span == default, Is.True);
    }

    [Test]
    public void Array_AsSpanUnsafe_LengthZero_ReturnsDefault()
    {
        var array = new[] { 10, 20, 30 };
        var span = array.AsSpanUnsafe(0, 0);
        Assert.That(span == default, Is.True);
    }

    [Test]
    public void Array_AsSpanUnsafe_DefaultLength_UsesRemaining()
    {
        var array = new[] { 5, 6, 7, 8 };
        var span = array.AsSpanUnsafe(1);

        Assert.That(span == default, Is.False);
        ref var first = ref span[0];
        Assert.That(first, Is.EqualTo(6));
    }

    [Test]
    public void Array_AsSpanUnsafe_ReadWrite_ReflectsArray()
    {
        var array = new[] { 1, 2, 3, 4 };
        var span = array.AsSpanUnsafe(1, 2);

        // Intended contract: span covers [start, start + length)
        Assert.That(span[0], Is.EqualTo(2));
        Assert.That(span[1], Is.EqualTo(3));

        span[0] = 99;
        Assert.That(array[1], Is.EqualTo(99));
    }

    [Test]
    public void Array_AsSpanUnsafe_StartAtArrayLength()
    {
        var array = new[] { 1, 2, 3 };
        var span = array.AsSpanUnsafe(array.Length);
        Assert.That(span.Length is 0);
    }

    [Test]
    public void Array_AsSpanUnsafe_NegativeStart_ThrowsInDebug()
    {
        var array = new[] { 1, 2, 3 };
        Assert.Throws<ArgumentOutOfRangeException>(() => array.AsSpanUnsafe(-1));
    }

    [Test]
    public void Array_AsSpanUnsafe_StartPastArrayLength_ThrowsInDebug()
    {
        var array = new[] { 1, 2, 3 };
        Assert.Throws<ArgumentOutOfRangeException>(() => array.AsSpanUnsafe(array.Length + 1));
    }

    [Test]
    public void Array_AsSpanUnsafe_LengthTooBig_ThrowsInDebug()
    {
        var array = new[] { 1, 2, 3 };
        Assert.Throws<ArgumentOutOfRangeException>(() => array.AsSpanUnsafe(1, 5));
    }

    [Test]
    public void Array_AsSpanUnsafe_NegativeLength_Throws()
    {
        var array = new[] { 1, 2, 3 };
        // negative length is invalid in any configuration
        Assert.Throws<ArgumentOutOfRangeException>(() => array.AsSpanUnsafe(1, -1));
    }

    [Test]
    public void Array_AsSpanUnsafe_ReferenceTypeElements()
    {
        var a = new[] { "a", "b", "c" };
        var span = a.AsSpanUnsafe(1, 2);
        Assert.That(span[0], Is.EqualTo("b"));
        span[1] = "z";
        Assert.That(a[2], Is.EqualTo("z"));
    }

    sealed class Dummy { }

    [Test]
    public unsafe void List_AsUnsafeList_SameType_UsesBackingArray()
    {
        var list = new List<int> { 10, 20, 30 };
        var unsafeList = list.AsUnsafeList<int>();

        Assert.That(unsafeList.Length, Is.EqualTo(list.Count));

        list[1] = 42;
        Assert.That(unsafeList[1], Is.EqualTo(42));

        unsafeList[2] = 99;
        Assert.That(list[2], Is.EqualTo(99));
    }

    [Test]
    public unsafe void List_AsUnsafeList_RefTypeToUnmanagedRef()
    {
        var a = new Dummy();
        var b = new Dummy();
        var list = new List<Dummy> { a, b };

        var unsafeList = list.AsUnsafeList<Dummy, UnmanagedRef<Dummy>>();

        Assert.That(unsafeList.Length, Is.EqualTo(list.Count));
        Assert.That(unsafeList[0].Ptr, Is.EqualTo(new UnmanagedRef<Dummy>(a).Ptr));
        Assert.That(unsafeList[1].Ptr, Is.EqualTo(new UnmanagedRef<Dummy>(b).Ptr));
    }
}
