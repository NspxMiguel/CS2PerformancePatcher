namespace Cs2Patcher.Core;

/// <summary>One timing series, compared between an ordinary frame and a slow one.</summary>
/// <param name="TypicalMs">Median across every frame in the run.</param>
/// <param name="SlowMs">Mean across the slowest frames only.</param>
public sealed record SpikeSeries(string Name, double TypicalMs, double SlowMs)
{
    /// <summary>How many times worse this series gets in a slow frame. The largest ratio is the culprit.</summary>
    public double Ratio => TypicalMs > 0 ? SlowMs / TypicalMs : 0;
}

/// <summary>
/// Why the lows are bad, which is a different question from why the average is bad.
///
/// An average is moved by steady-state cost: resolution, shadow distance, how much geometry is
/// on screen. A 1% low is moved by whatever goes wrong on forty frames out of four thousand,
/// and cutting steady-state cost does not necessarily touch it. In this project the average
/// more than doubled across the profile ladder while the 1% low barely moved, which is exactly
/// the shape of a problem that settings cannot reach.
///
/// So rather than guess, this takes the slowest frames apart. If the GPU series is what
/// inflates, the spikes are draw cost and settings will help. If it is CPU-game, they are
/// simulation or asset streaming, and no graphics setting will touch them.
/// </summary>
public sealed record SpikeReport(
    int TotalFrames,
    int SlowFrames,
    double SlowThresholdMs,
    SpikeSeries Effective,
    SpikeSeries Gpu,
    SpikeSeries Cpu,
    SpikeSeries CpuGame,
    SpikeSeries CpuRender,
    int CpuBlamed,
    int Bursts,
    int LongestBurst,
    IReadOnlyList<KeyValuePair<string, int>> ByPhase)
{
    /// <summary>
    /// Whichever series inflates most in a slow frame, ignoring the frame cost itself and the
    /// CPU total, both of which are sums rather than causes.
    /// </summary>
    public SpikeSeries Culprit =>
        new[] { Gpu, CpuGame, CpuRender }.OrderByDescending(s => s.Ratio).First();

    /// <summary>Slow frames that arrive next to each other, rather than scattered.</summary>
    public bool IsBursty => SlowFrames > 0 && (double)SlowFrames / Math.Max(1, Bursts) >= 2.0;

    public static SpikeReport? From(BenchmarkRun run, double fraction = 0.01)
    {
        var effective = run.EffectiveMs;
        var n = effective.Count;
        if (n == 0) return null;

        var count = Math.Max(1, (int)Math.Ceiling(n * fraction));

        // Slowest `count` frames, kept as indices so every other series can be sampled at the
        // same moments. Sorting the values alone would lose that.
        var slow = Enumerable.Range(0, n)
            .OrderByDescending(i => effective[i])
            .Take(count)
            .OrderBy(i => i)
            .ToArray();

        var threshold = slow.Min(i => effective[i]);

        var cpuBlamed = slow.Count(i =>
            i < run.CpuMs.Count && i < run.GpuMs.Count && run.CpuMs[i] > run.GpuMs[i]);

        // Consecutive indices are one event, not several. A hitch that spans six frames says
        // something different from six hitches scattered across the run.
        var bursts = 0;
        var longest = 0;
        var current = 0;
        for (var k = 0; k < slow.Length; k++)
        {
            if (k == 0 || slow[k] != slow[k - 1] + 1)
            {
                bursts++;
                current = 1;
            }
            else current++;

            if (current > longest) longest = current;
        }

        return new SpikeReport(
            TotalFrames: n,
            SlowFrames: count,
            SlowThresholdMs: threshold,
            Effective: Series("frame cost", effective, slow),
            Gpu: Series("GPU", run.GpuMs, slow),
            Cpu: Series("CPU total", run.CpuMs, slow),
            CpuGame: Series("CPU game", run.CpuGameMs, slow),
            CpuRender: Series("CPU render", run.CpuRenderMs, slow),
            CpuBlamed: cpuBlamed,
            Bursts: bursts,
            LongestBurst: longest,
            ByPhase: CountByPhase(run, slow));
    }

    private static SpikeSeries Series(string name, IReadOnlyList<double> values, int[] slowIndices)
    {
        if (values.Count == 0) return new SpikeSeries(name, 0, 0);

        var sorted = values.ToArray();
        Array.Sort(sorted);
        var typical = sorted[sorted.Length / 2];

        var sum = 0.0;
        var taken = 0;
        foreach (var i in slowIndices)
        {
            if (i >= values.Count) continue;
            sum += values[i];
            taken++;
        }

        return new SpikeSeries(name, typical, taken > 0 ? sum / taken : 0);
    }

    /// <summary>
    /// Where in the run the slow frames land. Spikes that pile into the speed-3 phase are
    /// simulation keeping up; spikes spread evenly are not.
    /// </summary>
    private static IReadOnlyList<KeyValuePair<string, int>> CountByPhase(BenchmarkRun run, int[] slowIndices)
    {
        if (run.PhaseMarkers.Count != 2)
            return [new KeyValuePair<string, int>("whole run", slowIndices.Length)];

        var names = new[] { "paused", "speed 1", "speed 3" };
        var counts = new int[3];

        foreach (var i in slowIndices)
        {
            var phase = i < run.PhaseMarkers[0] ? 0 : i < run.PhaseMarkers[1] ? 1 : 2;
            counts[phase]++;
        }

        return [.. names.Select((name, i) => new KeyValuePair<string, int>(name, counts[i]))];
    }
}
