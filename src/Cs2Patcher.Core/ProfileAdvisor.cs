namespace Cs2Patcher.Core;

/// <summary>Why a profile was recommended, in the words a person would use.</summary>
/// <param name="Profile">The tier to apply.</param>
/// <param name="Because">One sentence naming the thing about this machine that decided it.</param>
/// <param name="ExpectedFps">
/// What it measured on the reference machine, scaled by how this one compares. A projection, and
/// the wording everywhere it surfaces says so.
/// </param>
public sealed record Recommendation(TuningProfile Profile, string Because, double ExpectedFps);

/// <summary>
/// Picks a profile for a machine.
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

    /// <summary>
    /// Recommend a tier. <paramref name="targetFps"/> is what the player asked for; null means
    /// "pick something sensible", which is taken as sixty.
    /// </summary>
    public static Recommendation Recommend(HardwareInfo? hardware, double? targetFps = null)
    {
        var target = targetFps ?? 60;
        var ladder = Profiles.All.Where(p => p.Measured is not null).ToList();

        // No idea what this machine is: recommend the tier that hit sixty on the one machine
        // known, and say that is what happened rather than implying it was chosen.
        if (hardware is null)
        {
            var fallback = Profiles.Handsome;
            return new Recommendation(fallback,
                "Nothing is known about this machine yet — run the game once so it writes a log. "
                + "This is the tier that reached sixty on the machine this tool was built on.",
                fallback.Measured!.Fps);
        }

        var scale = SpeedRelativeToReference(hardware, out var because);

        // Walk from the best-looking tier down and stop at the first that clears the target.
        // Down the ladder is always faster and always uglier, so the first hit is the one to use.
        foreach (var profile in ladder)
        {
            var projected = profile.Measured!.Fps * scale;
            if (projected >= target)
                return new Recommendation(profile, because, projected);
        }

        // Nothing clears the target, so hand back the fastest there is rather than the last one
        // in the list — the ladder is ordered by how good each tier looks, not by how fast it is,
        // and the two do not agree at the bottom where the ugliest tier measured slower than the
        // one above it.
        var fastest = ladder.MaxBy(p => p.Measured!.Fps)!;
        var best = fastest.Measured!.Fps * scale;

        return new Recommendation(fastest,
            because + $" Nothing here reaches {target:N0} fps on this machine; the fastest tier is "
                    + $"projected at {best:N0}.",
            best);
    }

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
            because = $"{gpu} is integrated graphics, which shares memory with the CPU and has no "
                      + "upscaler to fall back on. Estimated at about a third of the machine this "
                      + "tool was measured on.";
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

        because = $"{gpu} with {hardware.VramMegabytes} MB and {hardware.CoreCount} threads, "
                  + $"against the 8 GB and 12 threads everything here was measured on"
                  + (Math.Abs(scale - 1) < 0.05 ? " — near enough the same machine." : $" — about {scale:N2}x.");

        return scale;
    }
}
