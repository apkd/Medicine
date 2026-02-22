using Microsoft.CodeAnalysis;
using static System.StringComparison;

internal readonly struct PropertySignatureKey(IPropertySymbol symbol)
{
    public readonly IPropertySymbol Symbol = symbol;
}

internal sealed class PropertySignatureComparer : IEqualityComparer<PropertySignatureKey>
{
    public static readonly PropertySignatureComparer Instance = new();
    readonly SymbolEqualityComparer comparer = SymbolEqualityComparer.Default;

    bool IEqualityComparer<PropertySignatureKey>.Equals(PropertySignatureKey left, PropertySignatureKey right)
    {
        var leftSymbol = left.Symbol;
        var rightSymbol = right.Symbol;

        return leftSymbol.Name.Equals(rightSymbol.Name, Ordinal)
               && comparer.Equals(leftSymbol.Type, rightSymbol.Type)
               && leftSymbol.GetMethod is not null == rightSymbol.GetMethod is not null
               && leftSymbol.SetMethod is not null == rightSymbol.SetMethod is not null;
    }

    int IEqualityComparer<PropertySignatureKey>.GetHashCode(PropertySignatureKey key)
    {
        var symbol = key.Symbol;

        unchecked
        {
            int hash = StringComparer.Ordinal.GetHashCode(symbol.Name);
            hash = (hash * 397) ^ comparer.GetHashCode(symbol.Type);
            hash = (hash * 397) ^ (symbol.GetMethod is not null ? 1 : 0);
            hash = (hash * 397) ^ (symbol.SetMethod is not null ? 1 : 0);
            return hash;
        }
    }
}
