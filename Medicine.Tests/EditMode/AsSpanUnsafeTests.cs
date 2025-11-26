#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Medicine.Internal;

[SuppressMessage("ReSharper", "ExpressionIsAlwaysNull")]
public sealed class AsSpanUnsafeTests
{
    [Test]
    public void List_AsSpanUnsafe_Empty()
    {
        var list = new List<int>();
        var span = list.AsSpanUnsafe();
        Assert.That(Unsafe.IsNullRef(ref MemoryMarshal.GetReference(span)), Is.True);
    }

    [Test]
    public void List_AsSpanUnsafe_ReadWrite_ReflectsBackedArray()
    {
        var list = new List<int> { 1, 2, 3 };
        var span = list.AsSpanUnsafe();

        Assert.That(Unsafe.IsNullRef(ref MemoryMarshal.GetReference(span)), Is.False);

        ref var first = ref MemoryMarshal.GetReference(span);
        Assert.That(first, Is.EqualTo(1));

        // write to second element
        Unsafe.Add(ref first, 1) = 42;
        Assert.That(list[1], Is.EqualTo(42));
    }

    [Test]
    public void Array_AsSpanUnsafe_Null_ReturnsDefault()
    {
        int[]? array = null;
        var span = array!.AsSpanUnsafe();
        Assert.That(Unsafe.IsNullRef(ref MemoryMarshal.GetReference(span)), Is.True);
    }

    [Test]
    public void Array_AsSpanUnsafe_EmptyArray_ReturnsDefault()
    {
        var array = Array.Empty<int>();
        var span = array.AsSpanUnsafe();
        Assert.That(Unsafe.IsNullRef(ref MemoryMarshal.GetReference(span)), Is.True);
    }

    [Test]
    public void Array_AsSpanUnsafe_LengthZero_ReturnsDefault()
    {
        var array = new[] { 10, 20, 30 };
        var span = array.AsSpanUnsafe(0, 0);
        Assert.That(Unsafe.IsNullRef(ref MemoryMarshal.GetReference(span)), Is.True);
    }

    [Test]
    public void Array_AsSpanUnsafe_DefaultLength_UsesRemaining()
    {
        var array = new[] { 5, 6, 7, 8 };
        var span = array.AsSpanUnsafe(1);

        Assert.That(Unsafe.IsNullRef(ref MemoryMarshal.GetReference(span)), Is.False);
        ref var first = ref MemoryMarshal.GetReference(span);
        Assert.That(first, Is.EqualTo(6));
    }

    [Test]
    public void Array_AsSpanUnsafe_ReadWrite_ReflectsArray()
    {
        var array = new[] { 1, 2, 3, 4 };
        var span = array.AsSpanUnsafe(1, 2);

        // Intended contract: span covers [start, start + length)
        ref var first = ref MemoryMarshal.GetReference(span);
        Assert.That(first, Is.EqualTo(2));
        Assert.That(Unsafe.Add(ref first, 1), Is.EqualTo(3));

        first = 99;
        Assert.That(array[1], Is.EqualTo(99));
    }

    [Test]
    public void Array_AsSpanUnsafe_StartAtArrayLength()
    {
        var array = new[] { 1, 2, 3 };

        // Regardless of configuration, using default length at start == Length should result in an invalid slice
        Assert.Throws<ArgumentOutOfRangeException>(() => array.AsSpanUnsafe(array.Length));
    }

    [Test]
    public void Array_AsSpanUnsafe_NegativeStart_ThrowsInDebug()
    {
        var array = new[] { 1, 2, 3 };
        Assert.Throws<ArgumentOutOfRangeException>(() => array.AsSpanUnsafe(-1));
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
        ref var first = ref MemoryMarshal.GetReference(span);
        Assert.That(first, Is.EqualTo("b"));
        Unsafe.Add(ref first, 1) = "z";
        Assert.That(a[2], Is.EqualTo("z"));
    }
}