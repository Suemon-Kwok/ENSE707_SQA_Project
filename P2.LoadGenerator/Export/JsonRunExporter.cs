using System.Text.Json;
using P2.LoadGenerator.Metrics;

namespace P2.LoadGenerator.Export;

/// <summary>
/// Appends a completed run to a JSON file shaped exactly like P3's TestRun contract
/// (BuildId, Timestamp, LoadProfile, EndpointMetrics[], AnomalyDetected), so the file this
/// writes can be pointed at directly by P3's JsonTestRunStore. This replaces the manual
/// "read the console output, retype it somewhere" step the mid-project report flagged as
/// the next thing to fix (Documentation/P3-README.md, "Next steps once P1/P2 land real data").
///
/// Uses the same simple read-modify-write strategy as P3's JsonTestRunStore (kept as a
/// separate, independent implementation rather than a shared class — see the note on
/// TestRunExportDto).
/// </summary>
public sealed class JsonRunExporter : IResultExporter
{
    private readonly string _filePath;
    private readonly string _buildId;
    private static readonly object FileLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <param name="filePath">Path to the shared runs file, e.g. the same file P3's
    /// JsonTestRunStore is pointed at.</param>
    /// <param name="buildId">Identifies which build/commit this run was executed against.
    /// Falls back to a timestamp-based local label if not supplied, since not every local
    /// run happens inside CI with a known build number.</param>
    public JsonRunExporter(string filePath, string? buildId = null)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _buildId = string.IsNullOrWhiteSpace(buildId)
            ? $"local-{DateTime.UtcNow:yyyyMMdd-HHmmss}"
            : buildId;
    }

    public Task<string> ExportAsync(LoadTestRunResult result, CancellationToken cancellationToken = default)
    {
        var dto = MapToContract(result);

        lock (FileLock)
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var existingRuns = ReadExistingRuns();
            existingRuns.Add(dto);

            var json = JsonSerializer.Serialize(existingRuns, JsonOptions);
            File.WriteAllText(_filePath, json);
        }

        return Task.FromResult(_filePath);
    }

    private TestRunExportDto MapToContract(LoadTestRunResult result)
    {
        return new TestRunExportDto
        {
            Id = result.RunId,
            BuildId = _buildId,
            Timestamp = result.StartedAtUtc.UtcDateTime,
            LoadProfile = result.ProfileName,
            AnomalyDetected = null,
            EndpointMetrics = result.EndpointMetrics.Select(m => new EndpointMetricExportDto
            {
                EndpointName = m.EndpointName,
                LatencyP50Ms = m.P50LatencyMs,
                LatencyP95Ms = m.P95LatencyMs,
                LatencyP99Ms = m.P99LatencyMs,
                ErrorRatePercent = m.ErrorRatePercent,
                ThroughputRps = m.ThroughputRps
            }).ToList()
        };
    }

    private List<TestRunExportDto> ReadExistingRuns()
    {
        if (!File.Exists(_filePath))
        {
            return new List<TestRunExportDto>();
        }

        var json = File.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<TestRunExportDto>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<TestRunExportDto>>(json, JsonOptions)
                   ?? new List<TestRunExportDto>();
        }
        catch (JsonException ex)
        {
            // Never overwrite a file we can't parse — that would silently wipe P3's run
            // history. Fail loudly with the path so the user can fix or move the file.
            throw new InvalidDataException(
                $"Existing runs file '{_filePath}' is not valid run JSON; refusing to overwrite it.", ex);
        }
    }
}
