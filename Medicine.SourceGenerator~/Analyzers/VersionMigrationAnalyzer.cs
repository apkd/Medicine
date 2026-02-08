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

    static void AnalyzeProperty(SyntaxNodeAnalysisContext context)
    {
        var prop = (PropertyDeclarationSyntax)context.Node;

        bool AttributePredicate(NameSyntax attributeName)
            => attributeName.MatchesQualifiedNamePattern("Medicine.InjectAttribute", namespaceSegments: 1, skipEnd: "Attribute") ||
               attributeName.MatchesQualifiedNamePattern("Medicine.Inject.All", namespaceSegments: 1) ||
               attributeName.MatchesQualifiedNamePattern("Medicine.Inject.Single", namespaceSegments: 1) ||
               attributeName.MatchesQualifiedNamePattern("Medicine.Inject.FromChildren", namespaceSegments: 1) ||
               attributeName.MatchesQualifiedNamePattern("Medicine.Inject.FromParents", namespaceSegments: 1) ||
               attributeName.MatchesQualifiedNamePattern("Medicine.Inject.Lazy", namespaceSegments: 1) ||
               attributeName.MatchesQualifiedNamePattern("Medicine.Inject.FromChildren.Lazy", namespaceSegments: 1) ||
               attributeName.MatchesQualifiedNamePattern("Medicine.Inject.FromParents.Lazy", namespaceSegments: 1);

        if (prop.HasAttribute(AttributePredicate))
            if (prop.AccessorList?.Accessors.Count is 1 or 2)
                if (prop.AccessorList.Accessors.Any(x => x.Keyword.IsKind(SyntaxKind.GetKeyword)))
                    context.ReportDiagnostic(Diagnostic.Create(MED012, prop.GetLocation(), prop.Identifier.Text));
    }
}
