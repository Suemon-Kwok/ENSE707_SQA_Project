// JsonTestRunStoreTests.cs
// Covers the file-backed store's contract: new runs persist and come back
// out, GetAllRuns is ordered oldest-first (required by the trend charts and
// regression detector, which both assume chronological order), and
// GetRunsForBuild filters correctly. Each test uses its own temp file so
// tests can't interfere with each other or with the real SampleData/runs.json.

using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using P3.Core.Models;
using P3.Core.Persistence;

namespace P3.Core.Tests
{
    [TestClass]
    public class JsonTestRunStoreTests
    {
        private string _tempFilePath = string.Empty;

        // Runs before each test: picks a fresh, unique temp file path so
        // tests never read/write each other's data.
        [TestInitialize]
        public void SetUp()
        {
            _tempFilePath = Path.Combine(Path.GetTempPath(), $"p3-test-runs-{Guid.NewGuid()}.json");
        }

        // Runs after each test: deletes the temp file so these tests don't
        // leave junk behind on disk.
        [TestCleanup]
        public void TearDown()
        {
            if (File.Exists(_tempFilePath))
            {
                File.Delete(_tempFilePath);
            }
        }

        [TestMethod]
        public void Constructor_CreatesEmptyArrayFile_WhenFileDoesNotExistYet()
        {
            var store = new JsonTestRunStore(_tempFilePath);

            Assert.IsTrue(File.Exists(_tempFilePath));
            Assert.AreEqual(0, store.GetAllRuns().Count);
        }

        [TestMethod]
        public void SaveRun_PersistsRun_AndAppearsInGetAllRuns()
        {
            var store = new JsonTestRunStore(_tempFilePath);
            var run = new TestRun { BuildId = "build-1", Timestamp = DateTime.UtcNow };

            store.SaveRun(run);
            var allRuns = store.GetAllRuns();

            Assert.AreEqual(1, allRuns.Count);
            Assert.AreEqual("build-1", allRuns[0].BuildId);
        }

        [TestMethod]
        public void GetAllRuns_ReturnsRunsOrderedOldestFirst()
        {
            var store = new JsonTestRunStore(_tempFilePath);
            var baseTime = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc);

            // Deliberately saved out of chronological order to prove the
            // store re-sorts on read rather than relying on insertion order.
            store.SaveRun(new TestRun { BuildId = "build-newest", Timestamp = baseTime.AddDays(2) });
            store.SaveRun(new TestRun { BuildId = "build-oldest", Timestamp = baseTime });
            store.SaveRun(new TestRun { BuildId = "build-middle", Timestamp = baseTime.AddDays(1) });

            var allRuns = store.GetAllRuns();

            Assert.AreEqual(3, allRuns.Count);
            Assert.AreEqual("build-oldest", allRuns[0].BuildId);
            Assert.AreEqual("build-middle", allRuns[1].BuildId);
            Assert.AreEqual("build-newest", allRuns[2].BuildId);
        }

        [TestMethod]
        public void GetRunsForBuild_ReturnsOnlyMatchingBuildId_CaseInsensitive()
        {
            var store = new JsonTestRunStore(_tempFilePath);

            store.SaveRun(new TestRun { BuildId = "Build-104", Timestamp = DateTime.UtcNow });
            store.SaveRun(new TestRun { BuildId = "build-105", Timestamp = DateTime.UtcNow });

            // Deliberately different casing from what was saved, to confirm
            // the OrdinalIgnoreCase comparison documented in JsonTestRunStore.
            var matches = store.GetRunsForBuild("build-104");

            Assert.AreEqual(1, matches.Count);
            Assert.AreEqual("Build-104", matches[0].BuildId);
        }
    }
}
