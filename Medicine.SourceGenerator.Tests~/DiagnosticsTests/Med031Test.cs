static class Med031Test
{
    public static readonly DiagnosticTest Case =
        new("MED031 when a nested union interface is implemented with incompatible first header field", Run);

    static void Run()
        => DiagnosticTester.AssertEmitted(
            diagnosticId: "MED031",
            source: """
using Medicine;

[UnionHeader]
public partial struct BaseState
{
    public interface IDerived { }
    public TypeIDs TypeID;
}

[UnionHeader]
public partial struct WeaponState
{
    public interface IDerivedWeapon : BaseState.IDerived { }
    public BaseState Header;
}

[Union(31)]
public partial struct InvalidWeaponState : WeaponState.IDerivedWeapon
{
    public BaseState Header;
}
"""
        );
}
