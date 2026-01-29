using System;
using Medicine;
using NUnit.Framework;

public partial class UnionTests
{
    public readonly struct Result
    {
        public Result(int value) => Value = value;
        public int Value { get; }
    }

    [UnionHeader]
    public partial struct Shape
    {
        public interface Interface
        {
            int Sides { get; }
            int Perimeter(int scale);
            bool TryGetScaledPerimeter(in int scale, out int perimeter);
            Result GetResult(int x);
        }

        public TypeIDs TypeID;
    }

    [Union(1)]
    public partial struct Triangle : Shape.Interface
    {
        public Shape Header;

        public int Sides => 3;

        int Shape.Interface.Perimeter(int scale)
            => 3 * scale;

        bool Shape.Interface.TryGetScaledPerimeter(in int scale, out int perimeter)
        {
            perimeter = 3 * scale;
            return true;
        }

        Result Shape.Interface.GetResult(int x)
            => new(x + 3);
    }

    [Union(2)]
    public partial struct Square : Shape.Interface
    {
        public Shape Header;

        public int Sides => 4;
        public int Perimeter(int scale) => 4 * scale;

        public bool TryGetScaledPerimeter(in int scale, out int perimeter)
        {
            perimeter = 4 * scale;
            return true;
        }

        public Result GetResult(int x)
            => new(x + 4);
    }

    [Test]
    public void GeneratedTypesAndMembersExist()
    {
        _ = typeof(Shape.TypeIDs);
        _ = typeof(ShapeExtensions);

        Assert.That(default(Shape).TypeName, Is.EqualTo("Undefined (TypeID=0)"));
        Assert.That((int)Shape.TypeIDs.Unset, Is.EqualTo(0));
        Assert.That((int)Shape.TypeIDs.Triangle, Is.EqualTo(1));
        Assert.That((int)Shape.TypeIDs.Square, Is.EqualTo(2));
    }

    [Test]
    public void CallsWork_ForExplicitAndPublicImplementations()
    {
        // explicit
        {
            var triangle = new Triangle { Header = { TypeID = Shape.TypeIDs.Triangle } };
            ref var shape = ref triangle.Header;

            Assert.That(shape.Sides(), Is.EqualTo(3));
            Assert.That(shape.Perimeter(2), Is.EqualTo(6));

            Assert.That(shape.TryGetScaledPerimeter(3, out var perimeter), Is.True);
            Assert.That(perimeter, Is.EqualTo(9));

            Assert.That(shape.GetResult(10).Value, Is.EqualTo(13));
        }

        // implicit
        {
            var square = new Square { Header = { TypeID = Shape.TypeIDs.Square } };
            ref var shape = ref square.Header;

            Assert.That(shape.Sides(), Is.EqualTo(4));
            Assert.That(shape.Perimeter(2), Is.EqualTo(8));

            Assert.That(shape.TryGetScaledPerimeter(3, out var perimeter), Is.True);
            Assert.That(perimeter, Is.EqualTo(12));

            Assert.That(shape.GetResult(10).Value, Is.EqualTo(14));
        }
    }

    [Test]
    public void AsDerivedAccessors_ReturnDerivedForMatchingTypeId()
    {
        var triangle = new Triangle { Header = { TypeID = Shape.TypeIDs.Triangle } };
        ref var shape = ref triangle.Header;
        ref var asTriangle = ref shape.AsTriangle();

        Assert.That(asTriangle.Sides, Is.EqualTo(3));
    }

#if DEBUG
    [Test]
    public void AsDerivedAccessors_ThrowUnexpectedTypeExceptionOnMismatch()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            var square = new Square { Header = { TypeID = Shape.TypeIDs.Square } };
            ref var shape = ref square.Header;
            ref var _ = ref shape.AsTriangle();
        });

        Assert.That(ex!.Message, Does.Contain("expected Triangle"));
        Assert.That(ex.Message, Does.Contain("got Square"));
    }
#endif

    [Test]
    public void TypeNameAndSizeInBytes_UnsetAndUnknown()
    {
        var shape = default(Shape);

        Assert.That(shape.TypeName, Is.EqualTo("Undefined (TypeID=0)"));
        Assert.That(shape.SizeInBytes, Is.EqualTo(-1));

        shape.TypeID = (Shape.TypeIDs)200;
        Assert.That(shape.TypeName, Is.EqualTo("Unknown (TypeID=200)"));
        Assert.That(shape.SizeInBytes, Is.EqualTo(-1));
    }

    [Test]
    public void UnknownTypeId_OutParameterDefaultInit()
    {
        var shape = new Shape
        {
            TypeID = (Shape.TypeIDs)200,
        };

        Assert.Throws<InvalidOperationException>(() =>
        {
            // ReSharper disable once RedundantAssignment
            // ReSharper disable once InlineOutVariableDeclaration
            int perimeter = -1;
            shape.TryGetScaledPerimeter(2, out perimeter);
            Assert.That(perimeter, Is.EqualTo(0));
        });
    }
}