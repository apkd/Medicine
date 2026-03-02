using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

#pragma warning disable RS2008

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SingletonStrategyAnalyzer : DiagnosticAnalyzer
{
    static readonly DiagnosticDescriptor MED028 = new(
        id: nameof(MED028),
        title: "Incompatible Singleton strategy options",
        messageFormat: "SingletonAttribute.Strategy options are incompatible: {0}",
        category: "Medicine",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Certain SingletonAttribute.Strategy flags cannot be combined."
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(MED028);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeAttribute, SyntaxKind.Attribute);
    }

    static void AnalyzeAttribute(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not AttributeSyntax attributeSyntax)
            return;

        if (!TryGetTargetAttribute(attributeSyntax.Name, out var isSingleton))
            return;

        string parameterName = isSingleton ? "strategy" : "singletonStrategy";
        int parameterOrdinal = isSingleton ? 0 : 2;

        if (!TryGetStrategyArgument(attributeSyntax, parameterName, parameterOrdinal, out var strategyArgument))
            return;

        if (strategyArgument.Expression.ToString().Trim() is not { Length: > 0 } expressionText)
            return;

        bool hasReplace = ContainsToken(expressionText, "Replace");
        bool hasKeepExisting = ContainsToken(expressionText, "KeepExisting");
        bool hasThrowException = ContainsToken(expressionText, "ThrowException");
        bool hasLogError = ContainsToken(expressionText, "LogError");
        bool hasLogWarning = ContainsToken(expressionText, "LogWarning");
        bool hasDestroy = ContainsToken(expressionText, "Destroy");

        bool conflictReplaceKeepExisting = hasReplace && hasKeepExisting;
        bool conflictLogging = (hasThrowException ? 1 : 0) + (hasLogError ? 1 : 0) + (hasLogWarning ? 1 : 0) > 1;
        bool conflictThrowDestroy = hasThrowException && hasDestroy;

        if (!conflictReplaceKeepExisting && !conflictLogging && !conflictThrowDestroy)
            return;

        var conflicts = new List<string>();

        void AddOnce(string value)
        {
            if (!conflicts.Contains(value, StringComparer.Ordinal))
                conflicts.Add(value);
        }

        if (conflictReplaceKeepExisting)
        {
            AddOnce("Replace");
            AddOnce("KeepExisting");
        }

        if (conflictLogging)
        {
            if (hasThrowException)
                AddOnce("ThrowException");

            if (hasLogError)
                AddOnce("LogError");

            if (hasLogWarning)
                AddOnce("LogWarning");
        }

        if (conflictThrowDestroy)
        {
            AddOnce("ThrowException");
            AddOnce("Destroy");
        }

        if (conflicts.Count == 0)
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(
                descriptor: MED028,
                location: strategyArgument.GetLocation(),
                messageArgs: string.Join(", ", conflicts)
            )
        );
    }

    static bool ContainsToken(string text, string token)
    {
        int startIndex = 0;
        while (true)
        {
            int index = text.IndexOf(token, startIndex, StringComparison.Ordinal);
            if (index < 0)
                return false;

            int beforeIndex = index - 1;
            int afterIndex = index + token.Length;

            bool beforeOk = beforeIndex < 0 || !IsIdentifierChar(text[beforeIndex]);
            bool afterOk = afterIndex >= text.Length || !IsIdentifierChar(text[afterIndex]);

            if (beforeOk && afterOk)
                return true;

            startIndex = index + token.Length;
        }

        static bool IsIdentifierChar(char value)
            => value is '_' || char.IsLetterOrDigit(value);
    }

    static bool TryGetTargetAttribute(NameSyntax nameSyntax, out bool isSingleton)
    {
        string name = nameSyntax switch
        {
            IdentifierNameSyntax id        => id.Text,
            GenericNameSyntax gen          => gen.Text,
            QualifiedNameSyntax qualified  => qualified.Right.Text,
            AliasQualifiedNameSyntax alias => alias.Name.Text,
            _                              => nameSyntax.ToString(),
        };

        isSingleton = name is Constants.SingletonAttributeNameShort or Constants.SingletonAttributeName;

        if (isSingleton)
            return true;

        return name is Constants.MedicineSettingsAttributeShort or Constants.MedicineSettingsAttribute;
    }

    static bool TryGetStrategyArgument(
        AttributeSyntax attributeSyntax,
        string parameterName,
        int parameterOrdinal,
        out AttributeArgumentSyntax strategyArgument
    )
    {
        strategyArgument = null!;

        if (attributeSyntax.ArgumentList is not { Arguments: { Count: > 0 } arguments })
            return false;

        foreach (var argument in arguments)
        {
            if (GetArgumentName(argument) is not { } name)
                continue;

            if (name != parameterName)
                continue;

            strategyArgument = argument;
            return true;
        }

        int positionalIndex = 0;
        foreach (var argument in arguments)
        {
            if (GetArgumentName(argument) is not null)
                continue;

            if (positionalIndex == parameterOrdinal)
            {
                strategyArgument = argument;
                return true;
            }

            positionalIndex++;
        }

        return false;

        static string? GetArgumentName(AttributeArgumentSyntax argument)
            => argument switch
            {
                { NameColon: { } nameColon }   => nameColon.Name.Text,
                { NameEquals: { } nameEquals } => nameEquals.Name.Text,
                _                              => null,
            };
    }
}
