namespace P2.LoadGenerator.Profiles;

/// <summary>
/// Linearly ramps concurrency from Start to Peak over the profile's duration.
/// Models gradually increasing traffic, e.g. the lead-up to a sale event.
/// </summary>
public sealed class SteadyRampProfile : ILoadProfile
{
    public string Name => "SteadyRamp";
    public TimeSpan Duration { get; }
    public int StartConcurrency { get; }
    public int PeakConcurrency { get; }

    public SteadyRampProfile(TimeSpan duration, int startConcurrency, int peakConcurrency)
    {
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be positive.");
        if (startConcurrency < 0)
            throw new ArgumentOutOfRangeException(nameof(startConcurrency), "Start concurrency cannot be negative.");
        if (peakConcurrency < startConcurrency)
            throw new ArgumentException("Peak concurrency must be >= start concurrency.", nameof(peakConcurrency));

        Duration = duration;
        StartConcurrency = startConcurrency;
        PeakConcurrency = peakConcurrency;
    }

    public int GetTargetConcurrency(TimeSpan elapsed)
    {
        if (elapsed >= Duration) return PeakConcurrency;

        var progress = elapsed.TotalMilliseconds / Duration.TotalMilliseconds;
        var value = StartConcurrency + (PeakConcurrency - StartConcurrency) * progress;
        // Floor, not Round: rounding let the ramp report peak concurrency up to half a
        // step before the duration had elapsed (e.g. 99.99 -> 100 at 9.999s of a 10s ramp).
        return (int)Math.Floor(value);
    }
}