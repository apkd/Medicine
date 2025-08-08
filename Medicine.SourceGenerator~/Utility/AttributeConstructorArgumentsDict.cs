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

            // keep only the constructor parameters that the caller actually wrote
            var explicitOrdinals = GetExplicitConstructorOrdinals(attributeData, ct);

            foreach (int ordinal in explicitOrdinals)
                if (ctorArgs[ordinal].Value is { } arg)
                    values[ctorParams[ordinal].Name] = arg;

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
            var parameterSymbols = attributeData.AttributeConstructor!.Parameters;

            if (attributeData.ApplicationSyntaxReference?.GetSyntax(ct) is not AttributeSyntax { ArgumentList: { } argumentList })
                return ordinals; // metadata-only attribute → syntax unavailable

            int positionalIndex = 0;
            foreach (var arg in argumentList.Arguments)
            {
                // if (arg.NameEquals != null)
                //     continue;

                if (arg.NameColon != null) // paramName: expr
                {
                    var name = arg.NameColon.Name.Identifier.ValueText;
                    ordinals.Add(parameterSymbols.First(p => p.Name == name).Ordinal);
                }
                else // pure positional
                {
                    ordinals.Add(positionalIndex);
                    positionalIndex++;
                }
            }
        }
        catch (Exception)
        {
            // ignored
        }

        return ordinals;
    }

    /// <summary>
    /// Retrieves the value associated with the specified name from the attribute constructor
    /// or returns a default value if the name does not exist.
    /// </summary>
    /// <param name="defaultValue">
    /// The default value to return when the argument value is not provided.
    /// Note that the default value specified in the constructor definition is ignored.
    /// You can specify <c>null</c> to make the return value nullable, in case you want to
    /// specifically check whether the argument value was explicitly provided.
    /// </param>
    public T? Get<T>(string name, T? defaultValue)
    {
        if (values.TryGetValue(name, out var value))
            if (value is not null)
                return (T)value;

        return defaultValue;
    }

    /// <inheritdoc cref="Get{T}(string,T?)"/>
    public T? Get<T>(string name, T? defaultValue) where T : struct
    {
        if (values.TryGetValue(name, out var value))
            if (value is T typedValue)
                return typedValue;

        return defaultValue;
    }

    /// <summary>
    /// Utility method that can be used to project the constructor arguments, e.g., to a tuple.
    /// </summary>
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