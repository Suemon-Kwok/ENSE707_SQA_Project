// HealthStatusEvaluator.cs
// Decides whether the latest build is Healthy (green), Warning (amber) or
// Critical (red) from two inputs: the regression detector's results and the
// latest run's per-endpoint error rates. This is the logic behind the
// dashboard's colour-coded banner (Requirement 9).
//
// Rules, in priority order:
//   Critical - any endpoint has a Major regression, OR any endpoint's error
//              rate is at or above the limit (default 5%, the SLA figure the
//              mid-project report proposed).
//   Warning  - no Critical condition, but at least one Minor regression.
//   Healthy  - none of the above.

using System;
using System.Collections.Generic;
using System.Linq;
using P3.Core.Models;

namespace P3.Core.Health
{
    public class HealthStatusEvaluator
    {
        // Error rate (in percent) at or above which an endpoint counts as failing.
        private readonly double _errorRateThresholdPercent;

        public HealthStatusEvaluator(double errorRateThresholdPercent = 5.0)
        {
            _errorRateThresholdPercent = errorRateThresholdPercent;
        }

        public HealthStatusResult Evaluate(TestRun latestRun, IReadOnlyList<RegressionResult> regressionResults)
        {
            if (latestRun == null)
            {
                throw new ArgumentNullException(nameof(latestRun));
            }

            if (regressionResults == null)
            {
                throw new ArgumentNullException(nameof(regressionResults));
            }

            var majorCount = regressionResults.Count(r => r.Severity == RegressionSeverity.Major);
            var minorCount = regressionResults.Count(r => r.Severity == RegressionSeverity.Minor);
            var highErrorCount = latestRun.EndpointMetrics.Count(m => m.ErrorRatePercent >= _errorRateThresholdPercent);

            if (majorCount > 0 || highErrorCount > 0)
            {
                var reasons = new List<string>();

                if (majorCount > 0)
                {
                    reasons.Add($"{majorCount} endpoint(s) with a major regression");
                }

                if (highErrorCount > 0)
                {
                    reasons.Add($"{highErrorCount} endpoint(s) with an error rate of {_errorRateThresholdPercent:F1}% or more");
                }

                return new HealthStatusResult
                {
                    Status = HealthStatus.Critical,
                    Message = "Action needed: " + string.Join(" and ", reasons) + "."
                };
            }

            if (minorCount > 0)
            {
                return new HealthStatusResult
                {
                    Status = HealthStatus.Warning,
                    Message = $"Watch: {minorCount} endpoint(s) slower than baseline by a minor margin."
                };
            }

            // Nothing wrong. If there were no regression results at all, say
            // why, so a green banner is not mistaken for "checked and fine".
            var healthyMessage = regressionResults.Count == 0
                ? "No error-rate problems. Not enough build history yet to check for regressions."
                : "All endpoints are within baseline and below the error-rate limit.";

            return new HealthStatusResult
            {
                Status = HealthStatus.Healthy,
                Message = healthyMessage
            };
        }
    }
}
