using System.Diagnostics;
using System.Text;
using static System.StringComparison;

var markdownReportPath = GetOptionalArg(args, "--markdown-report");
var failed = 0;
var startedAt = Stopwatch.GetTimestamp();

DiagnosticTest[] cases =
[
    Med001Test.Case,
    Med002Test.Case,
    Med003Test.Case,
    Med004Test.Case,
    Med005Test.Case,
    Med006Test.Case,
    Med007Test.Case,
    Med008Test.Case,
    Med009Test.Case,
    Med010Test.Case,
    Med011Test.Case,
    Med012Test.Case,
    Med013Test.Case,
    Med015Test.Case,
    Med016Test.Case,
    Med017Test.Case,
    Med019Test.Case,
    Med020Test.Case,
    Med021Test.Case,
    Med022Test.Case,
    Med023Test.Case,
    Med024Test.Case,
    Med025Test.Case,
    Med026Test.Case,
    Med027Test.Case,
    Med028Test.Case,
    Med029Test.Case,
    Med030Test.Case,
    Med031Test.Case,
    Med032Test.Case,
    InjectSourceGeneratorTest.Case,
    TrackSourceGeneratorTest.Case,
    SingletonSourceGeneratorTest.Case,
    UnionSourceGeneratorTest.Case,
    UnionSourceGeneratorTest.NoDerivedHeaderCase,
    UnionSourceGeneratorTest.HeaderFieldForwardingCase,
    UnionSourceGeneratorTest.HeaderPropertyAccessorForwardingCase,
    UnionSourceGeneratorTest.WrapperCase,
    UnionSourceGeneratorTest.GenericWrapperSkipCase,
    UnionNestedSourceGeneratorTest.Case,
    UnionNestedSourceGeneratorTest.HeaderFieldForwardingCase,
    UnmanagedAccessSourceGeneratorTest.Case,
    WrapValueEnumerableSourceGeneratorTest.Case,
    ConstantsSourceGeneratorTest.Case,
    ConstantsMissingInputSourceGeneratorTest.Case,
];

var testResults = new List<TestResult>(cases.Length);
foreach (var testCase in cases)
{
    var testStartedAt = Stopwatch.GetTimestamp();

    try
    {
        testCase.Run();
        var duration = Stopwatch.GetElapsedTime(testStartedAt);
        Console.WriteLine($"[PASS] {testCase.Name} ({FormatDuration(duration)})");
        testResults.Add(new(testCase.Name, Passed: true, duration, ""));
    }
    catch (Exception ex)
    {
        failed++;
        var duration = Stopwatch.GetElapsedTime(testStartedAt);
        var failure = $"{ex.Message}{Environment.NewLine}{ex.StackTrace}";
        Console.Error.WriteLine($"[FAIL] {testCase.Name} ({FormatDuration(duration)})");
        Console.Error.WriteLine(failure);
        testResults.Add(new(testCase.Name, Passed: false, duration, failure));
    }
}

var totalDuration = Stopwatch.GetElapsedTime(startedAt);
Console.WriteLine($"Executed {cases.Length} contract tests. Failed: {failed}.");

if (!string.IsNullOrWhiteSpace(markdownReportPath))
{
    var markdown = BuildMarkdownReport([.. testResults], totalDuration);
    var fullPath = Path.GetFullPath(markdownReportPath);
    var directory = Path.GetDirectoryName(fullPath);
    if (!string.IsNullOrWhiteSpace(directory))
        Directory.CreateDirectory(directory);

    File.WriteAllText(fullPath, markdown, Encoding.UTF8);
    Console.WriteLine($"Markdown report written to '{fullPath}'.");
}

return failed;

static string? GetOptionalArg(string[] args, string name)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].Equals(name, Ordinal))
            continue;

        return i + 1 < args.Length
            ? args[i + 1]
            : throw new ArgumentException($"Missing value for argument '{name}'.");
    }

    return null;
}

static string BuildMarkdownReport(TestResult[] results, TimeSpan totalDuration)
{
    var passed = results.Count(x => x.Passed);
    var failed = results.Count(x => !x.Passed);

    var lines = new List<string>(results.Length + 16)
    {
        $"## {passed} passed, {failed} failed and 0 skipped",
        $"![Analyzer contract tests](https://img.shields.io/badge/tests-{Uri.EscapeDataString($"{passed} passed")}-{(failed == 0 ? "success" : "critical")})",
        "<details><summary>Expand for details</summary>",
        "",
        "|Test|Result|Time|",
        "|:---|:---:|---:|",
    };

    foreach (var result in results)
        lines.Add($"|{EscapeTableCell(result.Name)}|{(result.Passed ? "PASS" : "FAIL")}|{FormatDuration(result.Duration)}|");

    lines.Add("</details>");
    lines.Add("");
    lines.Add($"Total time: **{FormatDuration(totalDuration)}**.");

    if (failed is 0)
        return string.Join(Environment.NewLine, lines);

    lines.Add("");
    lines.Add("### Failed tests");

    foreach (var result in results)
    {
        if (result.Passed)
            continue;

        lines.Add($"#### {result.Name}");
        lines.Add("```text");
        lines.Add(result.Failure);
        lines.Add("```");
    }

    return string.Join(Environment.NewLine, lines);
}

static string EscapeTableCell(string value)
    => value
        .Replace("|", "\\|", Ordinal)
        .Replace("\r\n", "<br>", Ordinal)
        .Replace("\n", "<br>", Ordinal)
        .Replace("\r", "", Ordinal);

static string FormatDuration(TimeSpan duration)
    => $"{duration.TotalMilliseconds:0.00}ms";

readonly record struct TestResult(string Name, bool Passed, TimeSpan Duration, string Failure);
