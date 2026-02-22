using Microsoft.CodeAnalysis;
using static System.StringComparison;

internal readonly struct MethodSignatureKey(IMethodSymbol symbol)
{
    public readonly IMethodSymbol Symbol = symbol;
}

internal sealed class MethodSignatureComparer : IEqualityComparer<MethodSignatureKey>
{
    public static readonly MethodSignatureComparer Instance = new();
    readonly SymbolEqualityComparer comparer = SymbolEqualityComparer.Default;

    bool IEqualityComparer<MethodSignatureKey>.Equals(MethodSignatureKey left, MethodSignatureKey right)
    {
        var leftSymbol = left.Symbol;
        var rightSymbol = right.Symbol;

        if (!leftSymbol.Name.Equals(rightSymbol.Name, Ordinal))
            return false;

        if (!comparer.Equals(leftSymbol.ReturnType, rightSymbol.ReturnType))
            return false;

        var leftParameters = leftSymbol.Parameters;
        var rightParameters = rightSymbol.Parameters;

        if (leftParameters.Length != rightParameters.Length)
            return false;

        for (int i = 0; i < leftParameters.Length; i++)
        {
            var lp = leftParameters[i];
            var rp = rightParameters[i];

            if (lp.RefKind != rp.RefKind)
                return false;

            if (!comparer.Equals(lp.Type, rp.Type))
                return false;
        }

        return true;
    }

    int IEqualityComparer<MethodSignatureKey>.GetHashCode(MethodSignatureKey key)
    {
        var symbol = key.Symbol;

        unchecked
        {
            int hash = StringComparer.Ordinal.GetHashCode(symbol.Name);
            hash = (hash * 397) ^ comparer.GetHashCode(symbol.ReturnType);
            foreach (var parameter in symbol.Parameters)
            {
                hash = (hash * 397) ^ (int)parameter.RefKind;
                hash = (hash * 397) ^ comparer.GetHashCode(parameter.Type);
            }

            return hash;
        }
    }
}
