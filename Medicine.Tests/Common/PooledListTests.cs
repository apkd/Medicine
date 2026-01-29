#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using Medicine;

public class PooledListTests
{
    [Test]
    public void Get_WithOutParameter_ReturnsSameList()
    {
        using var pooled = PooledList.Get<int>(out var list);

        Assert.That(list, Is.SameAs(pooled.List));
        Assert.That(pooled.IsDisposed, Is.False);
        Assert.That(pooled.Count, Is.EqualTo(0));

        list.Add(1);
        list.Add(2);

        Assert.That(pooled.Count, Is.EqualTo(2));
        CollectionAssert.AreEqual(new[] { 1, 2 }, pooled.List);
    }

    [Test]
    public void Get_WithoutOutParameter_ReturnsHandle()
    {
        using var pooled = PooledList.Get<int>();

        Assert.That(pooled.List, Is.Not.Null);
        Assert.That(pooled.IsDisposed, Is.False);

        pooled.List.Add(7);
        Assert.That(pooled.Count, Is.EqualTo(1));
    }

    [Test]
    public void Dispose_IsIdempotent_And_ClearsList()
    {
        var pooled = PooledList.Get<int>(out var list);
        list.Add(1);
        list.Add(2);

        Assert.That(pooled.IsDisposed, Is.False);

        pooled.Dispose();

        Assert.That(pooled.IsDisposed, Is.True);
        Assert.That(list, Is.Empty);

        Assert.DoesNotThrow(() => pooled.Dispose());
        Assert.That(pooled.IsDisposed, Is.True);
    }

    [Test]
    public void Span_ReflectsListChanges()
    {
        using var pooled = PooledList.Get<int>(out var list);

        Assert.That(pooled.Span == default, Is.True);

        list.Add(1);
        list.Add(2);
        list.Add(3);

        var span = pooled.Span;

        Assert.That(span == default, Is.False);
        Assert.That(span.Length, Is.EqualTo(3));
        Assert.That(span[1], Is.EqualTo(2));

        span[1] = 42;
        Assert.That(list[1], Is.EqualTo(42));

        list[0] = 99;
        Assert.That(span[0], Is.EqualTo(99));
    }

    [Test]
    public void Enumeration_ReturnsAllItems()
    {
        using var pooled = PooledList.Get<string>(out var list);
        list.Add("a");
        list.Add("b");
        list.Add("c");

        var collected = new List<string>();
        foreach (var item in pooled)
            collected.Add(item);

        CollectionAssert.AreEqual(list, collected);
    }

    [Test]
    public void Pool_ReusesList_ForSameType()
    {
        List<int> firstList;
        using (PooledList.Get<int>(out firstList))
        {
            firstList.Add(1);
        }

        using var pooledAgain = PooledList.Get<int>(out var secondList);

        Assert.That(secondList, Is.SameAs(firstList));
        Assert.That(secondList, Is.Empty);
    }

#if !MEDICINE_NO_FUNSAFE
    [Test]
    public void Pool_ReusesList_AcrossReferenceTypes_WhenFunsafe()
    {
        List<string> list1;

        using (PooledList.Get(out list1))
            list1.Add("a");

        using var pooledAgain = PooledList.Get<object>(out var list2);

        Assert.That(list2, Is.SameAs(list1));
        Assert.That(list2, Is.Empty);
    }
#endif
}