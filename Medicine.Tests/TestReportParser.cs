using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using static System.StringComparison;

static class TestReportParser
{
    const string TestCaseOpenTag = "<test-case ";
    const string TestCaseCloseTag = "</test-case>";
    const string CDataOpenTag = "<![CDATA[";
    const string CDataCloseTag = "]]>";
    static readonly CultureInfo invariant = CultureInfo.InvariantCulture;

    internal readonly struct ParsedTestRun
    {
        public readonly string Mode;
        public readonly string Result;
        public readonly int Total;
        public readonly int Passed;
        public readonly int Failed;
        public readonly int Inconclusive;
        public readonly int Skipped;
        public readonly double DurationSeconds;
        public readonly ParsedTestCase[] NonPassingCases;

        public ParsedTestRun(
            string mode,
            string result,
            int total,
            int passed,
            int failed,
            int inconclusive,
            int skipped,
            double durationSeconds,
            ParsedTestCase[] nonPassingCases
        )
        {
            Mode = mode;
            Result = result;
            Total = total;
            Passed = passed;
            Failed = failed;
            Inconclusive = inconclusive;
            Skipped = skipped;
            DurationSeconds = durationSeconds;
            NonPassingCases = nonPassingCases;
        }
    }

    internal readonly struct ParsedTestCase
    {
        public readonly string Mode;
        public readonly string Name;
        public readonly string Result;
        public readonly string Message;
        public readonly string StackTrace;

        public ParsedTestCase(string mode, string name, string result, string message, string stackTrace)
        {
            Mode = mode;
            Name = name;
            Result = result;
            Message = message;
            StackTrace = stackTrace;
        }
    }

    public static ParsedTestRun ParseTestRunReport(string filePath, string mode)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Test result file was not found for {mode} run.", filePath);

        var xml = File.ReadAllText(filePath);
        var testRunTag = GetOpeningTag(xml, "test-run");

        var nonPassingCases = ParseNonPassingCases(xml, mode);
        return new(
            mode: mode,
            result: GetAttributeValue(testRunTag, "result"),
            total: ParseInt(GetAttributeValue(testRunTag, "total")),
            passed: ParseInt(GetAttributeValue(testRunTag, "passed")),
            failed: ParseInt(GetAttributeValue(testRunTag, "failed")),
            inconclusive: ParseInt(GetAttributeValue(testRunTag, "inconclusive")),
            skipped: ParseInt(GetAttributeValue(testRunTag, "skipped")),
            durationSeconds: ParseDouble(GetAttributeValue(testRunTag, "duration")),
            nonPassingCases: nonPassingCases
        );
    }

    public static string BuildCombinedReport(ParsedTestRun editMode, ParsedTestRun playMode)
    {
        var nonPassingCount = editMode.NonPassingCases.Length + playMode.NonPassingCases.Length;
        var report = new StringBuilder(1024);

        report.AppendLine("Test Report");
        report.AppendLine(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", invariant));
        report.AppendLine();

        AppendSummaryLine(
            report: report,
            mode: editMode.Mode,
            result: editMode.Result,
            total: editMode.Total,
            passed: editMode.Passed,
            failed: editMode.Failed,
            skipped: editMode.Skipped,
            inconclusive: editMode.Inconclusive,
            durationSeconds: editMode.DurationSeconds
        );

        AppendSummaryLine(
            report: report,
            mode: playMode.Mode,
            result: playMode.Result,
            total: playMode.Total,
            passed: playMode.Passed,
            failed: playMode.Failed,
            skipped: playMode.Skipped,
            inconclusive: playMode.Inconclusive,
            durationSeconds: playMode.DurationSeconds
        );

        report.AppendLine();
        report.Append("Failed tests: ").AppendLine(nonPassingCount.ToString(invariant));

        if (nonPassingCount == 0)
            report.AppendLine("All good!");
        else
        {
            AppendNonPassingCases(report, editMode.NonPassingCases);
            AppendNonPassingCases(report, playMode.NonPassingCases);
        }

        return report.ToString();
    }

    static ParsedTestCase[] ParseNonPassingCases(string xml, string mode)
    {
        var cases = new List<ParsedTestCase>(8);
        var searchIndex = 0;

        while (true)
        {
            var caseStart = xml.IndexOf(TestCaseOpenTag, searchIndex, Ordinal);
            if (caseStart < 0)
                break;

            var openTagEnd = xml.IndexOf('>', caseStart);
            if (openTagEnd < 0)
                break;

            var caseTag = xml.Substring(caseStart, openTagEnd - caseStart + 1);
            var result = GetAttributeValue(caseTag, "result");
            var isSelfClosing = caseTag.EndsWith("/>", Ordinal);

            var contentStart = openTagEnd + 1;
            var caseClose = isSelfClosing
                ? contentStart
                : xml.IndexOf(TestCaseCloseTag, contentStart, Ordinal);

            if (caseClose < 0)
            {
                searchIndex = contentStart;
                continue;
            }

            if (!string.Equals(result, "Passed", Ordinal))
            {
                var name = GetAttributeValue(caseTag, "fullname");
                if (name is not { Length: > 0 })
                    name = GetAttributeValue(caseTag, "name");

                var caseXml = isSelfClosing
                    ? ""
                    : xml.Substring(contentStart, caseClose - contentStart);

                var message = ExtractFirstTagText(caseXml, "message");
                var stackTrace = ExtractFirstTagText(caseXml, "stack-trace");

                cases.Add(
                    new(
                        mode: mode,
                        name: name,
                        result: result,
                        message: message,
                        stackTrace: stackTrace
                    )
                );
            }

            searchIndex = caseClose + (isSelfClosing ? 0 : TestCaseCloseTag.Length);
        }

        return cases.Count == 0
            ? Array.Empty<ParsedTestCase>()
            : cases.ToArray();
    }

    static void AppendSummaryLine(
        StringBuilder report,
        string mode,
        string result,
        int total,
        int passed,
        int failed,
        int skipped,
        int inconclusive,
        double durationSeconds
    ) => report.Append(mode)
        .Append(": ")
        .Append(result)
        .Append(" | total=")
        .Append(total.ToString(invariant))
        .Append(" | passed=")
        .Append(passed.ToString(invariant))
        .Append(" | failed=")
        .Append(failed.ToString(invariant))
        .Append(" | skipped=")
        .Append(skipped.ToString(invariant))
        .Append(" | inconclusive=")
        .Append(inconclusive.ToString(invariant))
        .Append(" | duration=")
        .Append(durationSeconds.ToString("0.###", invariant))
        .AppendLine("s");

    static void AppendNonPassingCases(StringBuilder report, ParsedTestCase[] testCases)
    {
        for (var i = 0; i < testCases.Length; i++)
        {
            ref readonly var testCase = ref testCases[i];
            report.Append("[")
                .Append(testCase.Mode)
                .Append("] ")
                .Append(testCase.Result)
                .Append(" | ")
                .AppendLine(testCase.Name);

            if (!string.IsNullOrWhiteSpace(testCase.Message))
                report.Append("  message: ").AppendLine(ToSingleLine(testCase.Message));

            if (!string.IsNullOrWhiteSpace(testCase.StackTrace))
                report.Append("  stack: ").AppendLine(ToSingleLine(testCase.StackTrace));
        }
    }

    static string ToSingleLine(string text)
    {
        const int maxLength = 320;

        var singleLine = text
            .Replace("\r\n", " ", Ordinal)
            .Replace('\n', ' ')
            .Replace('\r', ' ')
            .Trim();

        if (singleLine.Length <= maxLength)
            return singleLine;

        return $"{singleLine[..(maxLength - 3)]}...";
    }

    static string GetOpeningTag(string xml, string tagName)
    {
        var opening = $"<{tagName}";
        var startIndex = xml.IndexOf(opening, Ordinal);
        if (startIndex < 0)
            return "";

        var endIndex = xml.IndexOf('>', startIndex);
        if (endIndex < 0)
            return "";

        return xml.Substring(startIndex, endIndex - startIndex + 1);
    }

    static string GetAttributeValue(string tag, string attributeName)
    {
        if (tag is not { Length: > 0 })
            return "";

        var attributeStart = $"{attributeName}=\"";
        var valueStartIndex = tag.IndexOf(attributeStart, Ordinal);
        if (valueStartIndex < 0)
            return "";

        valueStartIndex += attributeStart.Length;
        var valueEndIndex = tag.IndexOf('"', valueStartIndex);
        if (valueEndIndex < 0)
            return "";

        return tag.Substring(valueStartIndex, valueEndIndex - valueStartIndex);
    }

    static string ExtractFirstTagText(string xml, string tagName)
    {
        if (xml is not { Length: > 0 })
            return "";

        var opening = $"<{tagName}";
        var openIndex = xml.IndexOf(opening, Ordinal);
        if (openIndex < 0)
            return "";

        var tagEndIndex = xml.IndexOf('>', openIndex);
        if (tagEndIndex < 0)
            return "";

        var valueStartIndex = tagEndIndex + 1;
        if (StartsWith(xml, valueStartIndex, CDataOpenTag))
        {
            valueStartIndex += CDataOpenTag.Length;
            var cdataEndIndex = xml.IndexOf(CDataCloseTag, valueStartIndex, Ordinal);
            if (cdataEndIndex < 0)
                return "";

            return xml.Substring(valueStartIndex, cdataEndIndex - valueStartIndex).Trim();
        }

        var closeTag = $"</{tagName}>";
        var closeIndex = xml.IndexOf(closeTag, valueStartIndex, Ordinal);
        if (closeIndex < 0)
            return "";

        return DecodeXmlEntities(xml.Substring(valueStartIndex, closeIndex - valueStartIndex).Trim());
    }

    static bool StartsWith(string text, int startIndex, string value)
    {
        if (startIndex < 0 || startIndex + value.Length > text.Length)
            return false;

        for (var i = 0; i < value.Length; i++)
            if (text[startIndex + i] != value[i])
                return false;

        return true;
    }

    static int ParseInt(string value)
        => int.TryParse(value, NumberStyles.Integer, invariant, out var parsed)
            ? parsed
            : 0;

    static double ParseDouble(string value)
        => double.TryParse(value, NumberStyles.Float, invariant, out var parsed)
            ? parsed
            : 0d;

    static string DecodeXmlEntities(string value)
    {
        if (value.IndexOf('&') < 0)
            return value;

        return value
            .Replace("&lt;", "<", Ordinal)
            .Replace("&gt;", ">", Ordinal)
            .Replace("&quot;", "\"", Ordinal)
            .Replace("&apos;", "'", Ordinal)
            .Replace("&amp;", "&", Ordinal);
    }
}
