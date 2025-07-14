using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Composition;
using System.Diagnostics;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using static Constants;

#pragma warning disable RS2008
#pragma warning disable RS1012

public enum Stat
{
    LinesOfCodeGenerated,
    TransformTimeMs,
    SourceGenerationTimeMs,
    FullCompilationTimeMs,
}

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MedicineMetrics : DiagnosticAnalyzer
{
    public struct StatsReporter
    {
        static readonly Stopwatch stopwatch = new();

        public void Report(string? filename, Stat stat, float value)
        {
            if (filename is not null)
                Stats[(stat, filename)] = value;
        }

        public void ReportStartCompilation()
            => stopwatch.Restart();

        public void ReportEndCompilation()
        {
            stopwatch.Stop();
            Report(nameof(Stat.FullCompilationTimeMs), Stat.FullCompilationTimeMs, (float)stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    public static StatsReporter? Reporter { get; private set; }
#if DEBUG
        = new();
#endif

    static readonly Dictionary<(Stat Stat, string Filename), float> Stats = new();

    public static readonly DiagnosticDescriptor MedicineStatsDiagnosticDescriptor = new(
        id: nameof(MedicineMetrics),
        title: "Medicine Metrics",
        messageFormat: "'{0}'",
        category: "Debug",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(MedicineStatsDiagnosticDescriptor);

    public override void Initialize(AnalysisContext context)
    {
        context.RegisterCompilationStartAction(x => Reporter?.ReportStartCompilation());
        context.RegisterCompilationAction(x => Reporter?.ReportEndCompilation());
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            ctx =>
            {
                var prop = (EnumDeclarationSyntax)ctx.Node;

                if (prop.Identifier.ValueText is not $"{m}MedicineStats")
                    return;

                // enable reporting - we don't do this to avoid the overhead when
                // the MedicineStats enum is not defined anywhere
                Reporter = new();

                var summedStats
                    = Stats
                        .ToLookup(x => x.Key.Stat, x => x.Value)
                        .ToDictionary(x => x.Key, x => x.Sum());

                string PerFile(Stat stat, string unit = "ms", string format = "0.00")
                {
                    var values = Stats
                        .Where(x => x.Key.Stat == stat && x.Value > 0)
                        .OrderByDescending(x => x.Value)
                        .Select(x => $"{x.Key.Filename}: {x.Value.ToString(format)}{unit}")
                        .ToArray();

                    if (values.Length is 0)
                        return "";

                    return string.Join(
                        separator: "\n\x00a0\x00a0|\x00a0",
                        values: Enumerable.Concat([""], values)
                    );
                }

                float Total(Stat stat)
                    => summedStats.TryGetValue(stat, out var value) ? value : 0;

                string statsString =
                    $"""

                     - Lines of code generated: {Total(Stat.LinesOfCodeGenerated)}
                     - Total compilation time: {Total(Stat.FullCompilationTimeMs):0.00}ms
                     - Total syntax transform time: {Total(Stat.TransformTimeMs):0.00}ms {PerFile(Stat.TransformTimeMs)}
                     - Total source generation time: {Total(Stat.SourceGenerationTimeMs):0.00}ms {PerFile(Stat.SourceGenerationTimeMs)}
                     """;

                ctx.ReportDiagnostic(Diagnostic.Create(MedicineStatsDiagnosticDescriptor, prop.GetLocation(), statsString));
            }, SyntaxKind.EnumDeclaration
        );
    }

    [ExportCodeFixProvider(LanguageNames.CSharp), Shared]
    sealed class ResetFixProvider : CodeFixProvider
    {
        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Reset stats", token =>
                    {
                        foreach (var key in Stats.Keys.ToArray())
                            Stats[key] = 0;

                        return Task.FromResult(context.Document);
                    }, nameof(MedicineMetrics)
                ), context.Diagnostics.First()
            );

            return Task.CompletedTask;
        }

        public override FixAllProvider? GetFixAllProvider()
            => null;

        public override ImmutableArray<string> FixableDiagnosticIds
            => ImmutableArray.Create(MedicineStatsDiagnosticDescriptor.Id);
    }
}