using System.Diagnostics;

namespace MIGraphXSharp.Samples.Yolo26;

public sealed record TimedValue<T>(T Value, IReadOnlyDictionary<string, double[]> Stages);
public sealed record LatencyStatistics(int Samples, double Mean, double P50, double P95, double Minimum, double Maximum);
public sealed record TimingReport(int WarmupIterations, int MeasuredIterations, string Unit,
    double FirstPassMilliseconds, IReadOnlyDictionary<string, double[]> FirstPassStages,
    LatencyStatistics Pipeline, IReadOnlyDictionary<string, LatencyStatistics> Stages,
    double ImagesPerSecond, double[] PipelineSamples, IReadOnlyDictionary<string, double[]> StageSamples);

/// <summary>Sequential wall-clock measurements; no artifact I/O belongs inside the measured delegate.</summary>
public static class Benchmarking
{
    public static (T Value, TimingReport Report) Measure<T>(Func<TimedValue<T>> run, int warmups, int iterations)
    {
        if (warmups < 0 || iterations < 1) throw new ArgumentOutOfRangeException(nameof(iterations));
        var pipeline = new List<double>();
        var stages = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        TimedValue<T>? last = null;
        double firstPass = 0;
        IReadOnlyDictionary<string, double[]>? firstStages = null;
        for (var index = 0; index < checked(warmups + iterations); ++index)
        {
            var timer = Stopwatch.StartNew();
            last = run();
            var elapsed = timer.Elapsed.TotalMilliseconds;
            if (index == 0) { firstPass = elapsed; firstStages = last.Stages; }
            if (index < warmups) continue;
            pipeline.Add(elapsed);
            foreach (var (name, values) in last.Stages)
            {
                if (!stages.TryGetValue(name, out var samples)) stages[name] = samples = [];
                samples.AddRange(values);
            }
        }
        var statistics = Summarize(pipeline);
        return (last!.Value, new TimingReport(warmups, iterations, "milliseconds", firstPass, firstStages!, statistics,
            stages.ToDictionary(item => item.Key, item => Summarize(item.Value)),
            1000 / statistics.Mean, pipeline.ToArray(), stages.ToDictionary(item => item.Key, item => item.Value.ToArray())));
    }

    public static LatencyStatistics Summarize(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0 || values.Any(value => !double.IsFinite(value) || value < 0))
            throw new ArgumentException("Latency samples must be finite, nonnegative and nonempty.", nameof(values));
        var sorted = values.Order().ToArray();
        // Nearest-rank percentiles, with the definition retained in the sample README.
        double Percentile(double fraction) => sorted[Math.Max(0, (int)Math.Ceiling(fraction * sorted.Length) - 1)];
        return new LatencyStatistics(sorted.Length, sorted.Average(), Percentile(.5), Percentile(.95), sorted[0], sorted[^1]);
    }
}
