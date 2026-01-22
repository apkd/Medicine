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

            if (attributeData.ApplicationSyntaxReference?.GetSyntax(ct) is not AttributeSyntax { ArgumentList: { } argumentList })
                return ordinals;

            int positionalIndex = 0;
            foreach (var arg in argumentList.Arguments)
            {
                if (arg.NameColon is not null)
                {
                    var name = arg.NameColon.Name.Identifier.ValueText;
                    var p = parameterSymbols.FirstOrDefault(p => p.Name == name);
                    if (p is not null)
                        ordinals.Add(p.Ordinal);
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
        }
        catch (Exception)
        {
            // ignored
        }

        return ordinals;
    }

    static object MaterializeArray(TypedConstant arrayConstant, ITypeSymbol parameterType)
    {
        if (parameterType is IArrayTypeSymbol arrayType)
        {
            if (arrayType.ElementType.SpecialType == SpecialType.System_String)
                return arrayConstant.Values.Select(v => (string?)v.Value).ToArray();

            if (arrayType.ElementType.SpecialType == SpecialType.System_Boolean)
                return arrayConstant.Values.Select(v => v.Value is bool b && b).ToArray();

            if (arrayType.ElementType.SpecialType == SpecialType.System_Int32)
                return arrayConstant.Values.Select(v => v.Value is int i ? i : default).ToArray();

            if (arrayType.ElementType.SpecialType == SpecialType.System_Int64)
                return arrayConstant.Values.Select(v => v.Value is long l ? l : default).ToArray();

            if (arrayType.ElementType.SpecialType == SpecialType.System_Double)
                return arrayConstant.Values.Select(v => v.Value is double d ? d : default).ToArray();
        }

        return arrayConstant.Values.Select(v => v.Value).ToArray();
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
            if (value is T typedValue)
                return typedValue;

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