using Microsoft.CodeAnalysis;
using static System.StringComparison;

static class StructSizeEstimator
{
    const string StructLayoutAttributeFQN = "global::System.Runtime.InteropServices.StructLayoutAttribute";
    const string FieldOffsetAttributeFQN = "global::System.Runtime.InteropServices.FieldOffsetAttribute";

    const short LayoutKindSequential = 0;
    const short LayoutKindExplicit = 2;
    const short LayoutKindAuto = 3;

    const int PointerSize = 8;
    const int DefaultPack = 8;

    readonly record struct LayoutSettings(short Kind, int Pack, int Size);

    readonly record struct SizeResult(int Size, int Alignment);

    public static int EstimateTypeSizeInBytes(ITypeSymbol type)
        => TryEstimateTypeSize(type, new(StringComparer.Ordinal), new(StringComparer.Ordinal), out var size)
            ? size
            : -1;

    static bool TryEstimateTypeSize(
        ITypeSymbol type,
        Dictionary<string, SizeResult> cache,
        HashSet<string> visiting,
        out int size
    )
    {
        if (TryEstimate(type, cache, visiting, out var result))
        {
            size = result.Size;
            return true;
        }

        size = -1;
        return false;
    }

    static bool TryEstimate(ITypeSymbol type, Dictionary<string, SizeResult> cache, HashSet<string> visiting, out SizeResult result)
    {
        while (true)
        {
            if (type is ITypeParameterSymbol)
                return Fail(out result);

            if (type is IArrayTypeSymbol or IPointerTypeSymbol or IFunctionPointerTypeSymbol)
                return Success(PointerSize, PointerSize, out result);

            if (type is not INamedTypeSymbol named)
                return Fail(out result);

            if (named.SpecialType is var specialType and not SpecialType.None)
                if (TryGetSpecialTypeSize(specialType, out var primitiveSize, out var primitiveAlignment))
                    return Success(primitiveSize, primitiveAlignment, out result);

            if (named.TypeKind is TypeKind.Error)
            {
                return named.Name.Equals("TypeIDs", Ordinal)
                    ? Success(1, 1, out result)
                    : Success(PointerSize, PointerSize, out result);
            }

            if (named.IsReferenceType)
                return Success(PointerSize, PointerSize, out result);

            if (named.TypeKind is TypeKind.Enum)
            {
                type = named.EnumUnderlyingType ?? named;
                continue;
            }

            if (named.TypeKind is not TypeKind.Struct)
                return Fail(out result);

            string key = named.FQN;
            if (cache.TryGetValue(key, out result))
                return true;

            if (!visiting.Add(key))
                return Fail(out result);

            bool succeeded = TryEstimateStruct(named, cache, visiting, out result);
            visiting.Remove(key);

            if (succeeded)
                cache[key] = result;

            return succeeded;
        }
    }

    static bool TryEstimateStruct(
        INamedTypeSymbol type,
        Dictionary<string, SizeResult> cache,
        HashSet<string> visiting,
        out SizeResult result
    )
    {
        var layout = GetLayoutSettings(type);
        int pack = layout.Pack > 0 ? layout.Pack : DefaultPack;

        var fields = type.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(x => !x.IsStatic)
            .OrderBy(x => x.Locations.FirstOrDefault()?.SourceTree?.FilePath, StringComparer.Ordinal)
            .ThenBy(x => x.Locations.FirstOrDefault()?.SourceSpan.Start ?? int.MaxValue)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .ToArray();

        if (layout.Kind is LayoutKindExplicit)
        {
            int maxEnd = 0;
            int maxAlignment = 1;

            foreach (var field in fields)
            {
                if (!TryEstimate(field.Type, cache, visiting, out var fieldResult))
                    return Fail(out result);

                if (!TryGetFieldOffset(field, out var fieldOffset))
                    return Fail(out result);

                int fieldAlignment = NormalizeAlignment(fieldResult.Alignment, pack);
                maxAlignment = Math.Max(maxAlignment, fieldAlignment);
                maxEnd = Math.Max(maxEnd, fieldOffset + fieldResult.Size);
            }

            int explicitSize = layout.Size > 0
                ? Math.Max(layout.Size, maxEnd)
                : maxEnd;

            return Success(explicitSize, maxAlignment, out result);
        }

        if (layout.Kind is not LayoutKindSequential and not LayoutKindAuto)
            return Fail(out result);

        int offset = 0;
        int structAlignment = 1;

        foreach (var field in fields)
        {
            if (!TryEstimate(field.Type, cache, visiting, out var fieldResult))
                return Fail(out result);

            int fieldAlignment = NormalizeAlignment(fieldResult.Alignment, pack);
            structAlignment = Math.Max(structAlignment, fieldAlignment);
            offset = AlignUp(offset, fieldAlignment);
            offset += fieldResult.Size;
        }

        int size = AlignUp(offset, structAlignment);
        if (layout.Size > 0)
            size = Math.Max(size, layout.Size);

        return Success(size, structAlignment, out result);
    }

    static LayoutSettings GetLayoutSettings(INamedTypeSymbol type)
    {
        var attribute = type.GetAttribute(StructLayoutAttributeFQN);
        if (attribute is null)
            return new(LayoutKindSequential, DefaultPack, 0);

        short kind = LayoutKindSequential;
        if (attribute.ConstructorArguments.Length > 0)
            kind = attribute.ConstructorArguments[0].Value switch
            {
                short x => x,
                int x   => (short)x,
                _       => LayoutKindSequential,
            };

        int pack = GetNamedIntArgument(attribute, "Pack", DefaultPack);
        int size = GetNamedIntArgument(attribute, "Size", 0);

        return new(kind, pack, size);
    }

    static int GetNamedIntArgument(AttributeData attribute, string name, int fallback)
    {
        foreach (var (key, value) in attribute.NamedArguments)
        {
            if (!key.Equals(name, Ordinal))
                continue;

            return value.Value switch
            {
                short x => x,
                int x   => x,
                _       => fallback,
            };
        }

        return fallback;
    }

    static bool TryGetFieldOffset(IFieldSymbol field, out int offset)
    {
        var attribute = field.GetAttribute(FieldOffsetAttributeFQN);
        if (attribute is not { ConstructorArguments.Length: > 0 })
        {
            offset = -1;
            return false;
        }

        offset = attribute.ConstructorArguments[0].Value switch
        {
            int x   => x,
            short x => x,
            _       => -1,
        };

        return offset >= 0;
    }

    static bool TryGetSpecialTypeSize(SpecialType type, out int size, out int alignment)
    {
        switch (type)
        {
            case SpecialType.System_Boolean:
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
                size = 1;
                alignment = 1;
                return true;

            case SpecialType.System_Char:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
                size = 2;
                alignment = 2;
                return true;

            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Single:
                size = 4;
                alignment = 4;
                return true;

            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Double:
                size = 8;
                alignment = 8;
                return true;

            case SpecialType.System_IntPtr:
            case SpecialType.System_UIntPtr:
            case SpecialType.System_String:
            case SpecialType.System_Object:
                size = PointerSize;
                alignment = PointerSize;
                return true;

            default:
                size = -1;
                alignment = -1;
                return false;
        }
    }

    static int NormalizeAlignment(int alignment, int pack)
        => Math.Max(1, Math.Min(Math.Max(1, alignment), Math.Max(1, pack)));

    static int AlignUp(int value, int alignment)
    {
        if (alignment <= 1)
            return value;

        int remainder = value % alignment;
        return remainder == 0
            ? value
            : value + alignment - remainder;
    }

    static bool Success(int size, int alignment, out SizeResult result)
    {
        if (size < 0)
            return Fail(out result);

        result = new(size, Math.Max(1, alignment));
        return true;
    }

    static bool Fail(out SizeResult result)
    {
        result = default;
        return false;
    }
}
