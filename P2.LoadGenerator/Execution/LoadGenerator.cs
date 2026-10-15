using P2.LoadGenerator.Configuration;
using P2.LoadGenerator.Endpoints;
using P2.LoadGenerator.Metrics;

namespace P2.LoadGenerator.Execution;

/// <summary>
/// Orchestrates a full load test run: repeatedly asks the configured
/// <see cref="P2.LoadGenerator.Profiles.ILoadProfile"/> how many requests should be
/// in flight, sends that many batched requests via <see cref="ICustomerEndpointClient"/>,
/// then hands the collected results to <see cref="RunMetricsAggregator"/>.
/// </summary>
public sealed class LoadGenerator : ILoadGenerator
{
    private readonly LoadTestConfiguration _config;
    private readonly ICustomerEndpointClient _endpointClient;
    private readonly ICustomerRequestFactory _requestFactory;
    private readonly RunMetricsAggregator _aggregator;
    private readonly TimeSpan _tickInterval;

    public LoadGenerator(
        LoadTestConfiguration config,
        ICustomerEndpointClient endpointClient,
        ICustomerRequestFactory requestFactory,
        RunMetricsAggregator? aggregator = null,
        TimeSpan? tickInterval = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));

        // SemaphoreSlim(0) never releases a slot, so MaxConcurrency = 0 used to hang the
        // run forever instead of failing; reject it (and a non-positive timeout) up front.
        if (config.MaxConcurrency < 1)
            throw new ArgumentOutOfRangeException(nameof(config), "MaxConcurrency must be at least 1.");
        if (config.RequestTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(config), "RequestTimeout must be positive.");

        _endpointClient = endpointClient ?? throw new ArgumentNullException(nameof(endpointClient));
        _requestFactory = requestFactory ?? throw new ArgumentNullException(nameof(requestFactory));
        _aggregator = aggregator ?? new RunMetricsAggregator();
        _tickInterval = tickInterval ?? TimeSpan.FromMilliseconds(500);
    }

    public async Task<LoadTestRunResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var runId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;
        var elapsedClock = System.Diagnostics.Stopwatch.StartNew();

        var allResults = new List<RequestResult>();
        using var concurrencyLimiter = new SemaphoreSlim(_config.MaxConcurrency);

        while (elapsedClock.Elapsed < _config.Profile.Duration)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var targetConcurrency = _config.Profile.GetTargetConcurrency(elapsedClock.Elapsed);
            var batchSize = Math.Min(targetConcurrency, _config.BatchSize);
            batchSize = Math.Max(batchSize, 1);

            var batchResults = await SendBatchAsync(batchSize, concurrencyLimiter, cancellationToken);
            allResults.AddRange(batchResults);

            await Task.Delay(_tickInterval, cancellationToken);
        }

        var completedAt = DateTimeOffset.UtcNow;

        var endpointMetric = _aggregator.Aggregate(_config.CustomerEndpointPath, allResults, elapsedClock.Elapsed);

        return new LoadTestRunResult
        {
            RunId = runId,
            RunLabel = _config.RunLabel,
            ProfileName = _config.Profile.Name,
            StartedAtUtc = startedAt,
            CompletedAtUtc = completedAt,
            EndpointMetrics = new[] { endpointMetric }
        };
    }

    private async Task<List<RequestResult>> SendBatchAsync(
        int batchSize,
        SemaphoreSlim concurrencyLimiter,
        CancellationToken cancellationToken)
    {
        var tasks = new List<Task<RequestResult>>(batchSize);

        for (var i = 0; i < batchSize; i++)
        {
            tasks.Add(SendOneAsync(concurrencyLimiter, cancellationToken));
        }

        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    private async Task<RequestResult> SendOneAsync(SemaphoreSlim concurrencyLimiter, CancellationToken cancellationToken)
    {
        await concurrencyLimiter.WaitAsync(cancellationToken);
        var request = _requestFactory.Create();

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_config.RequestTimeout);

            return await _endpointClient.SendAsync(request, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The run itself wasn't cancelled — this was our own per-request timeout firing.
            // Record it as a failed request rather than letting it blow up the whole batch;
            // a timeout under load is exactly the kind of signal this tool exists to capture.
            return new RequestResult
            {
                RequestId = request.RequestId,
                StartedAtUtc = DateTimeOffset.UtcNow,
                LatencyMs = _config.RequestTimeout.TotalMilliseconds,
                Success = false,
                ErrorMessage = $"Request timed out after {_config.RequestTimeout.TotalMilliseconds:F0}ms."
            };
        }
        finally
        {
            concurrencyLimiter.Release();
        }
    }
}
