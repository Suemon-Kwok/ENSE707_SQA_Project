// MarkdownReportExporterTests.cs
// Covers the three sections MarkdownReportExporter writes: the breach log
// only lists Major-severity results, the anomaly log only lists runs where
// AnomalyDetected is explicitly true (not null), and both sections print a
// clear "nothing to show" message rather than an empty table when there's
// no data yet.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using P3.Core.Models;
using P3.Core.Reporting;

namespace P3.Core.Tests
{
    [TestClass]
    public class MarkdownReportExporterTests
    {
        private string _tempFilePath = string.Empty;

        [TestInitialize]
        public void SetUp()
        {
            _tempFilePath = Path.Combine(Path.GetTempPath(), $"p3-test-report-{Guid.NewGuid()}.md");
        }

        [TestCleanup]
        public void TearDown()
        {
            if (File.Exists(_tempFilePath))
            {
                File.Delete(_tempFilePath);
            }
        }

        [TestMethod]
        public void Export_WritesBreachLogRow_ForMajorRegressionOnly()
        {
            var exporter = new MarkdownReportExporter();
            var runs = new List<TestRun>
            {
                new TestRun { BuildId = "build-104", Timestamp = DateTime.UtcNow }
            };
            var regressionResults = new List<RegressionResult>
            {
                new RegressionResult { EndpointName = "POST /checkout", Severity = RegressionSeverity.Major, PercentChange = 95.2 },
                new RegressionResult { EndpointName = "GET /cart", Severity = RegressionSeverity.None, PercentChange = 2.5 }
            };

            exporter.Export(runs, regressionResults, _tempFilePath);
            var content = File.ReadAllText(_tempFilePath);

            StringAssert.Contains(content, "POST /checkout");
            // The None-severity /cart result must NOT appear in the breach
            // log table — only Major results belong there.
            var breachSection = content.Substring(content.IndexOf("## Breach Log", StringComparison.Ordinal));
            var nextSectionIndex = breachSection.IndexOf("## Anomaly Log", StringComparison.Ordinal);
            var breachSectionOnly = breachSection.Substring(0, nextSectionIndex);
            StringAssert.DoesNotMatch(breachSectionOnly, new System.Text.RegularExpressions.Regex("GET /cart"));
        }

        [TestMethod]
        public void Export_WritesNoRegressionsMessage_WhenNoMajorResultsExist()
        {
            var exporter = new MarkdownReportExporter();
            var runs = new List<TestRun>();
            var regressionResults = new List<RegressionResult>();

            exporter.Export(runs, regressionResults, _tempFilePath);
            var content = File.ReadAllText(_tempFilePath);

            StringAssert.Contains(content, "No major regressions detected.");
        }

        [TestMethod]
        public void Export_WritesAnomalyLogRow_OnlyForRunsExplicitlyFlaggedTrue()
        {
            var exporter = new MarkdownReportExporter();
            var runs = new List<TestRun>
            {
                new TestRun { BuildId = "build-101", Timestamp = DateTime.UtcNow, AnomalyDetected = false },
                new TestRun { BuildId = "build-102", Timestamp = DateTime.UtcNow, AnomalyDetected = null }, // not yet evaluated
                new TestRun { BuildId = "build-103", Timestamp = DateTime.UtcNow, AnomalyDetected = true }
            };

            exporter.Export(runs, new List<RegressionResult>(), _tempFilePath);
            var content = File.ReadAllText(_tempFilePath);

            StringAssert.Contains(content, "build-103");
            StringAssert.DoesNotMatch(content, new System.Text.RegularExpressions.Regex(@"\| build-101 \|"));
            StringAssert.DoesNotMatch(content, new System.Text.RegularExpressions.Regex(@"\| build-102 \|"));
        }
    }
}
