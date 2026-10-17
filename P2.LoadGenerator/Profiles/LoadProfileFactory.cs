namespace P2.LoadGenerator.Profiles;

/// <summary>
/// Builds a profile from a name plus the two numbers a user actually picks (duration and
/// peak concurrency). Lives in the engine rather than the GUI so the mapping is covered by
/// the cross-platform test project instead of only being exercised by clicking the form.
/// </summary>
public static class LoadProfileFactory
{
    public static readonly IReadOnlyList<string> ProfileNames = new[] { "SteadyRamp", "Spike", "Soak" };

    public static ILoadProfile Create(string profileName, TimeSpan duration, int peakConcurrency)
    {
        if (peakConcurrency < 1)
            throw new ArgumentOutOfRangeException(nameof(peakConcurrency), "Peak concurrency must be at least 1.");

        return profileName switch
        {
            "SteadyRamp" => new SteadyRampProfile(duration, startConcurrency: 1, peakConcurrency: peakConcurrency),
            // Spike: 10% baseline, spiking to peak for the middle 20% of the run.
            "Spike" => new SpikeProfile(
                duration: duration,
                baselineConcurrency: Math.Max(1, peakConcurrency / 10),
                spikeConcurrency: peakConcurrency,
                spikeStart: TimeSpan.FromTicks((long)(duration.Ticks * 0.4)),
                spikeDuration: TimeSpan.FromTicks((long)(duration.Ticks * 0.2))),
            // Soak: sustained moderate load at a quarter of peak.
            "Soak" => new SoakProfile(duration, concurrency: Math.Max(1, peakConcurrency / 4)),
            _ => throw new ArgumentException($"Unknown load profile '{profileName}'.", nameof(profileName))
        };
    }
}
