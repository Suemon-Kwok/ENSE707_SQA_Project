// HealthStatusEvaluatorTests.cs
// Tests for the rule behind the dashboard's green / amber / red banner
// (Requirement 9; TC-P3-26 to TC-P3-30). The error-rate limit is 5%, so the
// values exactly on it (5.0) and just below it (4.9) are tested as boundaries.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using P3.Core.Health;
using P3.Core.Models;

namespace P3.Core.Tests
{
    [TestClass]
    public class HealthStatusEvaluatorTests
    {
        // A latest run with one endpoint at the given error rate.
        private static TestRun MakeRun(double errorRatePercent)
        {
            return new TestRun
            {
                BuildId = "build-2",
                Timestamp = DateTime.UtcNow,
                EndpointMetrics = new List<EndpointMetric>
                {
                    new EndpointMetric { EndpointName = "POST /checkout", ErrorRatePercent = errorRatePercent }
                }
            };
        }

        // A single regression result with the given severity.
        private static List<RegressionResult> OneResult(RegressionSeverity severity)
        {
            return new List<RegressionResult>
            {
                new RegressionResult { EndpointName = "POST /checkout", Severity = severity }
            };
        }

        // Positive: no regression and a low error rate means green.
        [TestMethod]
        public void Evaluate_ReturnsHealthy_WhenNoRegressionAndLowErrorRate()
        {
            var evaluator = new HealthStatusEvaluator();

            var result = evaluator.Evaluate(MakeRun(1.0), OneResult(RegressionSeverity.None));

            Assert.AreEqual(HealthStatus.Healthy, result.Status);
        }

        // Edge case: with no regression results at all (first run, no history),
        // the banner is still green but its message says why, so green is not
        // mistaken for "checked against history and fine".
        [TestMethod]
        public void Evaluate_ReturnsHealthy_AndExplainsMissingHistory_WhenNoResults()
        {
            var evaluator = new HealthStatusEvaluator();

            var result = evaluator.Evaluate(MakeRun(0.0), new List<RegressionResult>());

            Assert.AreEqual(HealthStatus.Healthy, result.Status);
            StringAssert.Contains(result.Message, "Not enough build history");
        }

        // A Major regression turns the banner red.
        [TestMethod]
        public void Evaluate_ReturnsCritical_WhenMajorRegressionPresent()
        {
            var evaluator = new HealthStatusEvaluator();

            var result = evaluator.Evaluate(MakeRun(0.0), OneResult(RegressionSeverity.Major));

            Assert.AreEqual(HealthStatus.Critical, result.Status);
            StringAssert.Contains(result.Message, "major regression");
        }

        // A Minor regression (and nothing worse) gives amber.
        [TestMethod]
        public void Evaluate_ReturnsWarning_WhenOnlyMinorRegressionPresent()
        {
            var evaluator = new HealthStatusEvaluator();

            var result = evaluator.Evaluate(MakeRun(0.0), OneResult(RegressionSeverity.Minor));

            Assert.AreEqual(HealthStatus.Warning, result.Status);
        }

        // Boundary: an error rate exactly at the 5.0% limit counts as failing.
        [TestMethod]
        public void Evaluate_ReturnsCritical_WhenErrorRateExactlyAtLimit()
        {
            var evaluator = new HealthStatusEvaluator();

            var result = evaluator.Evaluate(MakeRun(5.0), OneResult(RegressionSeverity.None));

            Assert.AreEqual(HealthStatus.Critical, result.Status);
            StringAssert.Contains(result.Message, "error rate");
        }

        // Boundary: 4.9% is just under the limit, so the build is still healthy.
        [TestMethod]
        public void Evaluate_ReturnsHealthy_WhenErrorRateJustBelowLimit()
        {
            var evaluator = new HealthStatusEvaluator();

            var result = evaluator.Evaluate(MakeRun(4.9), OneResult(RegressionSeverity.None));

            Assert.AreEqual(HealthStatus.Healthy, result.Status);
        }
    }
}
