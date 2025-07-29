using NUnit.Framework.Constraints;

public static class TestExtensions
{
    public static CollectionContainsConstraint Contain(this ConstraintExpression does, object expected)
        => new(expected);
}