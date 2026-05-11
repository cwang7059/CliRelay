namespace CliRelay.CodexSwitchDashboard;

internal sealed class OverviewPage : ManagementPageBase
{
    private readonly List<RoundedSurfacePanel> _cards = [];
    private readonly Dictionary<string, Label> _metricTitleLabels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Label> _metricValueLabels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Label> _metricDetailLabels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CheckBox> _settingCheckboxes = new(StringComparer.OrdinalIgnoreCase);

    private readonly TableLayoutPanel _layout;
    private readonly FlowLayoutPanel _metricFlow;
    private readonly RoundedSurfacePanel _runtimeCard;
    private readonly RoundedSurfacePanel _systemCard;
    private readonly RoundedSurfacePanel _latencyCard;
    private readonly RoundedSurfacePanel _concurrencyCard;
    private readonly Label _runtimeTitleLabel;
    private readonly Label _runtimeSummaryLabel;
    private readonly Label _fingerprintLabel;
    private readonly Label _generatedLabel;
    private readonly Label _systemTitleLabel;
    private readonly Label _systemSummaryLabel;
    private readonly Label _latencyTitleLabel;
    private readonly Label _concurrencyTitleLabel;
    private readonly DataGridView _latencyGrid;
    private readonly DataGridView _concurrencyGrid;

    private bool _updatingToggleValues;

    public OverviewPage(ManagementApiClient client, Action<string, bool> setStatus)
        : base(client, setStatus)
    {
        AutoScroll = true;

        var scrollHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.Transparent
        };
        Controls.Add(scrollHost);

        _layout = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Padding = new Padding(0, 0, 6, 18),
            BackColor = Color.Transparent
        };
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        scrollHost.Controls.Add(_layout);
        scrollHost.Resize += (_, _) => _layout.Width = Math.Max(1000, scrollHost.ClientSize.Width - 24);

        _metricFlow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 16),
            BackColor = Color.Transparent
        };
        _layout.Controls.Add(_metricFlow, 0, 0);

        AddMetricCard("requests", "Total requests", "0", "Last 7 days");
        AddMetricCard("success-rate", "Success rate", "0%", "Healthy traffic");
        AddMetricCard("tokens", "Total tokens", "0", "Input + output");
        AddMetricCard("cost", "Estimated cost", "$0.00", "Rolling usage");
        AddMetricCard("auth-files", "Auth files", "0", "Registered accounts");
        AddMetricCard("codex", "Codex accounts", "0", "Switch-ready sessions");
        AddMetricCard("providers", "Providers", "0", "Configured channels");
        AddMetricCard("in-flight", "In flight", "0", "Current active requests");

        var summaryGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 16),
            BackColor = Color.Transparent
        };
        summaryGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        summaryGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        _layout.Controls.Add(summaryGrid, 0, 1);

        _runtimeCard = CreateSectionCard();
        summaryGrid.Controls.Add(_runtimeCard, 0, 0);

        _runtimeTitleLabel = CreateSectionTitle("Runtime controls");
        _runtimeCard.Controls.Add(_runtimeTitleLabel);

        _generatedLabel = CreateMutedLabel("Last sync: -");
        _generatedLabel.Dock = DockStyle.Top;
        _generatedLabel.Height = 22;
        _runtimeCard.Controls.Add(_generatedLabel);
        _generatedLabel.BringToFront();

        _fingerprintLabel = CreateBodyLabel("Codex fingerprint: -");
        _fingerprintLabel.Dock = DockStyle.Top;
        _fingerprintLabel.Height = 54;
        _fingerprintLabel.Padding = new Padding(0, 8, 0, 8);
        _runtimeCard.Controls.Add(_fingerprintLabel);
        _fingerprintLabel.BringToFront();

        _runtimeSummaryLabel = CreateBodyLabel("Connected endpoint details will appear here after refresh.");
        _runtimeSummaryLabel.Dock = DockStyle.Top;
        _runtimeSummaryLabel.Height = 68;
        _runtimeSummaryLabel.Padding = new Padding(0, 8, 0, 12);
        _runtimeCard.Controls.Add(_runtimeSummaryLabel);
        _runtimeSummaryLabel.BringToFront();

        var toggleLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(0, 6, 0, 0),
            BackColor = Color.Transparent
        };
        toggleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        toggleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        _runtimeCard.Controls.Add(toggleLayout);
        toggleLayout.BringToFront();

        AddSettingToggle(toggleLayout, 0, 0, "/v0/management/debug", "Debug mode");
        AddSettingToggle(toggleLayout, 1, 0, "/v0/management/request-log", "Request log");
        AddSettingToggle(toggleLayout, 0, 1, "/v0/management/usage-statistics-enabled", "Usage statistics");
        AddSettingToggle(toggleLayout, 1, 1, "/v0/management/logging-to-file", "File logging");
        AddSettingToggle(toggleLayout, 0, 2, "/v0/management/ws-auth", "WebSocket auth");
        AddSettingToggle(toggleLayout, 1, 2, "/v0/management/quota-exceeded/switch-project", "Quota switch project");
        AddSettingToggle(toggleLayout, 0, 3, "/v0/management/quota-exceeded/switch-preview-model", "Quota switch preview");

        _systemCard = CreateSectionCard();
        summaryGrid.Controls.Add(_systemCard, 1, 0);
        _systemTitleLabel = CreateSectionTitle("System health");
        _systemCard.Controls.Add(_systemTitleLabel);
        _systemSummaryLabel = CreateBodyLabel("CPU, memory, network and storage telemetry will appear here.");
        _systemSummaryLabel.Dock = DockStyle.Fill;
        _systemSummaryLabel.Padding = new Padding(0, 12, 0, 0);
        _systemCard.Controls.Add(_systemSummaryLabel);

        var gridRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            BackColor = Color.Transparent
        };
        gridRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        gridRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        _layout.Controls.Add(gridRow, 0, 2);

        _latencyCard = CreateSectionCard();
        gridRow.Controls.Add(_latencyCard, 0, 0);
        _latencyTitleLabel = CreateSectionTitle("Channel latency");
        _latencyCard.Controls.Add(_latencyTitleLabel);
        _latencyGrid = CreateGrid();
        _latencyGrid.Columns.Add(CreateTextColumn("source", "Source", 44));
        _latencyGrid.Columns.Add(CreateTextColumn("count", "Count", 22));
        _latencyGrid.Columns.Add(CreateTextColumn("avg", "Avg ms", 22));
        _latencyCard.Controls.Add(_latencyGrid);

        _concurrencyCard = CreateSectionCard();
        gridRow.Controls.Add(_concurrencyCard, 1, 0);
        _concurrencyTitleLabel = CreateSectionTitle("Active concurrency");
        _concurrencyCard.Controls.Add(_concurrencyTitleLabel);
        _concurrencyGrid = CreateGrid();
        _concurrencyGrid.Columns.Add(CreateTextColumn("key", "API key", 36));
        _concurrencyGrid.Columns.Add(CreateTextColumn("rpm", "RPM", 16));
        _concurrencyGrid.Columns.Add(CreateTextColumn("tpm", "TPM", 16));
        _concurrencyGrid.Columns.Add(CreateTextColumn("limit", "Limit", 22));
        _concurrencyCard.Controls.Add(_concurrencyGrid);
    }

    public override string PageKey => "overview";

    public override string PageTitle => "Dashboard";

    public override async Task RefreshAsync()
    {
        var summaryTask = Client.GetDashboardSummaryAsync();
        var statsTask = Client.GetSystemStatsAsync();
        var fingerprintTask = Client.GetIdentityFingerprintAsync();
        var settingsTasks = _settingCheckboxes.Keys.ToDictionary(
            path => path,
            path => Client.GetBooleanSettingAsync(path),
            StringComparer.OrdinalIgnoreCase);

        await Task.WhenAll(settingsTasks.Values.Cast<Task>().Append(summaryTask).Append(statsTask).Append(fingerprintTask));

        var summary = await summaryTask;
        var stats = await statsTask;
        var fingerprint = await fingerprintTask;

        if (summary?.Kpi is not null)
        {
            _metricValueLabels["requests"].Text = DashboardStyles.FormatCompactNumber(summary.Kpi.TotalRequests);
            _metricDetailLabels["requests"].Text = $"{DashboardStyles.FormatCompactNumber(summary.Kpi.SuccessRequests)} success / {DashboardStyles.FormatCompactNumber(summary.Kpi.FailedRequests)} failed";
            _metricValueLabels["success-rate"].Text = DashboardStyles.FormatPercent(summary.Kpi.SuccessRate);
            _metricDetailLabels["success-rate"].Text = $"{DashboardStyles.FormatCompactNumber(summary.Kpi.TotalRequests)} tracked requests";
            _metricValueLabels["tokens"].Text = DashboardStyles.FormatCompactNumber(summary.Kpi.TotalTokens);
            _metricDetailLabels["tokens"].Text = $"{DashboardStyles.FormatCompactNumber(summary.Kpi.InputTokens)} in / {DashboardStyles.FormatCompactNumber(summary.Kpi.OutputTokens)} out";
            _metricValueLabels["cost"].Text = DashboardStyles.FormatCurrency(summary.Kpi.TotalCost);
            _metricDetailLabels["cost"].Text = $"{DashboardStyles.FormatCompactNumber(summary.Kpi.CachedTokens)} cached tokens";
        }

        if (summary?.Counts is not null)
        {
            _metricValueLabels["auth-files"].Text = DashboardStyles.FormatCompactNumber(summary.Counts.AuthFiles);
            _metricDetailLabels["auth-files"].Text = $"{summary.Counts.ApiKeys} api keys";
            _metricValueLabels["codex"].Text = DashboardStyles.FormatCompactNumber(summary.Counts.CodexKeys);
            _metricDetailLabels["codex"].Text = $"{summary.Counts.GeminiKeys} Gemini / {summary.Counts.ClaudeKeys} Claude";
            _metricValueLabels["providers"].Text = DashboardStyles.FormatCompactNumber(summary.Counts.ProvidersTotal);
            _metricDetailLabels["providers"].Text = $"{summary.Counts.OpenAIProviders} OpenAI providers";
        }

        if (stats is not null)
        {
            _metricValueLabels["in-flight"].Text = DashboardStyles.FormatCompactNumber(stats.TotalInFlight);
            _metricDetailLabels["in-flight"].Text = $"{stats.TotalRpm} RPM / {DashboardStyles.FormatCompactNumber(stats.TotalTpm)} TPM";

            _systemSummaryLabel.Text =
                $"Uptime: {DashboardStyles.FormatDuration(stats.UptimeSeconds)}{Environment.NewLine}" +
                $"Process CPU: {stats.ProcessCpuPct:0.##}%   Process memory: {DashboardStyles.FormatBytes(stats.ProcessMemBytes)} ({stats.ProcessMemPct:0.##}%){Environment.NewLine}" +
                $"System CPU: {stats.SystemCpuPct:0.##}%   System memory: {DashboardStyles.FormatBytes(stats.SystemMemUsed)} / {DashboardStyles.FormatBytes(stats.SystemMemTotal)} ({stats.SystemMemPct:0.##}%){Environment.NewLine}" +
                $"Disk: {DashboardStyles.FormatBytes(stats.DiskUsed)} used / {DashboardStyles.FormatBytes(stats.DiskTotal)} total ({stats.DiskPct:0.##}%){Environment.NewLine}" +
                $"Network: {DashboardStyles.FormatBytes(stats.NetBytesSent)} sent / {DashboardStyles.FormatBytes(stats.NetBytesRecv)} recv{Environment.NewLine}" +
                $"Go runtime: {stats.GoRoutines} routines   Heap: {DashboardStyles.FormatBytes(stats.GoHeapBytes)}";

            _latencyGrid.Rows.Clear();
            foreach (var item in stats.ChannelLatency.OrderByDescending(entry => entry.Count))
            {
                _latencyGrid.Rows.Add(item.Source ?? "-", item.Count.ToString("N0"), item.AvgMs.ToString("0.##"));
            }

            _concurrencyGrid.Rows.Clear();
            foreach (var item in stats.ActiveConcurrency.OrderByDescending(entry => entry.Rpm))
            {
                _concurrencyGrid.Rows.Add(
                    DashboardStyles.Mask(item.ApiKey),
                    item.Rpm.ToString("N0"),
                    DashboardStyles.FormatCompactNumber(item.Tpm),
                    $"{item.RpmLimit:N0}/{DashboardStyles.FormatCompactNumber(item.TpmLimit)}");
            }
        }

        var codexFingerprint = fingerprint?.IdentityFingerprint?.Codex;
        _fingerprintLabel.Text =
            $"Codex fingerprint: {(codexFingerprint?.Enabled == true ? "enabled" : "disabled")}{Environment.NewLine}" +
            $"User-Agent: {codexFingerprint?.UserAgent ?? "-"}{Environment.NewLine}" +
            $"Session mode: {codexFingerprint?.SessionMode ?? "-"}";

        _runtimeSummaryLabel.Text =
            $"Connected base URL: {Client.BaseUrl}{Environment.NewLine}" +
            $"Generated at: {summary?.Meta?.GeneratedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "-"}{Environment.NewLine}" +
            $"Dashboard window: {summary?.Days ?? 7} day(s)";

        _generatedLabel.Text = $"Last sync: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

        _updatingToggleValues = true;
        try
        {
            foreach (var pair in settingsTasks)
            {
                _settingCheckboxes[pair.Key].Checked = (await pair.Value)?.GetFirstBoolean() ?? false;
            }
        }
        finally
        {
            _updatingToggleValues = false;
        }
    }

    protected override void ApplyThemeCore()
    {
        BackColor = Palette.WindowBackground;

        foreach (var card in _cards)
        {
            DashboardStyles.ApplyCardStyle(card, Palette);
        }

        foreach (var title in _metricTitleLabels.Values)
        {
            title.ForeColor = Palette.TextSecondary;
        }

        foreach (var value in _metricValueLabels.Values)
        {
            value.ForeColor = Palette.TextPrimary;
        }

        foreach (var detail in _metricDetailLabels.Values)
        {
            detail.ForeColor = Palette.TextTertiary;
        }

        _runtimeTitleLabel.ForeColor = Palette.TextPrimary;
        _runtimeSummaryLabel.ForeColor = Palette.TextSecondary;
        _fingerprintLabel.ForeColor = Palette.TextSecondary;
        _generatedLabel.ForeColor = Palette.TextTertiary;
        _systemTitleLabel.ForeColor = Palette.TextPrimary;
        _systemSummaryLabel.ForeColor = Palette.TextSecondary;
        _latencyTitleLabel.ForeColor = Palette.TextPrimary;
        _concurrencyTitleLabel.ForeColor = Palette.TextPrimary;

        foreach (var checkbox in _settingCheckboxes.Values)
        {
            DashboardStyles.StyleCheckBox(checkbox, Palette);
        }

        DashboardStyles.StyleDataGridView(_latencyGrid, Palette);
        DashboardStyles.StyleDataGridView(_concurrencyGrid, Palette);
    }

    private void AddMetricCard(string key, string title, string value, string detail)
    {
        var card = new RoundedSurfacePanel
        {
            Size = new Size(250, 118),
            CornerRadius = 24,
            BorderWidth = 1,
            Padding = new Padding(18, 16, 18, 14),
            Margin = new Padding(0, 0, 14, 14)
        };
        _cards.Add(card);
        _metricFlow.Controls.Add(card);

        var titleLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            Text = title,
            Font = DashboardStyles.CreateFont(8.75f, FontStyle.Bold)
        };
        card.Controls.Add(titleLabel);

        var detailLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 20,
            Text = detail,
            Font = DashboardStyles.CreateFont(8.5f, FontStyle.Regular)
        };
        card.Controls.Add(detailLabel);

        var valueLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = value,
            Font = DashboardStyles.CreateFont(21f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        card.Controls.Add(valueLabel);

        _metricTitleLabels[key] = titleLabel;
        _metricValueLabels[key] = valueLabel;
        _metricDetailLabels[key] = detailLabel;
    }

    private RoundedSurfacePanel CreateSectionCard()
    {
        var card = new RoundedSurfacePanel
        {
            Dock = DockStyle.Top,
            Height = 308,
            CornerRadius = 28,
            BorderWidth = 1,
            Padding = new Padding(22, 18, 22, 18),
            Margin = new Padding(0, 0, 16, 16)
        };
        _cards.Add(card);
        return card;
    }

    private static Label CreateSectionTitle(string text)
    {
        return new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = text,
            Font = DashboardStyles.CreateFont(12f, FontStyle.Bold)
        };
    }

    private static Label CreateBodyLabel(string text)
    {
        return new Label
        {
            AutoSize = false,
            Text = text,
            Font = DashboardStyles.CreateFont(9.25f, FontStyle.Regular)
        };
    }

    private static Label CreateMutedLabel(string text)
    {
        return new Label
        {
            AutoSize = false,
            Text = text,
            Font = DashboardStyles.CreateFont(8.5f, FontStyle.Regular)
        };
    }

    private void AddSettingToggle(TableLayoutPanel layout, int column, int row, string path, string title)
    {
        while (layout.RowCount <= row)
        {
            layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        }

        var checkBox = new CheckBox
        {
            Dock = DockStyle.Fill,
            Text = title,
            Margin = new Padding(0, 0, 10, 8)
        };
        checkBox.CheckedChanged += async (_, _) => await HandleToggleChangedAsync(path, checkBox);
        layout.Controls.Add(checkBox, column, row);
        _settingCheckboxes[path] = checkBox;
    }

    private async Task HandleToggleChangedAsync(string path, CheckBox checkBox)
    {
        if (_updatingToggleValues)
        {
            return;
        }

        var targetValue = checkBox.Checked;
        try
        {
            await Client.PutBooleanSettingAsync(path, targetValue);
            ReportStatus($"{checkBox.Text} {(targetValue ? "enabled" : "disabled")}.", false);
        }
        catch (Exception ex)
        {
            _updatingToggleValues = true;
            try
            {
                checkBox.Checked = !targetValue;
            }
            finally
            {
                _updatingToggleValues = false;
            }

            ReportStatus(ex.Message, true);
        }
    }

    private static DataGridView CreateGrid()
    {
        return new DataGridView
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 14, 0, 0)
        };
    }

    private static DataGridViewTextBoxColumn CreateTextColumn(string name, string header, float fillWeight)
    {
        return new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            FillWeight = fillWeight,
            ReadOnly = true
        };
    }
}
