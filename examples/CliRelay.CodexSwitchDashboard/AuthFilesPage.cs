namespace CliRelay.CodexSwitchDashboard;

internal sealed class AuthFilesPage : ManagementPageBase
{
    private readonly List<RoundedSurfacePanel> _cards = [];
    private readonly DataGridView _authGrid;
    private readonly Label _summaryLabel;
    private readonly Label _selectionTitleLabel;
    private readonly Label _selectionMetaLabel;
    private readonly Label _selectionStatusLabel;
    private readonly TextBox _labelTextBox;
    private readonly TextBox _prefixTextBox;
    private readonly TextBox _proxyUrlTextBox;
    private readonly TextBox _proxyIdTextBox;
    private readonly TextBox _restrictionsTextBox;
    private readonly TextBox _rawJsonTextBox;
    private readonly Button _saveFieldsButton;
    private readonly Button _toggleDisabledButton;
    private readonly Button _reconcileButton;
    private readonly Button _copyJsonButton;

    private List<AuthFileEntry> _files = [];
    private string? _selectedName;
    private int _selectionVersion;

    public AuthFilesPage(ManagementApiClient client, Action<string, bool> setStatus)
        : base(client, setStatus)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        Controls.Add(root);

        var summaryCard = CreateCard(26, new Padding(0, 0, 0, 16));
        root.Controls.Add(summaryCard, 0, 0);

        var summaryTitle = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = "Auth files",
            Font = DashboardStyles.CreateFont(12.5f, FontStyle.Bold)
        };
        summaryCard.Controls.Add(summaryTitle);

        _summaryLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Refresh to inspect managed accounts, restrictions and runtime health.",
            Font = DashboardStyles.CreateFont(9.25f, FontStyle.Regular)
        };
        summaryCard.Controls.Add(_summaryLabel);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.None,
            BackColor = Color.Transparent,
            SplitterDistance = 680,
            SplitterWidth = 8
        };
        root.Controls.Add(split, 0, 1);

        var listCard = CreateCard(28, new Padding(0));
        split.Panel1.Controls.Add(listCard);

        var listTitle = new Label
        {
            Dock = DockStyle.Top,
            Height = 30,
            Text = "Managed auth inventory",
            Font = DashboardStyles.CreateFont(11.75f, FontStyle.Bold)
        };
        listCard.Controls.Add(listTitle);

        _authGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 14, 0, 0)
        };
        _authGrid.Columns.Add(CreateTextColumn("name", "Name", 30));
        _authGrid.Columns.Add(CreateTextColumn("provider", "Provider", 12));
        _authGrid.Columns.Add(CreateTextColumn("status", "Status", 14));
        _authGrid.Columns.Add(CreateTextColumn("plan", "Plan", 12));
        _authGrid.Columns.Add(CreateTextColumn("disabled", "Disabled", 10));
        _authGrid.Columns.Add(CreateTextColumn("updated", "Updated", 12));
        _authGrid.SelectionChanged += async (_, _) => await HandleSelectionChangedAsync();
        listCard.Controls.Add(_authGrid);

        var detailCard = CreateCard(28, new Padding(0));
        split.Panel2.Controls.Add(detailCard);

        var detailLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            BackColor = Color.Transparent
        };
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        detailCard.Controls.Add(detailLayout);

        var selectionHeader = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };
        detailLayout.Controls.Add(selectionHeader, 0, 0);

        _selectionTitleLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = "Select an auth file",
            Font = DashboardStyles.CreateFont(12.5f, FontStyle.Bold)
        };
        selectionHeader.Controls.Add(_selectionTitleLabel);

        _selectionMetaLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 26,
            Text = "Provider, account and path details will appear here.",
            Font = DashboardStyles.CreateFont(8.75f, FontStyle.Regular)
        };
        selectionHeader.Controls.Add(_selectionMetaLabel);

        _selectionStatusLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "No auth selected.",
            Font = DashboardStyles.CreateFont(9f, FontStyle.Regular)
        };
        selectionHeader.Controls.Add(_selectionStatusLabel);

        var fieldGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            BackColor = Color.Transparent
        };
        fieldGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        fieldGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        fieldGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        fieldGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        fieldGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        fieldGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        detailLayout.Controls.Add(fieldGrid, 0, 1);

        fieldGrid.Controls.Add(CreateFieldLabel("Label"), 0, 0);
        fieldGrid.Controls.Add(CreateFieldLabel("Prefix"), 1, 0);
        _labelTextBox = CreateEditorTextBox();
        _prefixTextBox = CreateEditorTextBox();
        fieldGrid.Controls.Add(_labelTextBox, 0, 1);
        fieldGrid.Controls.Add(_prefixTextBox, 1, 1);

        fieldGrid.Controls.Add(CreateFieldLabel("Proxy URL"), 0, 2);
        fieldGrid.Controls.Add(CreateFieldLabel("Proxy ID"), 1, 2);
        _proxyUrlTextBox = CreateEditorTextBox();
        _proxyIdTextBox = CreateEditorTextBox();
        fieldGrid.Controls.Add(_proxyUrlTextBox, 0, 3);
        fieldGrid.Controls.Add(_proxyIdTextBox, 1, 3);

        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        detailLayout.Controls.Add(buttonRow, 0, 2);

        _saveFieldsButton = CreateActionButton("Save fields");
        _saveFieldsButton.Click += async (_, _) => await SaveSelectedFieldsAsync();
        buttonRow.Controls.Add(_saveFieldsButton);

        _toggleDisabledButton = CreateActionButton("Disable");
        _toggleDisabledButton.Click += async (_, _) => await ToggleSelectedStatusAsync();
        buttonRow.Controls.Add(_toggleDisabledButton);

        _reconcileButton = CreateActionButton("Reconcile quota");
        _reconcileButton.Click += async (_, _) => await ReconcileSelectedQuotaAsync();
        buttonRow.Controls.Add(_reconcileButton);

        _copyJsonButton = CreateActionButton("Copy JSON");
        _copyJsonButton.Click += (_, _) =>
        {
            if (_rawJsonTextBox is { } rawJsonBox && !string.IsNullOrWhiteSpace(rawJsonBox.Text))
            {
                Clipboard.SetText(rawJsonBox.Text);
                ReportStatus("Auth JSON copied to clipboard.", false);
            }
        };
        buttonRow.Controls.Add(_copyJsonButton);

        detailLayout.Controls.Add(CreateSectionLabel("Restrictions"), 0, 3);

        _restrictionsTextBox = CreateLargeReadOnlyBox();
        detailLayout.Controls.Add(_restrictionsTextBox, 0, 4);

        detailLayout.Controls.Add(CreateSectionLabel("Raw auth JSON"), 0, 5);

        _rawJsonTextBox = CreateLargeReadOnlyBox();
        detailLayout.Controls.Add(_rawJsonTextBox, 0, 6);
    }

    public override string PageKey => "auth-files";

    public override string PageTitle => "Auth Files";

    public override async Task RefreshAsync()
    {
        var response = await Client.GetAuthFilesAsync();
        _files = response?.Files
            .OrderBy(entry => entry.Disabled)
            .ThenBy(entry => entry.Provider)
            .ThenBy(entry => entry.Name)
            .ToList() ?? [];

        _authGrid.Rows.Clear();
        foreach (var file in _files)
        {
            _authGrid.Rows.Add(
                file.Name ?? "-",
                file.Provider ?? "-",
                file.Status ?? "-",
                file.PlanType ?? "-",
                file.Disabled ? "Yes" : "No",
                DashboardStyles.FormatRelative(file.UpdatedAt ?? file.Modtime));
        }

        var disabledCount = _files.Count(entry => entry.Disabled);
        var unavailableCount = _files.Count(entry => entry.Unavailable);
        var runtimeOnlyCount = _files.Count(entry => entry.RuntimeOnly);
        _summaryLabel.Text = $"{_files.Count} auth file(s) total, {disabledCount} disabled, {unavailableCount} unavailable, {runtimeOnlyCount} runtime-only.";

        if (_files.Count == 0)
        {
            ClearSelectionState();
            return;
        }

        var index = _files.FindIndex(entry => string.Equals(entry.Name, _selectedName, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            index = 0;
        }

        _authGrid.ClearSelection();
        if (index >= 0 && index < _authGrid.Rows.Count)
        {
            _authGrid.Rows[index].Selected = true;
            _authGrid.CurrentCell = _authGrid.Rows[index].Cells[0];
        }

        await PopulateSelectionAsync(_files[index]);
    }

    protected override void ApplyThemeCore()
    {
        BackColor = Palette.WindowBackground;
        foreach (var card in _cards)
        {
            DashboardStyles.ApplyCardStyle(card, Palette);
        }

        DashboardStyles.StyleDataGridView(_authGrid, Palette);
        ApplyEditorTheme(_labelTextBox);
        ApplyEditorTheme(_prefixTextBox);
        ApplyEditorTheme(_proxyUrlTextBox);
        ApplyEditorTheme(_proxyIdTextBox);
        ApplyReadOnlyTheme(_restrictionsTextBox);
        ApplyReadOnlyTheme(_rawJsonTextBox);

        foreach (var label in Controls.OfType<Control>().SelectMany(EnumerateLabels))
        {
            if (label.Font.Bold)
            {
                label.ForeColor = Palette.TextPrimary;
            }
            else
            {
                label.ForeColor = Palette.TextSecondary;
            }
        }

        DashboardStyles.StyleSecondaryButton(_saveFieldsButton, Palette);
        DashboardStyles.StyleSecondaryButton(_toggleDisabledButton, Palette);
        DashboardStyles.StyleSecondaryButton(_reconcileButton, Palette);
        DashboardStyles.StyleSecondaryButton(_copyJsonButton, Palette);
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

    private static Label CreateFieldLabel(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            TextAlign = ContentAlignment.BottomLeft,
            Font = DashboardStyles.CreateFont(8.75f, FontStyle.Bold)
        };
    }

    private static Label CreateSectionLabel(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = DashboardStyles.CreateFont(9f, FontStyle.Bold)
        };
    }

    private static TextBox CreateEditorTextBox()
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            Font = DashboardStyles.CreateFont(9.25f, FontStyle.Regular),
            Margin = new Padding(0, 0, 12, 12)
        };
    }

    private static TextBox CreateLargeReadOnlyBox()
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9f, FontStyle.Regular, GraphicsUnit.Point)
        };
    }

    private static Button CreateActionButton(string text)
    {
        return new Button
        {
            Text = text,
            Width = 114,
            Height = 36,
            Margin = new Padding(0, 0, 10, 0)
        };
    }

    private static DataGridViewTextBoxColumn CreateTextColumn(string name, string header, float fillWeight)
    {
        return new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            FillWeight = fillWeight
        };
    }

    private void ApplyEditorTheme(TextBox textBox)
    {
        textBox.BackColor = Palette.SurfaceAlt;
        textBox.ForeColor = Palette.TextPrimary;
    }

    private void ApplyReadOnlyTheme(TextBox textBox)
    {
        textBox.BackColor = Palette.SurfaceAlt;
        textBox.ForeColor = Palette.TextPrimary;
    }

    private async Task HandleSelectionChangedAsync()
    {
        if (_authGrid.SelectedRows.Count == 0)
        {
            return;
        }

        var name = _authGrid.SelectedRows[0].Cells[0].Value?.ToString();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var selected = _files.FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
        if (selected is null)
        {
            return;
        }

        await PopulateSelectionAsync(selected);
    }

    private async Task PopulateSelectionAsync(AuthFileEntry file)
    {
        _selectedName = file.Name;
        _selectionVersion++;
        var currentVersion = _selectionVersion;

        _selectionTitleLabel.Text = file.Label ?? file.Name ?? "Auth file";
        _selectionMetaLabel.Text = $"{file.Provider ?? "-"}  |  {file.Type ?? "-"}  |  {file.Email ?? file.Account ?? "-"}";
        _selectionStatusLabel.Text =
            $"Status: {file.Status ?? "-"}   Plan: {file.PlanType ?? "-"}   Disabled: {(file.Disabled ? "Yes" : "No")}{Environment.NewLine}" +
            $"Tags: {string.Join(", ", file.DisplayTags.DefaultIfEmpty("-"))}{Environment.NewLine}" +
            $"Path: {file.Path ?? "-"}{Environment.NewLine}" +
            $"Updated: {(file.UpdatedAt ?? file.Modtime)?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "-"}";

        _labelTextBox.Text = file.Label ?? string.Empty;
        _prefixTextBox.Text = string.Empty;
        _proxyUrlTextBox.Text = string.Empty;
        _proxyIdTextBox.Text = string.Empty;
        _toggleDisabledButton.Text = file.Disabled ? "Enable" : "Disable";

        if (file.Restrictions.Count == 0)
        {
            _restrictionsTextBox.Text = "No active restrictions.";
        }
        else
        {
            _restrictionsTextBox.Text = string.Join(
                Environment.NewLine + Environment.NewLine,
                file.Restrictions.Select(
                    restriction =>
                        $"Scope: {restriction.Scope ?? "-"}{Environment.NewLine}" +
                        $"Model: {restriction.Model ?? "-"}{Environment.NewLine}" +
                        $"Status: {restriction.Status ?? "-"} ({restriction.Code ?? "-"}){Environment.NewLine}" +
                        $"HTTP: {(restriction.HttpStatus?.ToString() ?? "-")}   Unavailable: {(restriction.Unavailable ? "Yes" : "No")}   Quota: {(restriction.QuotaExceeded ? "Yes" : "No")}{Environment.NewLine}" +
                        $"Reason: {restriction.Reason ?? restriction.StatusMessage ?? "-"}"));
        }

        _rawJsonTextBox.Text = "Loading raw auth JSON...";
        try
        {
            if (string.IsNullOrWhiteSpace(file.Name))
            {
                _rawJsonTextBox.Text = "This auth entry does not expose a downloadable file name.";
                return;
            }

            var rawJson = await Client.DownloadAuthFileJsonAsync(file.Name);
            if (currentVersion == _selectionVersion)
            {
                _rawJsonTextBox.Text = rawJson;
            }
        }
        catch (Exception ex)
        {
            if (currentVersion == _selectionVersion)
            {
                _rawJsonTextBox.Text = ex.Message;
            }
        }
    }

    private async Task SaveSelectedFieldsAsync()
    {
        var selected = GetSelectedFile();
        if (selected is null)
        {
            ReportStatus("Select an auth file first.", true);
            return;
        }

        await Client.PatchAuthFieldsAsync(new AuthFieldPatchRequest
        {
            Name = selected.Name ?? string.Empty,
            Label = NullIfWhiteSpace(_labelTextBox.Text),
            Prefix = NullIfWhiteSpace(_prefixTextBox.Text),
            ProxyUrl = NullIfWhiteSpace(_proxyUrlTextBox.Text),
            ProxyId = NullIfWhiteSpace(_proxyIdTextBox.Text)
        });

        ReportStatus($"Saved fields for {selected.Name}.", false);
        await RefreshAsync();
    }

    private async Task ToggleSelectedStatusAsync()
    {
        var selected = GetSelectedFile();
        if (selected is null)
        {
            ReportStatus("Select an auth file first.", true);
            return;
        }

        await Client.PatchAuthStatusAsync(selected.Name ?? string.Empty, !selected.Disabled);
        ReportStatus($"{selected.Name} {(selected.Disabled ? "enabled" : "disabled")}.", false);
        await RefreshAsync();
    }

    private async Task ReconcileSelectedQuotaAsync()
    {
        var selected = GetSelectedFile();
        if (selected is null)
        {
            ReportStatus("Select an auth file first.", true);
            return;
        }

        if (string.IsNullOrWhiteSpace(selected.AuthIndex))
        {
            ReportStatus("Selected auth does not expose an auth_index.", true);
            return;
        }

        await Client.ReconcileQuotaAsync(selected.AuthIndex);
        ReportStatus($"Quota reconcile triggered for auth {selected.AuthIndex}.", false);
    }

    private AuthFileEntry? GetSelectedFile()
    {
        if (string.IsNullOrWhiteSpace(_selectedName))
        {
            return null;
        }

        return _files.FirstOrDefault(entry => string.Equals(entry.Name, _selectedName, StringComparison.OrdinalIgnoreCase));
    }

    private void ClearSelectionState()
    {
        _selectedName = null;
        _selectionTitleLabel.Text = "No auth files available";
        _selectionMetaLabel.Text = "The current auth directory is empty.";
        _selectionStatusLabel.Text = "Import or create auth files, then refresh this view.";
        _labelTextBox.Clear();
        _prefixTextBox.Clear();
        _proxyUrlTextBox.Clear();
        _proxyIdTextBox.Clear();
        _restrictionsTextBox.Text = string.Empty;
        _rawJsonTextBox.Text = string.Empty;
    }

    private static string? NullIfWhiteSpace(string value)
    {
        value = value.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
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
