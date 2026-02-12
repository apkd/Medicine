static class UnionNestedSourceGeneratorTest
{
    public static readonly DiagnosticTest Case =
        new("Union generator supports nested [UnionHeader] families", Run);

    static void Run()
        => SourceGeneratorTester.AssertGeneratesSource(
            source: """
using Medicine;

[UnionHeader]
public partial struct BaseEntityState
{
    public interface IDerived
    {
        bool CanAbort(int unit);
    }

    public TypeIDs TypeID;
}

[UnionHeader]
public partial struct WeaponState
{
    public interface IDerivedWeapon : BaseEntityState.IDerived
    {
        bool CanBeginAttack(int unit);
    }

    public BaseEntityState Header;
}

[Union(1)]
public partial struct ItemState : BaseEntityState.IDerived
{
    public BaseEntityState Header;
    public bool CanAbort(int unit) => false;
}

[Union(2)]
public partial struct RangeWeaponState : WeaponState.IDerivedWeapon
{
    public WeaponState Header;
    public bool CanAbort(int unit) => false;
    public bool CanBeginAttack(int unit) => false;
}
""",
            generator: new UnionStructSourceGenerator()
        );
}
