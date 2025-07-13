using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

#pragma warning disable RS2008

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class VersionMigrationAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor MED012 = new(
        id: nameof(MED012),
        title: "Migrate injected property",
        messageFormat: "Property '{0}' should be migrated to the new injection syntax",
        category: "Refactoring",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(MED012);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeProperty, SyntaxKind.PropertyDeclaration);
    }

    static readonly string[] attributeFQNs =
    [
        "global::Medicine.Inject",
        "global::Medicine.InjectAttribute",
        "global::Medicine.Inject.All",
        "global::Medicine.Inject.Single",
        "global::Medicine.Inject.FromChildren",
        "global::Medicine.Inject.FromParents",
        "global::Medicine.Inject.Lazy",
        "global::Medicine.Inject.FromChildren.Lazy",
        "global::Medicine.Inject.FromParents.Lazy",
        "global::Medicine.Inject.All.Lazy",
        "global::Medicine.Inject.Single.Lazy",
    ];

    static void AnalyzeProperty(SyntaxNodeAnalysisContext context)
    {
        var prop = (PropertyDeclarationSyntax)context.Node;

        bool AttributePredicate(string attributeName)
            => attributeName is
                "Inject" or
                "Medicine.Inject" or
                "Inject.All" or
                "Medicine.Inject.All" or
                "Inject.Single" or
                "Medicine.Inject.Single" or
                "Inject.FromChildren" or
                "Inject.FromParents" or
                "Medicine.Inject.FromChildren" or
                "Medicine.Inject.FromParents" or
                "Inject.Lazy" or
                "Medicine.Inject.Lazy" or
                "Inject.FromChildren.Lazy" or
                "Inject.FromParents.Lazy" or
                "Medicine.Inject.FromChildren.Lazy" or
                "Medicine.Inject.FromParents.Lazy";

        if (prop.HasAttribute(AttributePredicate))
            if (prop.AccessorList?.Accessors.Count is 1)
                if (prop.AccessorList.Accessors[0].Kind() == SyntaxKind.GetAccessorDeclaration)
                    if (context.SemanticModel.GetDeclaredSymbol(prop) is { } symbol)
                        if (symbol.HasAttribute(attributeFQNs.Contains))
                            context.ReportDiagnostic(Diagnostic.Create(MED012, prop.GetLocation(), prop.Identifier.Text));
    }
}