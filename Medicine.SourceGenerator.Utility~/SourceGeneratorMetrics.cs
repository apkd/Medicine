using System.Collections.Concurrent;
using System.Diagnostics;

public enum Stat
{
    LinesOfCodeGenerated,
    TransformTimeMs,
    SourceGenerationTimeMs,
    FullCompilationTimeMs,
}

public static class SourceGeneratorMetrics
{
    public readonly struct StatsReporter(
        ConcurrentDictionary<(Stat Stat, string Filename), float> stats,
        ConcurrentDictionary<string, bool> shouldResetTransformTime
    )
    {
        static readonly Stopwatch stopwatch = new();

        public void Report(string? filename, Stat stat, float value)
        {
            if (filename is null)
                return;

            switch (stat)
            {
                case Stat.TransformTimeMs when !shouldResetTransformTime.ContainsKey(filename):
                    stats.AddOrUpdate((stat, filename), value, (key, previous) => previous + value);
                    break;
                case Stat.TransformTimeMs:
                    shouldResetTransformTime.TryRemove(filename, out _);
                    goto default;
                case Stat.SourceGenerationTimeMs:
                case Stat.LinesOfCodeGenerated:
                case Stat.FullCompilationTimeMs:
                default:
                    stats[(stat, filename)] = value;
                    break;
            }
        }

        public void ReportStartCompilation()
        {
            foreach (var metric in stats.Keys)
                shouldResetTransformTime[metric.Filename] = true;
            stopwatch.Restart();
        }

        public void ReportEndCompilation()
        {
            stopwatch.Stop();
            Report(nameof(Stat.FullCompilationTimeMs), Stat.FullCompilationTimeMs, (float)stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    public static StatsReporter? Reporter { get; set; }
#if DEBUG
        = new(new(), new());
#endif
}
