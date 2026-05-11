namespace CliRelay.CodexSwitchDashboard;

internal sealed class LogsPage : ManagementPageBase
{
    private readonly List<RoundedSurfacePanel> _cards = [];
    private readonly NumericUpDown _daysInput;
    private readonly NumericUpDown _sizeInput;
    private readonly Label _usageSummaryLabel;
    private readonly Label _systemSummaryLabel;
    private readonly DataGridView _usageGrid;
    private readonly TextBox _systemLogsTextBox;

    public LogsPage(ManagementApiClient client, Action<string, bool> setStatus)
        : base(client, setStatus)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 98));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 58f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 42f));
        Controls.Add(root);

        var filtersCard = CreateCard(28, new Padding(0, 0, 0, 16));
        root.Controls.Add(filtersCard, 0, 0);

        var filtersLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            BackColor = Color.Transparent
        };
        filtersLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        filtersLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        filtersLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        filtersLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        filtersLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        filtersLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        filtersCard.Controls.Add(filtersLayout);

        filtersLayout.Controls.Add(CreateLabel("Days"), 0, 0);
        _daysInput = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Minimum = 1,
            Maximum = 90,
            Value = 7,
            Margin = new Padding(0, 18, 12, 18)
        };
        filtersLayout.Controls.Add(_daysInput, 1, 0);

        filtersLayout.Controls.Add(CreateLabel("Rows"), 2, 0);
        _sizeInput = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Minimum = 10,
            Maximum = 500,
            Increment = 10,
            Value = 80,
            Margin = new Padding(0, 18, 12, 18)
        };
        filtersLayout.Controls.Add(_sizeInput, 3, 0);

        _usageSummaryLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "Usage metrics will be summarized here after refresh.",
            Font = DashboardStyles.CreateFont(9.25f, FontStyle.Regular)
        };
        filtersLayout.Controls.Add(_usageSummaryLabel, 4, 0);

        _systemSummaryLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Text = "System log tail will be summarized here.",
            Font = DashboardStyles.CreateFont(8.75f, FontStyle.Regular)
        };
        filtersLayout.Controls.Add(_systemSummaryLabel, 5, 0);

        var usageCard = CreateCard(30, new Padding(0, 0, 0, 16));
        root.Controls.Add(usageCard, 0, 1);

        var usageTitle = new Label
        {
            Dock = DockStyle.Top,
            Height = 30,
            Text = "Usage request logs",
            Font = DashboardStyles.CreateFont(11.75f, FontStyle.Bold)
        };
        usageCard.Controls.Add(usageTitle);

        _usageGrid = new DataGridView
        {
            Dock = DockStyle.Fill
        };
        _usageGrid.Columns.Add(CreateColumn("time", "Timestamp", 18));
        _usageGrid.Columns.Add(CreateColumn("model", "Model", 18));
        _usageGrid.Columns.Add(CreateColumn("source", "Source", 10));
        _usageGrid.Columns.Add(CreateColumn("channel", "Channel", 10));
        _usageGrid.Columns.Add(CreateColumn("status", "Status", 8));
        _usageGrid.Columns.Add(CreateColumn("latency", "Latency", 8));
        _usageGrid.Columns.Add(CreateColumn("tokens", "Tokens", 10));
        _usageGrid.Columns.Add(CreateColumn("cost", "Cost", 8));
        _usageGrid.Columns.Add(CreateColumn("auth", "Auth", 10));
        usageCard.Controls.Add(_usageGrid);

        var systemCard = CreateCard(30, new Padding(0));
        root.Controls.Add(systemCard, 0, 2);

        var systemTitle = new Label
        {
            Dock = DockStyle.Top,
            Height = 30,
            Text = "System logs",
            Font = DashboardStyles.CreateFont(11.75f, FontStyle.Bold)
        };
        systemCard.Controls.Add(systemTitle);

        _systemLogsTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9f, FontStyle.Regular, GraphicsUnit.Point)
        };
        systemCard.Controls.Add(_systemLogsTextBox);
    }

    public override string PageKey => "logs";

    public override string PageTitle => "Logs";

    public override async Task RefreshAsync()
    {
        var days = (int)_daysInput.Value;
        var size = (int)_sizeInput.Value;

        var usageTask = Client.GetUsageLogsAsync(days, size);
        var logsTask = Client.GetSystemLogsAsync(size);
        await Task.WhenAll(usageTask, logsTask);

        var usage = await usageTask;
        var logs = await logsTask;

        _usageGrid.Rows.Clear();
        foreach (var item in usage?.Items ?? [])
        {
            _usageGrid.Rows.Add(
                item.Timestamp?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "-",
                item.Model ?? "-",
                item.Source ?? "-",
                item.ChannelName ?? "-",
                item.Failed ? "Failed" : "OK",
                $"{item.LatencyMs:N0} ms",
                DashboardStyles.FormatCompactNumber(item.TotalTokens),
                DashboardStyles.FormatCurrency(item.Cost),
                item.AuthIndex ?? "-");
        }

        _usageSummaryLabel.Text = usage?.Stats is null
            ? "No usage metrics returned."
            : $"{usage.Total:N0} rows, {DashboardStyles.FormatPercent(usage.Stats.SuccessRate)} success, {DashboardStyles.FormatCompactNumber(usage.Stats.TotalTokens)} tokens, {DashboardStyles.FormatCurrency(usage.Stats.TotalCost)} total.";

        _systemLogsTextBox.Text = string.Join(Environment.NewLine, logs?.Lines ?? []);
        _systemSummaryLabel.Text = logs is null
            ? "No system logs returned."
            : $"{logs.LineCount:N0} line(s), latest timestamp: {logs.LatestTimestamp}.";
    }

    protected override void ApplyThemeCore()
    {
        BackColor = Palette.WindowBackground;

        foreach (var card in _cards)
        {
            DashboardStyles.ApplyCardStyle(card, Palette);
        }

        DashboardStyles.StyleDataGridView(_usageGrid, Palette);

        _daysInput.BackColor = Palette.SurfaceAlt;
        _daysInput.ForeColor = Palette.TextPrimary;
        _sizeInput.BackColor = Palette.SurfaceAlt;
        _sizeInput.ForeColor = Palette.TextPrimary;
        _usageSummaryLabel.ForeColor = Palette.TextSecondary;
        _systemSummaryLabel.ForeColor = Palette.TextTertiary;
        _systemLogsTextBox.BackColor = Palette.SurfaceAlt;
        _systemLogsTextBox.ForeColor = Palette.TextPrimary;

        foreach (var label in Controls.OfType<Control>().SelectMany(EnumerateLabels))
        {
            if (label == _usageSummaryLabel || label == _systemSummaryLabel)
            {
                continue;
            }

            label.ForeColor = label.Font.Bold ? Palette.TextPrimary : Palette.TextSecondary;
        }
    }

    private RoundedSurfacePanel CreateCard(int radius, Padding margin)
    {
        var card = new RoundedSurfacePanel
        {
            Dock = DockStyle.Fill,
            CornerRadius = radius,
            BorderWidth = 1,
            Padding = new Padding(22, 18, 22, 18),
            Margin = margin
        };
        _cards.Add(card);
        return card;
    }

    private static Label CreateLabel(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = DashboardStyles.CreateFont(9f, FontStyle.Bold)
        };
    }

    private static DataGridViewTextBoxColumn CreateColumn(string name, string header, float fillWeight)
    {
        return new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            FillWeight = fillWeight
        };
    }

    private static IEnumerable<Label> EnumerateLabels(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is Label label)
            {
                yield return label;
            }

            foreach (var nested in EnumerateLabels(child))
            {
                yield return nested;
            }
        }
    }
}
