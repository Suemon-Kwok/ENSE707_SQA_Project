// ThresholdRegressionDetectorTests.cs
// Covers the core build-over-build comparison logic: correct severity
// classification, use of a ROLLING multi-build baseline (not just the single
// previous build — this is the exact scope-narrowing mistake flagged in the
// mid-project report's AI-assisted development section), and the
// no-prior-history edge case that guards against a division-by-zero.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using P3.Core.Models;
using P3.Core.Regression;

namespace P3.Core.Tests
{
    [TestClass]
    public class ThresholdRegressionDetectorTests
    {
        // Small helper so every test doesn't have to hand-build a TestRun
        // from scratch — keeps each test focused on what it's actually
        // asserting rather than object-construction boilerplate.
        private static TestRun MakeRun(string buildId, DateTime timestamp, string endpointName, double p95)
        {
            return new TestRun
            {
                BuildId = buildId,
                Timestamp = timestamp,
                LoadProfile = "SteadyRamp",
                EndpointMetrics = new List<EndpointMetric>
                {
                    new EndpointMetric { EndpointName = endpointName, LatencyP95Ms = p95 }
                }
            };
        }

        [TestMethod]
        public void DetectRegressions_FlagsMajor_WhenP95FarExceedsBaseline()
        {
            // Baseline of 3 prior builds averaging ~210ms; current build at
            // 410ms is roughly 95% slower — well past the 25% major threshold.
            var detector = new ThresholdRegressionDetector();
            var baseTime = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc);

            var history = new List<TestRun>
            {
                MakeRun("build-101", baseTime, "POST /checkout", 210),
                MakeRun("build-102", baseTime.AddDays(2), "POST /checkout", 205),
                MakeRun("build-103", baseTime.AddDays(4), "POST /checkout", 215),
            };
            var currentRun = MakeRun("build-104", baseTime.AddDays(7), "POST /checkout", 410);

            var results = detector.DetectRegressions(currentRun, history);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(RegressionSeverity.Major, results[0].Severity);
            Assert.IsTrue(results[0].PercentChange > 25.0,
                "A build this much slower than baseline should exceed the major threshold.");
        }

        [TestMethod]
        public void DetectRegressions_ReturnsNone_OnStableBuilds()
        {
            // Four builds that only fluctuate by a couple of ms — normal
            // run-to-run noise, not a real regression.
            var detector = new ThresholdRegressionDetector();
            var baseTime = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc);

            var history = new List<TestRun>
            {
                MakeRun("build-201", baseTime, "GET /cart", 95),
                MakeRun("build-202", baseTime.AddDays(1), "GET /cart", 92),
                MakeRun("build-203", baseTime.AddDays(2), "GET /cart", 94),
            };
            var currentRun = MakeRun("build-204", baseTime.AddDays(3), "GET /cart", 96);

            var results = detector.DetectRegressions(currentRun, history);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(RegressionSeverity.None, results[0].Severity);
        }

        [TestMethod]
        public void DetectRegressions_ReturnsMinor_WhenChangeIsBetweenThresholds()
        {
            // Default thresholds: Minor >= 10%, Major >= 25%. A 15% slowdown
            // should land as Minor, not None and not Major.
            var detector = new ThresholdRegressionDetector();
            var baseTime = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc);

            var history = new List<TestRun>
            {
                MakeRun("build-301", baseTime, "GET /cart", 100),
            };
            var currentRun = MakeRun("build-302", baseTime.AddDays(1), "GET /cart", 115); // +15%

            var results = detector.DetectRegressions(currentRun, history);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(RegressionSeverity.Minor, results[0].Severity);
        }

        [TestMethod]
        public void DetectRegressions_UsesRollingWindowAverage_NotJustPreviousBuild()
        {
            // This locks in the behaviour the mid-report specifically flagged
            // as a Copilot mistake: comparing against only the immediately
            // previous build rather than averaging the whole baseline window.
            // Baseline here is (100 + 200 + 100) / 3 = ~133.3ms. If the
            // detector wrongly used only the single most recent prior build
            // (100ms), a current value of 140ms would be classified as a
            // Major regression (+40%) instead of the correct ~5% (None).
            var detector = new ThresholdRegressionDetector();
            var baseTime = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc);

            var history = new List<TestRun>
            {
                MakeRun("build-401", baseTime, "GET /cart", 100),
                MakeRun("build-402", baseTime.AddDays(1), "GET /cart", 200),
                MakeRun("build-403", baseTime.AddDays(2), "GET /cart", 100), // most recent prior
            };
            var currentRun = MakeRun("build-404", baseTime.AddDays(3), "GET /cart", 140);

            var results = detector.DetectRegressions(currentRun, history);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(RegressionSeverity.None, results[0].Severity,
                "Using the 3-build rolling average (~133ms) as baseline, +140ms is only a ~5% change.");
        }

        [TestMethod]
        public void DetectRegressions_SkipsEndpoint_WhenNoPriorHistoryExists()
        {
            // A brand-new endpoint with no prior runs has nothing to compare
            // against. This must not throw (division-by-zero) and must not
            // fabricate a 0%/undefined regression result — it should simply
            // be omitted from the results list.
            var detector = new ThresholdRegressionDetector();
            var currentRun = MakeRun("build-501", DateTime.UtcNow, "POST /new-endpoint", 150);

            var results = detector.DetectRegressions(currentRun, new List<TestRun>());

            Assert.AreEqual(0, results.Count);
        }
    }
}
