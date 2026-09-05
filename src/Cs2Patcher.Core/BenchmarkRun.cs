using System.Text.Json.Nodes;

namespace Cs2Patcher.Core;

/// <summary>
/// Frame timing summarised the way it is actually felt. The average is the number people
/// quote; the lows are the number people notice.
/// </summary>
/// <param name="Low1PctFps">
/// The mean of the slowest 1% of frames, expressed as a rate. This is the "1% low" as
/// hardware reviewers use it — not the 99th percentile frame, which is a single sample and
/// says nothing about how bad the tail behind it gets.
/// </param>
public sealed record FrameStats(
    int Frames,
    double AvgMs,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double AvgFps,
    double Low1PctFps,
    double Low01PctFps)
{
    public static readonly FrameStats Empty = new(0, 0, 0, 0, 0, 0, 0, 0);

    public static FrameStats From(IReadOnlyList<double> frameTimesMs)
    {
        if (frameTimesMs.Count == 0) return Empty;

        var sorted = frameTimesMs.ToArray();
        Array.Sort(sorted);

        var mean = frameTimesMs.Average();
        return new FrameStats(
            Frames: frameTimesMs.Count,
            AvgMs: mean,
            P50Ms: Percentile(sorted, 0.50),
            P95Ms: Percentile(sorted, 0.95),
            P99Ms: Percentile(sorted, 0.99),
            AvgFps: mean > 0 ? 1000.0 / mean : 0,
            Low1PctFps: SlowestFractionFps(sorted, 0.01),
            Low01PctFps: SlowestFractionFps(sorted, 0.001));
    }

    private static double Percentile(double[] ascending, double q)
    {
        var index = Math.Clamp((int)Math.Ceiling(q * ascending.Length) - 1, 0, ascending.Length - 1);
        return ascending[index];
    }

    /// <summary>Mean of the slowest fraction of frames, as a frame rate.</summary>
    private static double SlowestFractionFps(double[] ascending, double fraction)
    {
        var count = Math.Max(1, (int)Math.Ceiling(ascending.Length * fraction));
        var sum = 0.0;
        for (var i = ascending.Length - count; i < ascending.Length; i++) sum += ascending[i];
        var mean = sum / count;
        return mean > 0 ? 1000.0 / mean : 0;
    }
}

/// <summary>
/// One run of the benchmark the game ships with, read back from the file it leaves behind.
///
/// The benchmark is worth more to this project than hand-played sessions: it flies a fixed
/// camera path over a city the game bundles, for the same 90 seconds every time (20s paused,
/// 35s at speed 1, 35s at speed 3). Two runs therefore differ only by what we changed. It
/// also records the CPU and GPU cost of every individual frame, which is what makes real
/// lows — rather than averages — computable after the fact.
/// </summary>
public sealed class BenchmarkRun
{
    public const string SectionName = "Benchmark Settings";

    public required string Label { get; init; }
    public string? GameVersion { get; init; }
    public string? GraphicsQuality { get; init; }
    public string? ScreenResolution { get; init; }
    public string? Gpu { get; init; }
    public string? Cpu { get; init; }

    public int FramesRendered { get; init; }
    public int SimulationTicks { get; init; }

    /// <summary>The game's own headline number, kept so our arithmetic can be checked against it.</summary>
    public double ReportedAverageFps { get; init; }

    /// <summary>Share of frames the game considered GPU-bound. High means cutting draw work is the lever.</summary>
    public double GpuBoundPercent { get; init; }

    public double LoadingTimeSecs { get; init; }

    public IReadOnlyList<double> GpuMs { get; init; } = [];
    public IReadOnlyList<double> CpuMs { get; init; } = [];
    public IReadOnlyList<double> CpuRenderMs { get; init; } = [];
    public IReadOnlyList<double> CpuGameMs { get; init; } = [];
    public IReadOnlyList<int> PhaseMarkers { get; init; } = [];

    /// <summary>
    /// What the frame actually cost. The CPU and GPU run in parallel, so the frame is paced by
    /// whichever finishes last — averaging the two, or reading either alone, understates it.
    /// </summary>
    public IReadOnlyList<double> EffectiveMs
    {
        get
        {
            var n = Math.Min(CpuMs.Count, GpuMs.Count);
            if (n == 0) return CpuMs.Count > 0 ? CpuMs : GpuMs;

            var result = new double[n];
            for (var i = 0; i < n; i++) result[i] = Math.Max(CpuMs[i], GpuMs[i]);
            return result;
        }
    }

    public FrameStats Effective => FrameStats.From(EffectiveMs);
    public FrameStats GpuOnly => FrameStats.From(GpuMs);
    public FrameStats CpuTotal => FrameStats.From(CpuMs);
    public FrameStats CpuRender => FrameStats.From(CpuRenderMs);

    /// <summary>
    /// The run split into the three phases the benchmark drives. The paused phase is the
    /// cleanest read on rendering alone; the speed-3 phase is where simulation load peaks.
    /// </summary>
    public IEnumerable<(string Name, FrameStats Stats)> Phases()
    {
        var frames = EffectiveMs;
        if (frames.Count == 0) yield break;

        // Markers are the frame indices where the phase changed. Anything other than the two
        // we expect means the format moved, so report the run whole rather than guess at splits.
        if (PhaseMarkers.Count != 2)
        {
            yield return ("whole run", FrameStats.From(frames));
            yield break;
        }

        var names = new[] { "paused", "speed 1", "speed 3" };
        var bounds = new[] { 0, PhaseMarkers[0], PhaseMarkers[1], frames.Count };

        for (var i = 0; i < names.Length; i++)
        {
            var start = Math.Clamp(bounds[i], 0, frames.Count);
            var end = Math.Clamp(bounds[i + 1], start, frames.Count);
            if (end <= start) continue;

            var slice = new double[end - start];
            for (var j = 0; j < slice.Length; j++) slice[j] = frames[start + j];
            yield return (names[i], FrameStats.From(slice));
        }
    }

    public static BenchmarkRun Load(string path, string? label = null)
    {
        var name = Path.GetFileName(path);
        var doc = CocDocument.Load(path);

        var section = doc.GetSection(SectionName)
            ?? throw new InvalidOperationException($"'{name}' has no '{SectionName}' section — is it a benchmark result?");

        if (section.Json["latestResult"] is not JsonObject result)
            throw new InvalidOperationException($"'{name}' has no benchmark result in it.");

        if (result["hasData"]?.GetValue<bool>() != true)
            throw new InvalidOperationException($"'{name}' holds an empty result — the run never finished.");

        return new BenchmarkRun
        {
            Label = label ?? LabelFromFileName(path),
            GameVersion = result["gameVersion"]?.GetValue<string>(),
            GraphicsQuality = result["graphicsQuality"]?.GetValue<string>(),
            ScreenResolution = result["screenResolution"]?.GetValue<string>(),
            Gpu = result["gpuModel"]?.GetValue<string>(),
            Cpu = result["cpuModel"]?.GetValue<string>(),
            FramesRendered = (int)JsonNumber.Read(result["framesRendered"]),
            SimulationTicks = (int)JsonNumber.Read(result["simulationTicks"]),
            ReportedAverageFps = JsonNumber.Read(result["averageFps"]),
            GpuBoundPercent = JsonNumber.Read(result["gpuBoundPercent"]),
            LoadingTimeSecs = JsonNumber.Read(result["loadingTimeSecs"]),
            GpuMs = Numbers(result["gpuFrameTimes"]),
            CpuMs = Numbers(result["cpuFrameTimes"]),
            CpuRenderMs = Numbers(result["cpuRenderFrameTimes"]),
            CpuGameMs = Numbers(result["cpuGameFrameTimes"]),
            PhaseMarkers = Numbers(result["phaseMarkers"]).Select(v => (int)v).ToArray(),
        };
    }

    /// <summary>Results are filed as Benchmark.&lt;label&gt;.coc by the runner script.</summary>
    private static string LabelFromFileName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.StartsWith("Benchmark.", StringComparison.OrdinalIgnoreCase)
            ? name["Benchmark.".Length..]
            : name;
    }

    private static double[] Numbers(JsonNode? node)
    {
        if (node is not JsonArray array) return [];

        var values = new List<double>(array.Count);
        foreach (var item in array)
            if (JsonNumber.TryRead(item, out var v)) values.Add(v);
        return [.. values];
    }
}
