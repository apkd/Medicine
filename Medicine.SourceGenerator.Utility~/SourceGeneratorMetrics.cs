using System.Collections.Concurrent;
using System.Diagnostics;

/// <summary>
/// Metric kinds reported by <see cref="SourceGeneratorMetrics"/>.
/// </summary>
public enum Stat
{
    /// <summary>
    /// Number of lines emitted for a generated file.
    /// </summary>
    LinesOfCodeGenerated,

    /// <summary>
    /// Time spent in transform stages.
    /// </summary>
    TransformTimeMs,

    /// <summary>
    /// Time spent writing generated source.
    /// </summary>
    SourceGenerationTimeMs,

    /// <summary>
    /// Total time spent across a compilation run.
    /// </summary>
    FullCompilationTimeMs,
}

/// <summary>
/// Provides functionality for recording and reporting source generator pipeline metrics.
/// </summary>
public static class SourceGeneratorMetrics
{
    /// <summary>
    /// Reports metric values into the backing dictionaries used by the generator.
    /// </summary>
    /// <param name="stats">Metric store keyed by statistic and filename.</param>
    /// <param name="shouldResetTransformTime">
    /// Tracks files whose accumulated transform time should be reset on the next report.
    /// </param>
    public readonly struct StatsReporter(
        ConcurrentDictionary<(Stat Stat, string Filename), float> stats,
        ConcurrentDictionary<string, bool> shouldResetTransformTime
    )
    {
        static readonly Stopwatch stopwatch = new();

        /// <summary>
        /// Records a metric value for a file.
        /// </summary>
        /// <param name="filename">Generated filename associated with the metric.</param>
        /// <param name="stat">Metric kind being reported.</param>
        /// <param name="value">Metric value.</param>
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

        /// <summary>
        /// Marks the start of a compilation and resets per-file transform accumulation.
        /// </summary>
        public void ReportStartCompilation()
        {
            foreach (var metric in stats.Keys)
                shouldResetTransformTime[metric.Filename] = true;
            stopwatch.Restart();
        }

        /// <summary>
        /// Marks the end of a compilation and records the total compilation time.
        /// </summary>
        public void ReportEndCompilation()
        {
            stopwatch.Stop();
            Report(nameof(Stat.FullCompilationTimeMs), Stat.FullCompilationTimeMs, (float)stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    /// <summary>
    /// Active reporter used by generator helpers, or <c>null</c> to disable metric collection.
    /// </summary>
    public static StatsReporter? Reporter { get; set; }
#if DEBUG
        = new(new(), new());
#endif
}
