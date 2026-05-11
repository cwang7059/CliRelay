namespace CliRelay.CodexSwitchDashboard;

internal sealed class ModelsPage : ManagementPageBase
{
    private readonly List<RoundedSurfacePanel> _cards = [];
    private readonly Label _summaryLabel;
    private readonly Label _pricingHintLabel;
    private readonly DataGridView _modelsGrid;

    public ModelsPage(ManagementApiClient client, Action<string, bool> setStatus)
        : base(client, setStatus)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        Controls.Add(root);

        var heroCard = CreateCard(28, new Padding(0, 0, 0, 16));
        root.Controls.Add(heroCard, 0, 0);

        var titleLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 30,
            Text = "Model catalog",
            Font = DashboardStyles.CreateFont(12.5f, FontStyle.Bold)
        };
        heroCard.Controls.Add(titleLabel);

        _summaryLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Refresh to fetch the proxied model list and pricing metadata.",
            Font = DashboardStyles.CreateFont(9.25f, FontStyle.Regular)
        };
        heroCard.Controls.Add(_summaryLabel);

        _pricingHintLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 22,
            Text = "Pricing is shown per million tokens when provided by the management API.",
            Font = DashboardStyles.CreateFont(8.5f, FontStyle.Regular)
        };
        heroCard.Controls.Add(_pricingHintLabel);

        var gridCard = CreateCard(30, new Padding(0));
        root.Controls.Add(gridCard, 0, 1);

        var gridTitle = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = "Available models",
            Font = DashboardStyles.CreateFont(11.75f, FontStyle.Bold)
        };
        gridCard.Controls.Add(gridTitle);

        _modelsGrid = new DataGridView
        {
            Dock = DockStyle.Fill
        };
        _modelsGrid.Columns.Add(CreateColumn("id", "Model", 34));
        _modelsGrid.Columns.Add(CreateColumn("owner", "Owner", 14));
        _modelsGrid.Columns.Add(CreateColumn("object", "Object", 12));
        _modelsGrid.Columns.Add(CreateColumn("input", "Input / M", 13));
        _modelsGrid.Columns.Add(CreateColumn("output", "Output / M", 13));
        _modelsGrid.Columns.Add(CreateColumn("cached", "Cached / M", 14));
        gridCard.Controls.Add(_modelsGrid);
    }

    public override string PageKey => "models";

    public override string PageTitle => "Models";

    public override async Task RefreshAsync()
    {
        var response = await Client.GetModelsAsync();
        var models = response?.Data.OrderBy(model => model.Id).ToList() ?? [];

        _modelsGrid.Rows.Clear();
        foreach (var model in models)
        {
            _modelsGrid.Rows.Add(
                model.Id ?? "-",
                model.OwnedBy ?? "-",
                model.Object ?? "-",
                FormatPrice(model.Pricing?.InputPricePerMillion),
                FormatPrice(model.Pricing?.OutputPricePerMillion),
                FormatPrice(model.Pricing?.CachedPricePerMillion));
        }

        var providerCount = models
            .Select(model => model.OwnedBy ?? "-")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        _summaryLabel.Text = $"{models.Count} model(s) loaded across {providerCount} owner bucket(s).";
    }

    protected override void ApplyThemeCore()
    {
        BackColor = Palette.WindowBackground;
        foreach (var card in _cards)
        {
            DashboardStyles.ApplyCardStyle(card, Palette);
        }

        DashboardStyles.StyleDataGridView(_modelsGrid, Palette);

        foreach (var label in Controls.OfType<Control>().SelectMany(EnumerateLabels))
        {
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

    private static DataGridViewTextBoxColumn CreateColumn(string name, string header, float fillWeight)
    {
        return new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            FillWeight = fillWeight
        };
    }

    private static string FormatPrice(double? value)
    {
        return value.HasValue ? $"${value.Value:0.####}" : "-";
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
