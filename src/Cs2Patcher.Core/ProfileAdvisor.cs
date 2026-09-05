namespace Cs2Patcher.Core;

/// <summary>How strictly a frame rate has to be met before a profile counts as reaching it.</summary>
public enum Criterion
{
    /// <summary>The average frame rate reaches it. What a benchmark headline means.</summary>
    Average,

    /// <summary>
    /// The slowest 1% of frames reach it, so nothing visibly drops below. This is what people
    /// mean by "stable", and it is a much harder bar: a profile averaging 61 fps here has a 1%
    /// low of 39, and those forty frames in four thousand are the ones anybody notices.
    /// </summary>
    Stable,
}

/// <summary>Why a profile was recommended, in the words a person would use.</summary>
/// <param name="Because">One sentence naming the thing about this machine that decided it.</param>
/// <param name="ExpectedFps">Projected average. An estimate, and labelled one wherever it shows.</param>
/// <param name="ExpectedLow">Projected 1% low — the number that decides whether it feels smooth.</param>
public sealed record Recommendation(
    TuningProfile Profile,
    string Because,
    double ExpectedFps,
    double ExpectedLow);

/// <summary>What one frame-rate target costs, or that it cannot be had.</summary>
/// <param name="Label">How the target reads to a person: "30 fps, never dropping".</param>
/// <param name="Stable">Cheapest-looking profile whose 1% low reaches the target, if any.</param>
/// <param name="OnAverage">
/// Cheapest-looking profile whose average reaches it. Always at least as good-looking as
/// <paramref name="Stable"/>, and often the only one of the two that exists.
/// </param>
public sealed record TargetOption(
    string Label,
    double Fps,
    Recommendation? Stable,
    Recommendation? OnAverage);

/// <summary>
/// Picks a profile for a machine, and answers the more useful question underneath it: what frame
/// rates this machine can actually hold, and what each one costs.
///
/// <para><b>What this is honestly built on.</b> Every measurement in this project comes from one
/// computer: an RTX 3050 with 8 GB, a Ryzen 5 4600G, 16 GB of system memory, at 1080p. So this
/// cannot be a model of how Cities: Skylines II performs on hardware in general — nobody has that
/// — and pretending otherwise would produce confident numbers with nothing underneath them.</para>
///
/// <para>What it does instead is place a machine relative to that one on the two axes the
/// measurements showed actually matter, and move up or down the ladder accordingly. Those axes
/// are not guesses: the benchmark showed the game GPU-bound at every tier down to
/// <c>super-potato</c>, at which point it becomes CPU-bound and further graphics cuts stop
/// returning anything. So GPU class decides which tier reaches a frame rate, and core count
/// decides where the ceiling on that frame rate sits.</para>
///
/// <para>The estimate it prints is the reference machine's measured number for that tier,
/// multiplied by a ratio. It is a projection, it says so wherever it appears, and it will be wrong
/// on hardware far from the reference. The mod's frame log exists so that anybody can replace it
/// with a measurement of their own in ninety seconds.</para>
/// </summary>
public static class ProfileAdvisor
{
    /// <summary>The machine every measured figure in this project came from.</summary>
    private const int ReferenceVramMb = 8042;
    private const int ReferenceThreads = 12;

    public const string ReferenceMachine =
        "RTX 3050 8GB, Ryzen 5 4600G (12 threads), 16 GB, 1080p";

    /// <summary>
    /// The frame rates worth asking for, low to high. Twenty is where a city builder stops being
    /// unpleasant; thirty is where it stops feeling slow; sixty is where it stops being a
    /// consideration. The gaps between them are wider than they look, which the table makes plain.
    /// </summary>
    public static readonly IReadOnlyList<(string Label, double Fps)> Targets =
    [
        ("20 fps", 20),
        ("25 fps", 25),
        ("30 fps", 30),
        ("45 fps", 45),
        ("60 fps", 60),
        ("90 fps", 90),
    ];

    /// <summary>
    /// Every target against this machine, for the question "what can I actually have".
    ///
    /// Both readings are reported for each, because they disagree and the disagreement is the
    /// point: sixty on average and sixty that never drops are different machines apart.
    /// </summary>
    public static IReadOnlyList<TargetOption> Survey(HardwareInfo? hardware)
    {
        return
        [
            .. Targets.Select(t => new TargetOption(
                t.Label,
                t.Fps,
                Find(hardware, t.Fps, Criterion.Stable),
                Find(hardware, t.Fps, Criterion.Average)))
        ];
    }

    /// <summary>The fastest tier there is, for "as many frames as this thing can produce".</summary>
    public static Recommendation Fastest(HardwareInfo? hardware)
    {
        var scale = hardware is null ? 1.0 : SpeedRelativeToReference(hardware, out _);
        var because = hardware is null ? Text.NothingKnownYet : Describe(hardware);

        var profile = Profiles.All.Where(p => p.Measured is not null).MaxBy(p => p.Measured!.Fps)!;
        return new Recommendation(profile, because,
            profile.Measured!.Fps * scale, profile.Measured.OnePercentLow * scale);
    }

    /// <summary>
    /// Recommend a tier. <paramref name="targetFps"/> null means "pick something sensible", taken
    /// as sixty on average — the goal this project was built around.
    /// </summary>
    public static Recommendation Recommend(
        HardwareInfo? hardware, double? targetFps = null, Criterion criterion = Criterion.Average)
    {
        if (hardware is null)
        {
            // No idea what this machine is: recommend the tier that hit sixty on the one machine
            // known, and say that is what happened rather than implying it was chosen.
            var fallback = Profiles.Handsome;
            return new Recommendation(fallback,
                Text.NothingKnownYet,
                fallback.Measured!.Fps, fallback.Measured.OnePercentLow);
        }

        var found = Find(hardware, targetFps ?? 60, criterion);
        if (found is not null) return found;

        var fastest = Fastest(hardware);
        return fastest with
        {
            Because = fastest.Because + Text.NothingHolds(targetFps ?? 60, fastest.ExpectedFps, fastest.ExpectedLow),
        };
    }

    /// <summary>
    /// The best-looking profile that meets a target, or null if none does.
    ///
    /// Walks the ladder from the top, which is ordered by looks rather than by speed, so the first
    /// hit is the prettiest one that gets there rather than merely the first one measured.
    /// </summary>
    private static Recommendation? Find(HardwareInfo? hardware, double target, Criterion criterion)
    {
        var scale = hardware is null ? 1.0 : SpeedRelativeToReference(hardware, out _);
        var because = hardware is null
            ? Text.NothingKnownYet
            : Describe(hardware);

        foreach (var profile in Profiles.All)
        {
            if (profile.Measured is not { } m) continue;

            var fps = m.Fps * scale;
            var low = m.OnePercentLow * scale;
            var met = criterion == Criterion.Stable ? low : fps;

            if (met >= target) return new Recommendation(profile, because, fps, low);
        }

        return null;
    }

    private static string Describe(HardwareInfo hardware) =>
        SpeedRelativeToReference(hardware, out var because) is var _ ? because : because;

    /// <summary>
    /// How fast this machine is likely to be against the reference, as a multiplier.
    ///
    /// Deliberately crude. Two inputs, both of which the game itself reports into its log, and
    /// both of which the measurements justify caring about. A finer model would be a fiction
    /// dressed as arithmetic.
    /// </summary>
    private static double SpeedRelativeToReference(HardwareInfo hardware, out string because)
    {
        // Integrated graphics are the one case worth detecting by name rather than by number,
        // because they report system memory as VRAM and would otherwise look generous.
        var gpu = hardware.Gpu ?? string.Empty;
        var integrated = gpu.Contains("Radeon Graphics", StringComparison.OrdinalIgnoreCase)
                         || gpu.Contains("Vega", StringComparison.OrdinalIgnoreCase)
                         || gpu.Contains("UHD", StringComparison.OrdinalIgnoreCase)
                         || gpu.Contains("Iris", StringComparison.OrdinalIgnoreCase)
                         || gpu.Contains("Van Gogh", StringComparison.OrdinalIgnoreCase);

        if (integrated)
        {
            because = Text.IntegratedGpu(gpu);
            return 0.35;
        }

        var vramRatio = hardware.HasVram
            ? Math.Clamp((double)hardware.VramMegabytes / ReferenceVramMb, 0.4, 2.5)
            : 1.0;

        // Cores matter far less than VRAM until they run out, and then they matter completely:
        // the measurements put the CPU wall at 11.2 ms per frame on twelve threads.
        var cpuRatio = Math.Clamp((double)hardware.CoreCount / ReferenceThreads, 0.5, 1.3);

        // The GPU decides most of it, which is what "GPU-bound at every tier" means in practice.
        var scale = Math.Round(vramRatio * 0.75 + cpuRatio * 0.25, 2);

        because = Text.ComparedToReference(gpu, hardware.VramMegabytes, hardware.CoreCount, scale);

        return scale;
    }
}
