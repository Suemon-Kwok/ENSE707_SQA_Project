using P2.LoadGenerator.Catalog;
using P2.LoadGenerator.Configuration;
using P2.LoadGenerator.Endpoints;
using P2.LoadGenerator.Execution;
using P2.LoadGenerator.Export;
using P2.LoadGenerator.Metrics;
using P2.LoadGenerator.Profiles;

namespace P2.LoadGenerator.Tests;

[TestClass]
public class Tests
{
    // Verifies ErrorRatePercent handles zero total requests without a
    // divide-by-zero. Should return 0, not throw or return NaN.
    [TestMethod]
    public void ErrorRatePercent_ZeroTotalRequests_ReturnsZero()
    {
        var metric = new EndpointLoadMetric
        {
            EndpointName = "/api/test",
            TotalRequests = 0,
            SuccessCount = 0,
            FailureCount = 0,
            AverageLatencyMs = 0,
            P50LatencyMs = 0,
            P95LatencyMs = 0,
            P99LatencyMs = 0,
            MaxLatencyMs = 0,
            ThroughputRps = 0
        };

        Assert.AreEqual(0, metric.ErrorRatePercent);
    }

    // Confirms a run with zero failures reports exactly 0% error rate.
    [TestMethod]
    public void ErrorRatePercent_AllSuccess_ReturnsZero()
    {
        var metric = new EndpointLoadMetric
        {
            EndpointName = "/api/test",
            TotalRequests = 100,
            SuccessCount = 100,
            FailureCount = 0,
            AverageLatencyMs = 40,
            P50LatencyMs = 40,
            P95LatencyMs = 60,
            P99LatencyMs = 70,
            MaxLatencyMs = 75,
            ThroughputRps = 5
        };

        Assert.AreEqual(0, metric.ErrorRatePercent);
    }

    // Checks the percentage math itself is correct and rounded properly
    // for a realistic partial-failure scenario (5 failures out of 215).
    [TestMethod]
    public void ErrorRatePercent_PartialFailures_ReturnsCorrectPercentage()
    {
        var metric = new EndpointLoadMetric
        {
            EndpointName = "/api/test",
            TotalRequests = 215,
            SuccessCount = 210,
            FailureCount = 5,
            AverageLatencyMs = 40.97,
            P50LatencyMs = 39.89,
            P95LatencyMs = 67.49,
            P99LatencyMs = 75.22,
            MaxLatencyMs = 80,
            ThroughputRps = 10.5
        };

        // 5 / 215 * 100 = 2.33 (rounded)
        Assert.AreEqual(2.33, metric.ErrorRatePercent);
    }

    // Confirms a fully-failed run reports 100%, not an off-by-something
    // value from the rounding logic.
    [TestMethod]
    public void ErrorRatePercent_AllFailures_ReturnsOneHundred()
    {
        var metric = new EndpointLoadMetric
        {
            EndpointName = "/api/test",
            TotalRequests = 50,
            SuccessCount = 0,
            FailureCount = 50,
            AverageLatencyMs = 5000,
            P50LatencyMs = 5000,
            P95LatencyMs = 5000,
            P99LatencyMs = 5000,
            MaxLatencyMs = 5000,
            ThroughputRps = 0
        };

        Assert.AreEqual(100, metric.ErrorRatePercent);
    }

    // Verifies the factory is deterministic: two instances built with the
    // same seed should generate the same customer and parameters, since
    // reproducible test data matters for comparing runs.
    [TestMethod]
    public void RandomCustomerRequestFactory_SameSeed_ProducesSameCustomerAndParameters()
    {
        var factoryA = new RandomCustomerRequestFactory(seed: 42);
        var factoryB = new RandomCustomerRequestFactory(seed: 42);

        var requestA = factoryA.Create();
        var requestB = factoryB.Create();

        // RequestId uses Guid.NewGuid() regardless of seed, so it's excluded here.
        Assert.AreEqual(requestA.CustomerId, requestB.CustomerId);
        CollectionAssert.AreEqual(requestA.Parameters, requestB.Parameters);
    }

    // Sanity check that different seeds actually produce different output,
    // confirming the "randomness" isn't secretly hardcoded or ignored.
    [TestMethod]
    public void RandomCustomerRequestFactory_DifferentSeeds_LikelyProduceDifferentCustomers()
    {
        var factoryA = new RandomCustomerRequestFactory(seed: 1);
        var factoryB = new RandomCustomerRequestFactory(seed: 2);

        var requestA = factoryA.Create();
        var requestB = factoryB.Create();

        Assert.AreNotEqual(requestA.CustomerId, requestB.CustomerId);
    }

    // Proves the generator survives a run where most requests fail, rather
    // than crashing, and that the failures actually get recorded in the
    // resulting metrics instead of being silently dropped.
    [TestMethod]
    public async Task RunAsync_HighFailureRate_CompletesWithoutThrowing()
    {
        var profile = new SpikeProfile(
            duration: TimeSpan.FromSeconds(2),
            baselineConcurrency: 2,
            spikeConcurrency: 5,
            spikeStart: TimeSpan.FromSeconds(1),
            spikeDuration: TimeSpan.FromMilliseconds(500));

        var config = new LoadTestConfiguration
        {
            TargetBaseUrl = "http://localhost:5000",
            CustomerEndpointPath = "/api/customers/orders",
            Profile = profile,
            MaxConcurrency = 10,
            BatchSize = 2,
            RequestTimeout = TimeSpan.FromSeconds(1),
            RunLabel = "reliability-test-high-failure"
        };

        ICustomerEndpointClient endpointClient = new SimulatedCustomerEndpointClient(
            baseLatencyMs: 10,
            latencyJitterMs: 5,
            errorRate: 0.9,
            seed: 1);

        var requestFactory = new RandomCustomerRequestFactory(seed: 1);
        var generator = new Execution.LoadGenerator(config, endpointClient, requestFactory);

        var result = await generator.RunAsync();

        Assert.IsNotNull(result);
        Assert.HasCount(1, result.EndpointMetrics);
        Assert.IsGreaterThan(0, result.EndpointMetrics[0].FailureCount,
            "Expected a high-error-rate run to record at least one failure.");
    }

    // Confirms the printed console output actually contains the endpoint
    // name and table header for a normal, one-endpoint result.
    // [DoNotParallelize]: this test redirects the shared, process-wide
    // Console.Out, so it can't safely run at the same time as the other
    // console test below — without this, MSTest's default parallel
    // execution causes the two tests' output to overwrite each other.
    [TestMethod]
    [DoNotParallelize]
    public async Task ExportAsync_OneEndpoint_PrintsEndpointNameAndHeader()
    {
        var originalOut = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            var result = new LoadTestRunResult
            {
                RunId = Guid.NewGuid(),
                RunLabel = "printer-test",
                ProfileName = "Spike",
                StartedAtUtc = DateTimeOffset.UtcNow,
                CompletedAtUtc = DateTimeOffset.UtcNow.AddSeconds(20),
                EndpointMetrics = new[]
                {
                    new EndpointLoadMetric
                    {
                        EndpointName = "/api/customers/orders",
                        TotalRequests = 215,
                        SuccessCount = 210,
                        FailureCount = 5,
                        AverageLatencyMs = 40.97,
                        P50LatencyMs = 39.89,
                        P95LatencyMs = 67.49,
                        P99LatencyMs = 75.22,
                        MaxLatencyMs = 80,
                        ThroughputRps = 10.5
                    }
                }
            };

            var printer = new ConsoleResultPrinter();
            await printer.ExportAsync(result);

            var output = writer.ToString();
            StringAssert.Contains(output, "Endpoint");
            StringAssert.Contains(output, "/api/customers/orders");
            StringAssert.Contains(output, "Rps");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    // Confirms the printed row includes the throughput figure itself, not just the header —
    // this printer previously computed ThroughputRps but never rendered it.
    [TestMethod]
    [DoNotParallelize]
    public async Task ExportAsync_OneEndpoint_PrintsThroughputValue()
    {
        var originalOut = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            var result = new LoadTestRunResult
            {
                RunId = Guid.NewGuid(),
                RunLabel = "throughput-print-test",
                ProfileName = "Spike",
                StartedAtUtc = DateTimeOffset.UtcNow,
                CompletedAtUtc = DateTimeOffset.UtcNow.AddSeconds(20),
                EndpointMetrics = new[]
                {
                    new EndpointLoadMetric
                    {
                        EndpointName = "/api/customers/orders",
                        TotalRequests = 215,
                        SuccessCount = 210,
                        FailureCount = 5,
                        AverageLatencyMs = 40.97,
                        P50LatencyMs = 39.89,
                        P95LatencyMs = 67.49,
                        P99LatencyMs = 75.22,
                        MaxLatencyMs = 80,
                        ThroughputRps = 10.5
                    }
                }
            };

            var printer = new ConsoleResultPrinter();
            await printer.ExportAsync(result);

            StringAssert.Contains(writer.ToString(), "10.50");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    // Confirms the printer doesn't throw on a run with no endpoint metrics
    // at all, and still prints the run-level info (label, profile, etc).
    // [DoNotParallelize]: see comment on the test above — same shared
    // Console.Out issue applies here.
    [TestMethod]
    [DoNotParallelize]
    public async Task ExportAsync_NoEndpoints_PrintsHeaderWithoutThrowing()
    {
        var originalOut = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            var result = new LoadTestRunResult
            {
                RunId = Guid.NewGuid(),
                RunLabel = "printer-test-empty",
                ProfileName = "Spike",
                StartedAtUtc = DateTimeOffset.UtcNow,
                CompletedAtUtc = DateTimeOffset.UtcNow.AddSeconds(5),
                EndpointMetrics = Array.Empty<EndpointLoadMetric>()
            };

            var printer = new ConsoleResultPrinter();
            await printer.ExportAsync(result);

            var output = writer.ToString();
            StringAssert.Contains(output, "printer-test-empty");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    // --- ThroughputRps (RunMetricsAggregator) ---------------------------------------

    // 50 successes over a 5-second run should report 10 requests/sec, not raw request count.
    [TestMethod]
    public void Aggregate_FiftySuccessesOverFiveSeconds_ReportsTenRps()
    {
        var results = Enumerable.Range(0, 50)
            .Select(_ => new RequestResult
            {
                RequestId = Guid.NewGuid(),
                StartedAtUtc = DateTimeOffset.UtcNow,
                LatencyMs = 20,
                Success = true
            })
            .ToList();

        var aggregator = new RunMetricsAggregator();
        var metric = aggregator.Aggregate("/api/test", results, TimeSpan.FromSeconds(5));

        Assert.AreEqual(10.0, metric.ThroughputRps);
    }

    // Failures shouldn't count toward throughput — only completed successful requests
    // represent capacity the endpoint actually sustained.
    [TestMethod]
    public void Aggregate_MixedSuccessAndFailure_ThroughputCountsSuccessesOnly()
    {
        var results = new List<RequestResult>
        {
            new() { RequestId = Guid.NewGuid(), StartedAtUtc = DateTimeOffset.UtcNow, LatencyMs = 20, Success = true },
            new() { RequestId = Guid.NewGuid(), StartedAtUtc = DateTimeOffset.UtcNow, LatencyMs = 20, Success = true },
            new() { RequestId = Guid.NewGuid(), StartedAtUtc = DateTimeOffset.UtcNow, LatencyMs = 20, Success = false }
        };

        var aggregator = new RunMetricsAggregator();
        var metric = aggregator.Aggregate("/api/test", results, TimeSpan.FromSeconds(2));

        Assert.AreEqual(1.0, metric.ThroughputRps);
    }

    // Guards the same divide-by-zero shape ErrorRatePercent already guards against, this
    // time for a zero-duration run rather than a zero-request one.
    [TestMethod]
    public void Aggregate_ZeroDuration_ReturnsZeroThroughputInsteadOfThrowing()
    {
        var results = new List<RequestResult>
        {
            new() { RequestId = Guid.NewGuid(), StartedAtUtc = DateTimeOffset.UtcNow, LatencyMs = 20, Success = true }
        };

        var aggregator = new RunMetricsAggregator();
        var metric = aggregator.Aggregate("/api/test", results, TimeSpan.Zero);

        Assert.AreEqual(0.0, metric.ThroughputRps);
    }

    // --- Boundary tests on concurrency, per the rubric's named boundary pairs -------

    // SteadyRampProfile at exactly its configured peak (100) should return that value,
    // not overshoot — the 99-vs-100 boundary the rubric calls out explicitly.
    [TestMethod]
    public void SteadyRampProfile_AtPeakBoundary_ReturnsExactlyPeakConcurrency()
    {
        var profile = new SteadyRampProfile(duration: TimeSpan.FromSeconds(10), startConcurrency: 1, peakConcurrency: 100);

        Assert.AreEqual(100, profile.GetTargetConcurrency(TimeSpan.FromSeconds(10)));
    }

    // One tick before the ramp completes, concurrency must still be below peak (99, not 100),
    // proving the ramp is linear right up to the boundary rather than jumping early.
    [TestMethod]
    public void SteadyRampProfile_OneMillisecondBeforePeak_IsBelowPeakConcurrency()
    {
        var profile = new SteadyRampProfile(duration: TimeSpan.FromSeconds(10), startConcurrency: 1, peakConcurrency: 100);

        var justBefore = profile.GetTargetConcurrency(TimeSpan.FromMilliseconds(9999));

        Assert.IsLessThan(100, justBefore);
    }

    // LoadGenerator must never exceed MaxConcurrency even when the profile asks for more —
    // the 600-vs-601 style boundary from the rubric, expressed as the hard cap this class
    // enforces. If SemaphoreSlim(_config.MaxConcurrency) ever deadlocked or threw on a
    // batch larger than the cap, this test would fail/hang rather than silently pass.
    [TestMethod]
    public async Task RunAsync_BatchSizeExceedsMaxConcurrency_CompletesWithoutDeadlockOrThrow()
    {
        var config = new LoadTestConfiguration
        {
            TargetBaseUrl = "http://localhost:5000",
            CustomerEndpointPath = "/api/customers/orders",
            Profile = new SoakProfile(TimeSpan.FromMilliseconds(600), concurrency: 601),
            MaxConcurrency = 600,
            BatchSize = 601,
            RequestTimeout = TimeSpan.FromSeconds(1),
            RunLabel = "boundary-concurrency-test"
        };

        ICustomerEndpointClient endpointClient = new SimulatedCustomerEndpointClient(baseLatencyMs: 1, latencyJitterMs: 0, errorRate: 0, seed: 1);
        var generator = new Execution.LoadGenerator(config, endpointClient, new RandomCustomerRequestFactory(seed: 1));

        var result = await generator.RunAsync();

        Assert.IsNotNull(result);
    }

    // --- CatalogCustomerRequestFactory (P1 data hookup) ------------------------------

    // Requests generated from the catalogue must reference a real product ID from that
    // catalogue, not an arbitrary/fake SKU string.
    [TestMethod]
    public void CatalogCustomerRequestFactory_Create_UsesRealCatalogProductId()
    {
        var catalog = new List<CatalogProduct>
        {
            new() { Id = 7, Name = "Test Dice Set", Category = "Dice", Quantity = 20, Price = 14.99m }
        };
        var factory = new CatalogCustomerRequestFactory(catalog, seed: 1);

        var request = factory.Create();

        Assert.AreEqual("7", request.Parameters["productId"]);
        Assert.AreEqual("Dice", request.Parameters["category"]);
    }

    // A product with only 2 in stock should never generate a requested quantity above 2 —
    // proves the factory respects the catalogue's stock levels rather than always asking 1-4.
    [TestMethod]
    public void CatalogCustomerRequestFactory_LowStockProduct_NeverRequestsMoreThanAvailable()
    {
        var catalog = new List<CatalogProduct>
        {
            new() { Id = 1, Name = "Nearly Sold Out Item", Category = "Dice", Quantity = 2, Price = 5.00m }
        };
        var factory = new CatalogCustomerRequestFactory(catalog, seed: 2);

        for (var i = 0; i < 20; i++)
        {
            var qty = int.Parse(factory.Create().Parameters["qty"]);
            Assert.IsLessThanOrEqualTo(2, qty);
        }
    }

    // --- AdversarialCustomerRequestFactory (negative/security-style input testing) --

    // The pipeline must survive a full run of malformed/injection-style input without
    // throwing — reliability under bad input, exercised through the same execution path
    // as normal load rather than any live attack against a real system.
    [TestMethod]
    public async Task RunAsync_AdversarialRequests_CompletesWithoutThrowing()
    {
        var profile = new SpikeProfile(
            duration: TimeSpan.FromSeconds(1),
            baselineConcurrency: 2,
            spikeConcurrency: 4,
            spikeStart: TimeSpan.FromMilliseconds(400),
            spikeDuration: TimeSpan.FromMilliseconds(300));

        var config = new LoadTestConfiguration
        {
            TargetBaseUrl = "http://localhost:5000",
            CustomerEndpointPath = "/api/customers/orders",
            Profile = profile,
            MaxConcurrency = 10,
            BatchSize = 2,
            RequestTimeout = TimeSpan.FromSeconds(1),
            RunLabel = "adversarial-input-test"
        };

        ICustomerEndpointClient endpointClient = new SimulatedCustomerEndpointClient(
            baseLatencyMs: 5, latencyJitterMs: 2, errorRate: 0.02, seed: 3);
        var requestFactory = new AdversarialCustomerRequestFactory(seed: 3);
        var generator = new Execution.LoadGenerator(config, endpointClient, requestFactory);

        var result = await generator.RunAsync();

        Assert.IsNotNull(result);
        Assert.HasCount(1, result.EndpointMetrics);
    }

    // --- JsonRunExporter (P2 -> P3 handoff contract) ---------------------------------

    // Confirms the exported JSON uses exactly the field names P3's TestRun/JsonTestRunStore
    // contract expects (Documentation/P3-README.md), so a mismatch here fails the build
    // instead of surfacing later as a silent P3 deserialization gap.
    [TestMethod]
    public async Task ExportAsync_WritesFileMatchingP3TestRunContract()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"p2-export-test-{Guid.NewGuid():N}.json");

        try
        {
            var result = new LoadTestRunResult
            {
                RunId = Guid.NewGuid(),
                RunLabel = "contract-test",
                ProfileName = "Spike",
                StartedAtUtc = DateTimeOffset.UtcNow,
                CompletedAtUtc = DateTimeOffset.UtcNow.AddSeconds(10),
                EndpointMetrics = new[]
                {
                    new EndpointLoadMetric
                    {
                        EndpointName = "/api/customers/orders",
                        TotalRequests = 10,
                        SuccessCount = 9,
                        FailureCount = 1,
                        AverageLatencyMs = 50,
                        P50LatencyMs = 45,
                        P95LatencyMs = 90,
                        P99LatencyMs = 120,
                        MaxLatencyMs = 130,
                        ThroughputRps = 0.9
                    }
                }
            };

            var exporter = new JsonRunExporter(tempFile, buildId: "build-test-1");
            await exporter.ExportAsync(result);

            using var stream = File.OpenRead(tempFile);
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(stream);
            var run = doc.RootElement[0];

            Assert.AreEqual("build-test-1", run.GetProperty("BuildId").GetString());
            Assert.AreEqual("Spike", run.GetProperty("LoadProfile").GetString());
            Assert.IsTrue(run.TryGetProperty("Timestamp", out _));
            Assert.IsTrue(run.TryGetProperty("AnomalyDetected", out var anomaly));
            Assert.AreEqual(System.Text.Json.JsonValueKind.Null, anomaly.ValueKind);

            var metric = run.GetProperty("EndpointMetrics")[0];
            Assert.AreEqual("/api/customers/orders", metric.GetProperty("EndpointName").GetString());
            Assert.AreEqual(45, metric.GetProperty("LatencyP50Ms").GetDouble());
            Assert.AreEqual(90, metric.GetProperty("LatencyP95Ms").GetDouble());
            Assert.AreEqual(120, metric.GetProperty("LatencyP99Ms").GetDouble());
            Assert.AreEqual(0.9, metric.GetProperty("ThroughputRps").GetDouble());
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // Exporting two runs to the same file must append (produce a 2-element array), matching
    // P3's JsonTestRunStore read-modify-write behaviour, not overwrite the first run.
    [TestMethod]
    public async Task ExportAsync_CalledTwice_AppendsRatherThanOverwrites()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"p2-export-append-test-{Guid.NewGuid():N}.json");

        try
        {
            var exporter = new JsonRunExporter(tempFile, buildId: "build-test-append");

            for (var i = 0; i < 2; i++)
            {
                var result = new LoadTestRunResult
                {
                    RunId = Guid.NewGuid(),
                    RunLabel = $"append-test-{i}",
                    ProfileName = "Soak",
                    StartedAtUtc = DateTimeOffset.UtcNow,
                    CompletedAtUtc = DateTimeOffset.UtcNow.AddSeconds(5),
                    EndpointMetrics = Array.Empty<EndpointLoadMetric>()
                };

                await exporter.ExportAsync(result);
            }

            using var stream = File.OpenRead(tempFile);
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(stream);

            Assert.AreEqual(2, doc.RootElement.GetArrayLength());
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
