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
    // --- Metrics ---------------------------------------------------------------------

    // Error rate must be failures / total as a rounded percentage, including the
    // divide-by-zero case (no requests) and both extremes.
    [TestMethod]
    [DataRow(0, 0, 0.0, DisplayName = "No requests -> 0%, not NaN")]
    [DataRow(100, 0, 0.0, DisplayName = "All succeed -> 0%")]
    [DataRow(215, 5, 2.33, DisplayName = "5 of 215 fail -> 2.33%")]
    [DataRow(50, 50, 100.0, DisplayName = "All fail -> 100%")]
    public void ErrorRatePercent_IsFailuresOverTotal(int total, int failures, double expected)
    {
        var metric = new EndpointLoadMetric
        {
            EndpointName = "/api/test",
            TotalRequests = total,
            SuccessCount = total - failures,
            FailureCount = failures,
            AverageLatencyMs = 0,
            P50LatencyMs = 0,
            P95LatencyMs = 0,
            P99LatencyMs = 0,
            MaxLatencyMs = 0,
            ThroughputRps = 0
        };

        Assert.AreEqual(expected, metric.ErrorRatePercent);
    }

    // With latencies 1..100ms supplied out of order, nearest-rank percentiles are exact:
    // P50=50, P95=95, P99=99. These are the numbers P3 runs its regression checks on.
    [TestMethod]
    public void Aggregate_UnsortedLatencies_ReportsNearestRankPercentiles()
    {
        var results = Enumerable.Range(1, 100).Reverse()
            .Select(ms => Result(latencyMs: ms, success: true))
            .ToList();

        var metric = new RunMetricsAggregator().Aggregate("/api/test", results, TimeSpan.FromSeconds(10));

        Assert.AreEqual(50, metric.P50LatencyMs);
        Assert.AreEqual(95, metric.P95LatencyMs);
        Assert.AreEqual(99, metric.P99LatencyMs);
        Assert.AreEqual(100, metric.MaxLatencyMs);
    }

    // Throughput is successful requests per second; failures aren't capacity the
    // endpoint actually sustained, so 2 successes + 1 failure over 2s = 1.0 rps.
    [TestMethod]
    public void Aggregate_Throughput_CountsSuccessesOnly()
    {
        var results = new List<RequestResult>
        {
            Result(latencyMs: 20, success: true),
            Result(latencyMs: 20, success: true),
            Result(latencyMs: 20, success: false)
        };

        var metric = new RunMetricsAggregator().Aggregate("/api/test", results, TimeSpan.FromSeconds(2));

        Assert.AreEqual(1.0, metric.ThroughputRps);
    }

    // --- Load profiles -----------------------------------------------------------------

    // Regression test: the ramp used Math.Round, so it hit peak (100) at 9.999s of a 10s
    // ramp. It must stay below peak until the duration has fully elapsed.
    [TestMethod]
    public void SteadyRampProfile_ReachesPeakOnlyAtEndOfDuration()
    {
        var profile = new SteadyRampProfile(TimeSpan.FromSeconds(10), startConcurrency: 1, peakConcurrency: 100);

        Assert.IsLessThan(100, profile.GetTargetConcurrency(TimeSpan.FromMilliseconds(9999)));
        Assert.AreEqual(100, profile.GetTargetConcurrency(TimeSpan.FromSeconds(10)));
    }

    // Spike window is [start, start + duration): on at its first instant, off again at its end.
    [TestMethod]
    public void SpikeProfile_WindowBoundaries_StartInclusiveEndExclusive()
    {
        var profile = new SpikeProfile(TimeSpan.FromSeconds(10), baselineConcurrency: 5, spikeConcurrency: 50,
            spikeStart: TimeSpan.FromSeconds(4), spikeDuration: TimeSpan.FromSeconds(2));

        Assert.AreEqual(5, profile.GetTargetConcurrency(TimeSpan.FromMilliseconds(3999)));
        Assert.AreEqual(50, profile.GetTargetConcurrency(TimeSpan.FromSeconds(4)));
        Assert.AreEqual(5, profile.GetTargetConcurrency(TimeSpan.FromSeconds(6)));
    }

    // Invalid profile input must be rejected at construction rather than producing a
    // nonsense run (negative concurrency, zero duration, spike lower than baseline).
    [TestMethod]
    public void Profiles_InvalidInput_ThrowOnConstruction()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SoakProfile(TimeSpan.FromSeconds(1), concurrency: -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SoakProfile(TimeSpan.Zero, concurrency: 5));
        Assert.ThrowsExactly<ArgumentException>(() => new SpikeProfile(TimeSpan.FromSeconds(10), baselineConcurrency: 10,
            spikeConcurrency: 5, spikeStart: TimeSpan.FromSeconds(1), spikeDuration: TimeSpan.FromSeconds(1)));
    }

    // The GUI's profile dropdown goes through LoadProfileFactory, so each name must build
    // the right profile shape from the user's peak concurrency, and unknown names must fail.
    [TestMethod]
    public void LoadProfileFactory_GuiProfileNames_BuildExpectedShapes()
    {
        var duration = TimeSpan.FromSeconds(20);

        var ramp = LoadProfileFactory.Create("SteadyRamp", duration, 100);
        var spike = LoadProfileFactory.Create("Spike", duration, 100);
        var soak = LoadProfileFactory.Create("Soak", duration, 100);

        Assert.AreEqual(100, ramp.GetTargetConcurrency(duration));
        Assert.AreEqual(10, spike.GetTargetConcurrency(TimeSpan.FromSeconds(1)), "Spike baseline should be 10% of peak.");
        Assert.AreEqual(100, spike.GetTargetConcurrency(TimeSpan.FromSeconds(10)), "Spike should be at peak mid-run.");
        Assert.AreEqual(25, soak.GetTargetConcurrency(TimeSpan.FromSeconds(10)), "Soak should hold 25% of peak.");
        Assert.ThrowsExactly<ArgumentException>(() => LoadProfileFactory.Create("Bogus", duration, 10));
    }

    // --- LoadGenerator execution ---------------------------------------------------------

    // Regression test: MaxConcurrency = 0 created SemaphoreSlim(0), which hung the run
    // forever. It must now be rejected immediately.
    [TestMethod]
    public void LoadGenerator_ZeroMaxConcurrency_ThrowsInsteadOfHanging()
    {
        var config = ConfigFor(new SoakProfile(TimeSpan.FromSeconds(1), 1), maxConcurrency: 0);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new Execution.LoadGenerator(config, new SimulatedCustomerEndpointClient(), new RandomCustomerRequestFactory()));
    }

    // An endpoint that never answers must be recorded as 100% timed-out failures and the
    // run must still finish — a hung server is exactly what a load test needs to report.
    [TestMethod]
    public async Task RunAsync_EndpointNeverResponds_RecordsTimeoutsAsFailures()
    {
        var config = ConfigFor(new SoakProfile(TimeSpan.FromMilliseconds(300), 2), timeout: TimeSpan.FromMilliseconds(50));
        var generator = new Execution.LoadGenerator(config, new NeverRespondingEndpointClient(),
            new RandomCustomerRequestFactory(seed: 1), tickInterval: TimeSpan.FromMilliseconds(10));

        var metric = (await generator.RunAsync()).EndpointMetrics[0];

        Assert.IsGreaterThan(0, metric.TotalRequests);
        Assert.AreEqual(100.0, metric.ErrorRatePercent);
    }

    // Cancelling a run (the GUI's Cancel button) must stop it promptly rather than
    // running to the end of a 5-minute profile.
    [TestMethod]
    public async Task RunAsync_Cancelled_StopsPromptly()
    {
        var config = ConfigFor(new SoakProfile(TimeSpan.FromMinutes(5), 2));
        var generator = new Execution.LoadGenerator(config,
            new SimulatedCustomerEndpointClient(baseLatencyMs: 5, latencyJitterMs: 0, errorRate: 0, seed: 1),
            new RandomCustomerRequestFactory(seed: 1));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        await Assert.ThrowsAsync<OperationCanceledException>(() => generator.RunAsync(cts.Token));

        Assert.IsLessThan(TimeSpan.FromSeconds(5), stopwatch.Elapsed);
    }

    // --- Request generation from P1's catalogue -------------------------------------------

    // Requests must use real catalogue product IDs, and a product with only 2 in stock
    // must never be requested in a quantity above 2.
    [TestMethod]
    public void CatalogCustomerRequestFactory_LowStockProduct_UsesCatalogIdAndNeverExceedsStock()
    {
        var catalog = new List<CatalogProduct>
        {
            new() { Id = 7, Name = "Nearly Sold Out Dice", Category = "Dice", Quantity = 2, Price = 5.00m }
        };
        var factory = new CatalogCustomerRequestFactory(catalog, seed: 2);

        for (var i = 0; i < 20; i++)
        {
            var request = factory.Create();
            Assert.AreEqual("7", request.Parameters["productId"]);
            Assert.IsLessThanOrEqualTo(2, int.Parse(request.Parameters["qty"]));
        }
    }

    // --- HttpCustomerEndpointClient (live-endpoint path, via a stub transport) -------------

    // Only 2xx responses count as successes; server/client errors are failures, not exceptions.
    [TestMethod]
    [DataRow(200, true)]
    [DataRow(404, false)]
    [DataRow(500, false)]
    public async Task HttpClient_StatusCode_MapsToSuccessOrFailure(int statusCode, bool expectedSuccess)
    {
        var client = HttpClientWith(new StubHttpHandler(_ => new HttpResponseMessage((System.Net.HttpStatusCode)statusCode)));

        var result = await client.SendAsync(SampleRequest(), CancellationToken.None);

        Assert.AreEqual(expectedSuccess, result.Success);
        Assert.AreEqual(statusCode, result.StatusCode);
    }

    // A dropped connection / unreachable host must become a failed result carrying the
    // error message, so one bad request can't crash the whole run.
    [TestMethod]
    public async Task HttpClient_TransportFailure_RecordsFailureInsteadOfThrowing()
    {
        var client = HttpClientWith(new StubHttpHandler(_ => throw new HttpRequestException("connection refused")));

        var result = await client.SendAsync(SampleRequest(), CancellationToken.None);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.ErrorMessage!, "connection refused");
    }

    // Security: injection-style input must be URL-encoded on the wire, so it arrives as a
    // literal value and can't add or override query parameters.
    [TestMethod]
    public async Task HttpClient_InjectionInput_IsUrlEncoded()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest));

        await HttpClientWith(handler).SendAsync(SampleRequest(productId: "1&qty=999 OR 1=1"), CancellationToken.None);

        var query = handler.LastRequestUri!.Query;
        StringAssert.Contains(query, "productId=1%26qty%3D999%20OR%201%3D1");
        Assert.AreEqual(1, query.Split("qty=").Length - 1, "Injected '&qty=' must not create a second qty parameter.");
    }

    // --- Export to P3 ------------------------------------------------------------------------

    // Integration: a real short run through LoadGenerator, exported by JsonRunExporter, must
    // produce exactly the field names P3's TestRun contract reads (Documentation/P3-README.md).
    [TestMethod]
    public async Task RunThenExport_WritesRunMatchingP3Contract()
    {
        var tempFile = TempFile();

        try
        {
            var generator = new Execution.LoadGenerator(ConfigFor(new SoakProfile(TimeSpan.FromMilliseconds(400), 3)),
                new SimulatedCustomerEndpointClient(baseLatencyMs: 5, latencyJitterMs: 1, errorRate: 0, seed: 9),
                new CatalogCustomerRequestFactory(seed: 9), tickInterval: TimeSpan.FromMilliseconds(20));

            await new JsonRunExporter(tempFile, buildId: "e2e-build").ExportAsync(await generator.RunAsync());

            using var doc = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(tempFile));
            var run = doc.RootElement[0];
            var metric = run.GetProperty("EndpointMetrics")[0];

            Assert.AreEqual("e2e-build", run.GetProperty("BuildId").GetString());
            Assert.AreEqual("Soak", run.GetProperty("LoadProfile").GetString());
            Assert.IsTrue(run.TryGetProperty("Timestamp", out _));
            Assert.AreEqual(System.Text.Json.JsonValueKind.Null, run.GetProperty("AnomalyDetected").ValueKind);
            Assert.AreEqual("/api/customers/orders", metric.GetProperty("EndpointName").GetString());
            Assert.IsGreaterThan(0.0, metric.GetProperty("LatencyP95Ms").GetDouble());
            Assert.IsGreaterThan(0.0, metric.GetProperty("ThroughputRps").GetDouble());
            Assert.AreEqual(0.0, metric.GetProperty("ErrorRatePercent").GetDouble());
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // Exporting twice must append (2-element array), never overwrite earlier run history.
    [TestMethod]
    public async Task ExportAsync_CalledTwice_AppendsRatherThanOverwrites()
    {
        var tempFile = TempFile();

        try
        {
            var exporter = new JsonRunExporter(tempFile, buildId: "build-append");
            await exporter.ExportAsync(SampleRunResult());
            await exporter.ExportAsync(SampleRunResult());

            using var doc = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(tempFile));
            Assert.AreEqual(2, doc.RootElement.GetArrayLength());
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // Regression test: a corrupt runs file must be reported and left untouched —
    // overwriting it would silently wipe P3's entire run history.
    [TestMethod]
    public async Task ExportAsync_CorruptExistingFile_ThrowsAndLeavesFileUntouched()
    {
        var tempFile = TempFile();
        const string corrupt = "{ this is not a run array";

        try
        {
            await File.WriteAllTextAsync(tempFile, corrupt);

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                new JsonRunExporter(tempFile, buildId: "build-corrupt").ExportAsync(SampleRunResult()));

            Assert.AreEqual(corrupt, await File.ReadAllTextAsync(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // The console app and GUI both export via SharedRunsFile, so from deep inside a build
    // output folder it must find the repo's SampleData/runs.json (the file P3 reads).
    [TestMethod]
    public void SharedRunsFile_FromNestedBuildFolder_FindsSampleDataRunsJson()
    {
        var root = Path.Combine(Path.GetTempPath(), $"p2-repo-{Guid.NewGuid():N}");
        var buildFolder = Path.Combine(root, "P2.LoadGenerator.Gui", "bin", "Debug", "net8.0-windows");

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "SampleData"));
            Directory.CreateDirectory(buildFolder);

            Assert.AreEqual(Path.Combine(root, "SampleData", "runs.json"), SharedRunsFile.ResolvePath(buildFolder));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // --- Helpers -----------------------------------------------------------------------------

    private static LoadTestConfiguration ConfigFor(ILoadProfile profile, int maxConcurrency = 10, TimeSpan? timeout = null) => new()
    {
        TargetBaseUrl = "http://localhost:5000",
        CustomerEndpointPath = "/api/customers/orders",
        Profile = profile,
        MaxConcurrency = maxConcurrency,
        BatchSize = 2,
        RequestTimeout = timeout ?? TimeSpan.FromSeconds(1),
        RunLabel = "test-run"
    };

    private static RequestResult Result(double latencyMs, bool success) => new()
    {
        RequestId = Guid.NewGuid(),
        StartedAtUtc = DateTimeOffset.UtcNow,
        LatencyMs = latencyMs,
        Success = success
    };

    private static LoadTestRunResult SampleRunResult() => new()
    {
        RunId = Guid.NewGuid(),
        RunLabel = "sample",
        ProfileName = "Soak",
        StartedAtUtc = DateTimeOffset.UtcNow,
        CompletedAtUtc = DateTimeOffset.UtcNow.AddSeconds(1),
        EndpointMetrics = Array.Empty<EndpointLoadMetric>()
    };

    private static CustomerRequest SampleRequest(string productId = "7") => new()
    {
        RequestId = Guid.NewGuid(),
        CustomerId = "cust-1",
        Parameters = new Dictionary<string, string> { ["productId"] = productId, ["qty"] = "1" }
    };

    private static string TempFile() => Path.Combine(Path.GetTempPath(), $"p2-test-{Guid.NewGuid():N}.json");

    private static HttpCustomerEndpointClient HttpClientWith(StubHttpHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") }, "/api/customers/orders");

    /// <summary>Endpoint that never answers on its own — only the per-request timeout ends it.</summary>
    private sealed class NeverRespondingEndpointClient : ICustomerEndpointClient
    {
        public async Task<RequestResult> SendAsync(CustomerRequest request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    /// <summary>Fake HTTP transport so HttpCustomerEndpointClient can be tested without a server.</summary>
    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public Uri? LastRequestUri { get; private set; }

        public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(_respond(request));
        }
    }
}
