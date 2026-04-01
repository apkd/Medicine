using Microsoft.CodeAnalysis;

public static class MedicineSymbolExtensions
{
    extension(ISymbol self)
    {
        public bool IsInMedicineNamespace
            => self.ContainingNamespace is { Name: Constants.Namespace, ContainingNamespace.IsGlobalNamespace: true };
    }
}
