using P2.LoadGenerator.Catalog;
using P2.LoadGenerator.Configuration;
using P2.LoadGenerator.Endpoints;
using P2.LoadGenerator.Execution;
using P2.LoadGenerator.Export;
using P2.LoadGenerator.Metrics;
using P2.LoadGenerator.Profiles;

namespace P2.LoadGenerator.Gui;

/// <summary>
/// Single-form GUI over the existing P2.LoadGenerator engine (Task 1 rubric item: P2 needs
/// a GUI). Deliberately thin — it builds a LoadTestConfiguration from the controls, calls
/// the same LoadGenerator/ICustomerEndpointClient/ICustomerRequestFactory classes the
/// console app and tests already use, and displays/exports the result. No new engine
/// classes live here; this form is UI only.
/// </summary>
public sealed class LoadGeneratorForm : Form
{
    private readonly ComboBox _profileCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _durationSeconds = new() { Minimum = 1, Maximum = 600, Value = 20 };
    private readonly NumericUpDown _maxConcurrency = new() { Minimum = 1, Maximum = 2000, Value = 100 };
    private readonly NumericUpDown _batchSize = new() { Minimum = 1, Maximum = 500, Value = 10 };
    private readonly ComboBox _requestSourceCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _targetCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _targetUrlBox = new() { Text = "http://localhost:5000", Enabled = false };
    private readonly TextBox _endpointPathBox = new() { Text = "/api/customers/orders" };
    private readonly Button _runButton = new() { Text = "Run Load Test" };
    private readonly Button _cancelButton = new() { Text = "Cancel", Enabled = false };
    private readonly Button _exportButton = new() { Text = "Export to P3 JSON...", Enabled = false };
    private readonly Label _statusLabel = new() { AutoSize = true, Text = "Idle." };
    private readonly DataGridView _resultsGrid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        AllowUserToAddRows = false
    };

    private LoadTestRunResult? _lastResult;
    private CancellationTokenSource? _runCancellation;

    public LoadGeneratorForm()
    {
        Text = "P2 Load Generator";
        Width = 900;
        Height = 600;
        StartPosition = FormStartPosition.CenterScreen;

        _profileCombo.Items.AddRange(LoadProfileFactory.ProfileNames.ToArray<object>());
        _profileCombo.SelectedIndex = 0;

        _requestSourceCombo.Items.AddRange(new object[]
        {
            "Catalog (real P1 product data)",
            "Adversarial (negative/security-style input)"
        });
        _requestSourceCombo.SelectedIndex = 0;

        _targetCombo.Items.AddRange(new object[] { "Simulated endpoint", "Live HTTP endpoint" });
        _targetCombo.SelectedIndex = 0;
        _targetCombo.SelectedIndexChanged += (_, _) =>
            _targetUrlBox.Enabled = _targetCombo.SelectedIndex == 1;

        _runButton.Click += async (_, _) => await RunAsync();
        _cancelButton.Click += (_, _) => _runCancellation?.Cancel();
        _exportButton.Click += (_, _) => Export();

        BuildLayout();
    }

    private void BuildLayout()
    {
        var configPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 4,
            AutoSize = true,
            Padding = new Padding(8)
        };

        AddRow(configPanel, "Load profile:", _profileCombo, "Duration (s):", _durationSeconds);
        AddRow(configPanel, "Max concurrency:", _maxConcurrency, "Batch size:", _batchSize);
        AddRow(configPanel, "Request data:", _requestSourceCombo, "Target:", _targetCombo);
        AddRow(configPanel, "Target base URL:", _targetUrlBox, "Endpoint path:", _endpointPathBox);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        buttonPanel.Controls.Add(_runButton);
        buttonPanel.Controls.Add(_cancelButton);
        buttonPanel.Controls.Add(_exportButton);
        buttonPanel.Controls.Add(_statusLabel);

        Controls.Add(_resultsGrid);
        Controls.Add(buttonPanel);
        Controls.Add(configPanel);
    }

    private static void AddRow(TableLayoutPanel panel, string label1, Control control1, string label2, Control control2)
    {
        var row = panel.RowCount;
        panel.RowCount++;
        panel.Controls.Add(new Label { Text = label1, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        control1.Width = 220;
        panel.Controls.Add(control1, 1, row);
        panel.Controls.Add(new Label { Text = label2, AutoSize = true, Anchor = AnchorStyles.Left }, 2, row);
        control2.Width = 220;
        panel.Controls.Add(control2, 3, row);
    }

    private async Task RunAsync()
    {
        var useHttp = _targetCombo.SelectedIndex == 1;
        Uri? targetUri = null;
        if (useHttp && !TryParseHttpUri(_targetUrlBox.Text, out targetUri))
        {
            MessageBox.Show(this, "Target base URL must be an absolute http:// or https:// address.",
                "Invalid target URL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _runButton.Enabled = false;
        _cancelButton.Enabled = true;
        _exportButton.Enabled = false;
        _statusLabel.Text = "Running...";
        _resultsGrid.DataSource = null;
        _runCancellation = new CancellationTokenSource();
        HttpClient? httpClient = null;

        try
        {
            var profile = LoadProfileFactory.Create(
                (string)_profileCombo.SelectedItem!,
                TimeSpan.FromSeconds((double)_durationSeconds.Value),
                (int)_maxConcurrency.Value);
            var config = new LoadTestConfiguration
            {
                TargetBaseUrl = _targetUrlBox.Text,
                CustomerEndpointPath = _endpointPathBox.Text,
                Profile = profile,
                MaxConcurrency = (int)_maxConcurrency.Value,
                BatchSize = (int)_batchSize.Value,
                RequestTimeout = TimeSpan.FromSeconds(5),
                RunLabel = $"gui-run-{DateTime.Now:HHmmss}"
            };

            ICustomerEndpointClient endpointClient;
            if (useHttp)
            {
                // One HttpClient per run, disposed in finally, so repeated runs don't leak sockets.
                httpClient = new HttpClient { BaseAddress = targetUri };
                endpointClient = new HttpCustomerEndpointClient(httpClient, _endpointPathBox.Text);
            }
            else
            {
                endpointClient = new SimulatedCustomerEndpointClient(baseLatencyMs: 40, latencyJitterMs: 15, errorRate: 0.02);
            }

            ICustomerRequestFactory requestFactory = _requestSourceCombo.SelectedIndex == 1
                ? new AdversarialCustomerRequestFactory()
                : new CatalogCustomerRequestFactory(ProductCatalogLoader.Load());

            var generator = new Execution.LoadGenerator(config, endpointClient, requestFactory);
            var result = await generator.RunAsync(_runCancellation.Token);

            _lastResult = result;
            _resultsGrid.DataSource = result.EndpointMetrics
                .Select(m => new
                {
                    m.EndpointName,
                    m.TotalRequests,
                    m.FailureCount,
                    m.ErrorRatePercent,
                    AvgMs = Math.Round(m.AverageLatencyMs, 1),
                    P50 = Math.Round(m.P50LatencyMs, 1),
                    P95 = Math.Round(m.P95LatencyMs, 1),
                    P99 = Math.Round(m.P99LatencyMs, 1),
                    m.ThroughputRps
                })
                .ToList();

            _statusLabel.Text = $"Done: {result.Duration.TotalSeconds:F1}s, profile '{result.ProfileName}'.";
            _exportButton.Enabled = true;
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Run cancelled.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Run failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "Load test failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            httpClient?.Dispose();
            _runCancellation.Dispose();
            _runCancellation = null;
            _cancelButton.Enabled = false;
            _runButton.Enabled = true;
        }
    }

    private static bool TryParseHttpUri(string text, out Uri? uri) =>
        Uri.TryCreate(text, UriKind.Absolute, out uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);


    private void Export()
    {
        if (_lastResult is null) return;

        // Default to the shared SampleData/runs.json P3.Dashboard reads, so a GUI run shows up
        // in the regression tracker without the user having to find that file themselves.
        var sharedPath = SharedRunsFile.ResolvePath();
        using var dialog = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
            InitialDirectory = Path.GetDirectoryName(sharedPath),
            FileName = Path.GetFileName(sharedPath),
            OverwritePrompt = false // the exporter appends; it never replaces existing runs
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var exporter = new JsonRunExporter(dialog.FileName, Environment.GetEnvironmentVariable("BUILD_ID"));
        _ = exporter.ExportAsync(_lastResult).ContinueWith(t =>
        {
            _statusLabel.Text = t.IsFaulted
                ? $"Export failed: {t.Exception?.GetBaseException().Message}"
                : $"Exported to {t.Result}";
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }
}
