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

    public LoadGeneratorForm()
    {
        Text = "P2 Load Generator";
        Width = 900;
        Height = 600;
        StartPosition = FormStartPosition.CenterScreen;

        _profileCombo.Items.AddRange(new object[] { "SteadyRamp", "Spike", "Soak" });
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
        _runButton.Enabled = false;
        _exportButton.Enabled = false;
        _statusLabel.Text = "Running...";
        _resultsGrid.DataSource = null;

        try
        {
            var profile = BuildProfile();
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

            ICustomerEndpointClient endpointClient = _targetCombo.SelectedIndex == 1
                ? new HttpCustomerEndpointClient(new HttpClient { BaseAddress = new Uri(_targetUrlBox.Text) }, _endpointPathBox.Text)
                : new SimulatedCustomerEndpointClient(baseLatencyMs: 40, latencyJitterMs: 15, errorRate: 0.02);

            ICustomerRequestFactory requestFactory = _requestSourceCombo.SelectedIndex == 1
                ? new AdversarialCustomerRequestFactory()
                : new CatalogCustomerRequestFactory(ProductCatalogLoader.Load());

            var generator = new Execution.LoadGenerator(config, endpointClient, requestFactory);
            var result = await generator.RunAsync();

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
        catch (Exception ex)
        {
            _statusLabel.Text = $"Run failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "Load test failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _runButton.Enabled = true;
        }
    }

    private ILoadProfile BuildProfile()
    {
        var duration = TimeSpan.FromSeconds((double)_durationSeconds.Value);
        var peak = (int)_maxConcurrency.Value;

        return _profileCombo.SelectedItem as string switch
        {
            "Spike" => new SpikeProfile(
                duration: duration,
                baselineConcurrency: Math.Max(1, peak / 10),
                spikeConcurrency: peak,
                spikeStart: TimeSpan.FromSeconds(duration.TotalSeconds * 0.4),
                spikeDuration: TimeSpan.FromSeconds(duration.TotalSeconds * 0.2)),
            "Soak" => new SoakProfile(duration, concurrency: Math.Max(1, peak / 4)),
            _ => new SteadyRampProfile(duration, startConcurrency: 1, peakConcurrency: peak)
        };
    }

    private void Export()
    {
        if (_lastResult is null) return;

        using var dialog = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
            FileName = "runs.json"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var exporter = new JsonRunExporter(dialog.FileName);
        _ = exporter.ExportAsync(_lastResult).ContinueWith(t =>
        {
            _statusLabel.Text = t.IsFaulted
                ? $"Export failed: {t.Exception?.GetBaseException().Message}"
                : $"Exported to {t.Result}";
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }
}
