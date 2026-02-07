using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;

// ReSharper disable MethodOverloadWithOptionalParameter
namespace Medicine
{
    [UsedImplicitly]
    public readonly struct Benchmark : IDisposable
    {
        readonly string name;
        readonly long ticks;

        Benchmark(string name, long ticks)
            => (this.name, this.ticks) = (name, ticks);

        static readonly double tickFrequency = Stopwatch.IsHighResolution
            ? 10000000.0 / Stopwatch.Frequency
            : 1.0;

        /// <summary> Start a new benchmark with default name (based on call location). </summary>
        public static Benchmark Start([CallerMemberName] string name = "", [CallerFilePath] string path = "", [CallerLineNumber] int cln = 0)
        {
            name = $"{name}() ({Path.GetFileName(path)}:{cln.ToString()})";
#if DEBUG
            Profiler.BeginSample($"[Benchmark] {name}");
#endif
            return new(name, Stopwatch.GetTimestamp());
        }

        /// <summary> Start a new benchmark with given name. </summary>
        public static Benchmark Start(string name)
        {
#if DEBUG
            Profiler.BeginSample("[Benchmark] " + name);
#endif
            return new(name, Stopwatch.GetTimestamp());
        }

        long ElapsedTicks
            => Stopwatch.GetTimestamp() - ticks;

        long GetElapsedDateTimeTicks()
            => Stopwatch.IsHighResolution
                ? (long)(ElapsedTicks * tickFrequency)
                : ElapsedTicks;

        TimeSpan Elapsed
            => new(GetElapsedDateTimeTicks());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            var elapsed = Elapsed.TotalMilliseconds;
#if DEBUG
            Profiler.EndSample();
#endif
#if UNITY_EDITOR
            Debug.Log($"<b>[Benchmark] <i>{name}</i></b>: {elapsed:0.00}ms");
#else
            Debug.Log($"[Benchmark] {name}: {elapsed:0.00}ms");
#endif
        }
    }
}