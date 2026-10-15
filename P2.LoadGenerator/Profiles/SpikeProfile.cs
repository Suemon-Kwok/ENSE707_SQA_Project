namespace P2.LoadGenerator.Profiles;

/// <summary>
/// Holds a low baseline concurrency, then jumps to a short, sharp spike partway through
/// the run. Models flash-sale / viral-link style traffic bursts.
/// </summary>
public sealed class SpikeProfile : ILoadProfile
{
    public string Name => "Spike";
    public TimeSpan Duration { get; }
    public int BaselineConcurrency { get; }
    public int SpikeConcurrency { get; }
    public TimeSpan SpikeStart { get; }
    public TimeSpan SpikeDuration { get; }

    public SpikeProfile(
        TimeSpan duration,
        int baselineConcurrency,
        int spikeConcurrency,
        TimeSpan spikeStart,
        TimeSpan spikeDuration)
    {
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be positive.");
        if (baselineConcurrency < 0)
            throw new ArgumentOutOfRangeException(nameof(baselineConcurrency), "Baseline concurrency cannot be negative.");
        if (spikeConcurrency < baselineConcurrency)
            throw new ArgumentException("Spike concurrency must be >= baseline concurrency.", nameof(spikeConcurrency));
        if (spikeStart < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(spikeStart), "Spike start cannot be negative.");
        if (spikeDuration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(spikeDuration), "Spike duration cannot be negative.");

        Duration = duration;
        BaselineConcurrency = baselineConcurrency;
        SpikeConcurrency = spikeConcurrency;
        SpikeStart = spikeStart;
        SpikeDuration = spikeDuration;
    }

    public int GetTargetConcurrency(TimeSpan elapsed)
    {
        var spikeEnd = SpikeStart + SpikeDuration;
        var isDuringSpike = elapsed >= SpikeStart && elapsed < spikeEnd;
        return isDuringSpike ? SpikeConcurrency : BaselineConcurrency;
    }
}