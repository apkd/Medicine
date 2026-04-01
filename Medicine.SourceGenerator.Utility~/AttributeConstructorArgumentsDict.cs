using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public readonly struct AttributeConstructorArgumentsDict
{
    readonly Dictionary<string, object> values = new(StringComparer.Ordinal);

    public AttributeConstructorArgumentsDict(AttributeData? attributeData, CancellationToken ct = default)
    {
        try
        {
            if (attributeData is null)
                return;

            if (attributeData.ConstructorArguments.Length is 0)
                return;

            var ctor = attributeData.AttributeConstructor ?? throw new("Attribute constructor is null");
            var ctorParams = ctor.Parameters;
            var ctorArgs = attributeData.ConstructorArguments;
            var explicitOrdinals = GetExplicitConstructorOrdinals(attributeData, ct);

            foreach (int ordinal in explicitOrdinals)
            {
                if ((uint)ordinal >= (uint)ctorParams.Length)
                    continue;

                if ((uint)ordinal >= (uint)ctorArgs.Length)
                    continue;

                var tc = ctorArgs[ordinal];
                var param = ctorParams[ordinal];

                if (tc.Kind is TypedConstantKind.Array)
                {
                    if (tc.IsNull)
                        continue;

                    values[param.Name] = MaterializeArray(tc, param.Type);
                    continue;
                }

                if (tc.Value is { } scalar)
                    values[param.Name] = scalar;
            }

            // foreach (var namedArg in attributeData.NamedArguments)
            //     if (namedArg.Value.Value is { } value)
            //         values[namedArg.Key] = value;
        }
        catch (Exception)
        {
            // ignored
        }
    }

    static IEnumerable<int> GetExplicitConstructorOrdinals(AttributeData attributeData, CancellationToken ct)
    {
        var ordinals = new HashSet<int>();
        try
        {
            if (attributeData.AttributeConstructor is not { } ctor)
                return ordinals;

            var parameterSymbols = ctor.Parameters;
            int paramCount = parameterSymbols.Length;
            bool lastIsParams = paramCount > 0 && parameterSymbols[paramCount - 1].IsParams;
            int lastOrdinal = paramCount - 1;
            Dictionary<string, int>? parameterOrdinalsByName = null;

            if (attributeData.ApplicationSyntaxReference?.GetSyntax(ct) is not AttributeSyntax { ArgumentList: { } argumentList })
                return ordinals;

            int positionalIndex = 0;
            foreach (var arg in argumentList.Arguments)
            {
                if (arg.NameColon is not null)
                {
                    var name = arg.NameColon.Name.Text;
                    parameterOrdinalsByName ??= BuildParameterOrdinals();
                    if (parameterOrdinalsByName.TryGetValue(name, out int namedOrdinal))
                        ordinals.Add(namedOrdinal);

                    continue;
                }

                int mappedOrdinal;

                if (positionalIndex < paramCount)
                    mappedOrdinal = positionalIndex;
                else if (lastIsParams)
                    mappedOrdinal = lastOrdinal;
                else
                    break;

                ordinals.Add(mappedOrdinal);
                positionalIndex++;
            }

            Dictionary<string, int> BuildParameterOrdinals()
            {
                var map = new Dictionary<string, int>(parameterSymbols.Length, StringComparer.Ordinal);

                foreach (var parameter in parameterSymbols)
                    map[parameter.Name] = parameter.Ordinal;

                return map;
            }
        }
        catch (Exception)
        {
            // ignored
        }

        return ordinals;
    }

    static object MaterializeArray(TypedConstant arrayConstant, ITypeSymbol parameterType)
    {
        var values = arrayConstant.Values;
        int length = values.Length;

        if (parameterType is IArrayTypeSymbol arrayType)
        {
            switch (arrayType.ElementType.SpecialType)
            {
                case SpecialType.System_String:
                {
                    var result = new string?[length];
                    for (int i = 0; i < result.Length; i++)
                        result[i] = (string?)values[i].Value;
                    return result;
                }
                case SpecialType.System_Boolean:
                {
                    var result = new bool[length];
                    for (int i = 0; i < result.Length; i++)
                        result[i] = values[i].Value is true;
                    return result;
                }
                case SpecialType.System_Int32:
                {
                    var result = new int[length];
                    for (int i = 0; i < result.Length; i++)
                        result[i] = values[i].Value is int x ? x : 0;
                    return result;
                }
                case SpecialType.System_Int64:
                {
                    var result = new long[length];
                    for (int i = 0; i < result.Length; i++)
                        result[i] = values[i].Value is long x ? x : 0;
                    return result;
                }
                case SpecialType.System_Double:
                {
                    var result = new double[length];
                    for (int i = 0; i < result.Length; i++)
                        result[i] = values[i].Value is double x ? x : 0;
                    return result;
                }
            }
        }

        var fallback = new object?[length];
        for (int i = 0; i < fallback.Length; i++)
            fallback[i] = values[i].Value;
        return fallback;
    }

    public T? Get<T>(string name, T? defaultValue)
    {
        if (values.TryGetValue(name, out var value))
            if (value is not null)
                return (T)value;

        return defaultValue;
    }

    public T? Get<T>(string name, T? defaultValue) where T : struct
    {
        if (values.TryGetValue(name, out var value))
        {
            try
            {
                return (T)value;
            }
            catch
            {
                return defaultValue;
            }
        }

        return defaultValue;
    }

    public T Select<T>(Func<AttributeConstructorArgumentsDict, T> selector)
        => selector(this);
}

public static partial class ExtensionMethods
{
    /// <summary>
    /// Retrieves a structure representing the constructor arguments of the given attribute.
    /// </summary>
    public static AttributeConstructorArgumentsDict GetAttributeConstructorArguments(this AttributeData? attributeData, CancellationToken ct = default)
        => new(attributeData, ct);
}
