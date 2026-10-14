// HealthStatus.cs
// The three states the dashboard's status banner can show, plus the small
// result object the evaluator returns. Kept in P3.Core (not in the dashboard page)
// so the rule that decides "green or red" has no UI dependency and can be
// unit-tested directly, which supports Requirement 8's separation of logic
// from the user interface.

namespace P3.Core.Health
{
    public enum HealthStatus
    {
        // Latest build is within baseline and below the error-rate limit.
        Healthy,

        // At least one endpoint is slower than baseline by a Minor margin.
        Warning,

        // A Major regression, or an endpoint whose error rate is at or above
        // the limit, which is the closest this tool gets to an "outage".
        Critical
    }

    public class HealthStatusResult
    {
        // Which colour state the banner should show.
        public HealthStatus Status { get; set; }

        // One plain-English sentence for the banner, so colour is never the
        // only signal (helps colour-blind users).
        public string Message { get; set; } = string.Empty;
    }
}
