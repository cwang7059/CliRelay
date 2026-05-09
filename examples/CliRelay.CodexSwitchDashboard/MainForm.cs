using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace CliRelay.CodexSwitchDashboard;

public sealed class MainForm : Form
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private readonly TextBox _baseUrlTextBox = new();
    private readonly Button _refreshButton = new();
    private readonly Button _reconcileButton = new();
    private readonly Button _openManageButton = new();
    private readonly CheckBox _onlyCodexCheckBox = new();
    private readonly Label _quotaSwitchProjectLabel = new();
    private readonly Label _quotaSwitchPreviewLabel = new();
    private readonly Label _fingerprintLabel = new();
    private readonly Label _statusLabel = new();
    private readonly DataGridView _grid = new();
    private readonly TextBox _detailsTextBox = new();

    private IReadOnlyList<AuthFileEntry> _allEntries = Array.Empty<AuthFileEntry>();

    public MainForm()
    {
        Text = "CliRelay Codex Switch Dashboard";
        MinimumSize = new Size(1180, 760);
        StartPosition = FormStartPosition.CenterScreen;

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12)
        };
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        Controls.Add(shell);

        shell.Controls.Add(BuildToolbar(), 0, 0);
        shell.Controls.Add(BuildSummaryPanel(), 0, 1);
        shell.Controls.Add(BuildGridPanel(), 0, 2);
        shell.Controls.Add(BuildDetailsPanel(), 0, 3);

        _refreshButton.Click += async (_, _) => await RefreshDashboardAsync();
        _reconcileButton.Click += async (_, _) => await ReconcileSelectedAsync();
        _openManageButton.Click += (_, _) => OpenManagePage();
        _onlyCodexCheckBox.CheckedChanged += (_, _) => RebindGrid();
        _grid.SelectionChanged += (_, _) => ShowSelectedDetails();

        Load += async (_, _) => await RefreshDashboardAsync();
        FormClosed += (_, _) => _httpClient.Dispose();
    }

    private Control BuildToolbar()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 7,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var endpointLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Text = "Management Base URL"
        };

        _baseUrlTextBox.Dock = DockStyle.Fill;
        _baseUrlTextBox.Text = "http://127.0.0.1:8317";

        _refreshButton.Text = "Refresh";
        _refreshButton.AutoSize = true;

        _reconcileButton.Text = "Reconcile Selected";
        _reconcileButton.AutoSize = true;

        _openManageButton.Text = "Open /manage";
        _openManageButton.AutoSize = true;

        _onlyCodexCheckBox.Text = "Only Codex";
        _onlyCodexCheckBox.Checked = true;
        _onlyCodexCheckBox.AutoSize = true;
        _onlyCodexCheckBox.Anchor = AnchorStyles.Left;

        panel.Controls.Add(endpointLabel, 0, 0);
        panel.Controls.Add(_baseUrlTextBox, 1, 0);
        panel.Controls.Add(_onlyCodexCheckBox, 2, 0);
        panel.Controls.Add(_refreshButton, 3, 0);
        panel.Controls.Add(_reconcileButton, 4, 0);
        panel.Controls.Add(_openManageButton, 5, 0);
        panel.Controls.Add(_statusLabel, 6, 0);

        _statusLabel.AutoSize = true;
        _statusLabel.Anchor = AnchorStyles.Left;
        _statusLabel.ForeColor = Color.DimGray;
        _statusLabel.Text = "Idle";

        return panel;
    }

    private Control BuildSummaryPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(0, 10, 0, 8)
        };

        ConfigureSummaryLabel(_quotaSwitchProjectLabel, "Quota switch-project: --");
        ConfigureSummaryLabel(_quotaSwitchPreviewLabel, "Quota switch-preview-model: --");
        ConfigureSummaryLabel(_fingerprintLabel, "Codex fingerprint: --");

        panel.Controls.Add(_quotaSwitchProjectLabel);
        panel.Controls.Add(_quotaSwitchPreviewLabel);
        panel.Controls.Add(_fingerprintLabel);
        return panel;
    }

    private static void ConfigureSummaryLabel(Label label, string text)
    {
        label.AutoSize = true;
        label.Margin = new Padding(0, 0, 18, 6);
        label.Padding = new Padding(10, 8, 10, 8);
        label.BackColor = Color.FromArgb(245, 247, 250);
        label.Text = text;
    }

    private Control BuildGridPanel()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.BackgroundColor = Color.White;

        AddColumn("AuthIndex", "Auth Index", 110);
        AddColumn("Label", "Label", 180);
        AddColumn("Provider", "Provider", 90);
        AddColumn("PlanType", "Plan", 80);
        AddColumn("Status", "Status", 90);
        AddColumn("Availability", "Availability", 110);
        AddColumn("Account", "Account / Email", 210);
        AddColumn("RetryAt", "Next Retry", 140);
        AddColumn("Restrictions", "Restrictions", 280);

        return _grid;
    }

    private Control BuildDetailsPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 10, 0, 0)
        };

        var title = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 26,
            Text = "Selected auth JSON",
            Font = new Font(Font, FontStyle.Bold)
        };

        _detailsTextBox.Dock = DockStyle.Fill;
        _detailsTextBox.Multiline = true;
        _detailsTextBox.ScrollBars = ScrollBars.Both;
        _detailsTextBox.Font = new Font("Consolas", 10);
        _detailsTextBox.ReadOnly = true;

        panel.Controls.Add(_detailsTextBox);
        panel.Controls.Add(title);
        return panel;
    }

    private void AddColumn(string name, string headerText, int minimumWidth)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = headerText,
            MinimumWidth = minimumWidth
        });
    }

    private async Task RefreshDashboardAsync()
    {
        SetBusy(true, "Refreshing...");
        try
        {
            var baseUrl = NormalizeBaseUrl(_baseUrlTextBox.Text);
            var authTask = GetJsonAsync<AuthFilesResponse>($"{baseUrl}/v0/management/auth-files");
            var switchProjectTask = GetJsonAsync<BoolPayload>($"{baseUrl}/v0/management/quota-exceeded/switch-project");
            var switchPreviewTask = GetJsonAsync<BoolPayload>($"{baseUrl}/v0/management/quota-exceeded/switch-preview-model");
            var fingerprintTask = GetJsonAsync<IdentityFingerprintResponse>($"{baseUrl}/v0/management/identity-fingerprint");

            await Task.WhenAll(authTask, switchProjectTask, switchPreviewTask, fingerprintTask);

            var authPayload = await authTask;
            var switchProject = await switchProjectTask;
            var switchPreview = await switchPreviewTask;
            var fingerprint = await fingerprintTask;

            _allEntries = (authPayload?.Files ?? new List<AuthFileEntry>())
                .OrderByDescending(entry => IsCodex(entry))
                .ThenBy(entry => entry.Label ?? entry.Name ?? entry.AuthIndex ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            _quotaSwitchProjectLabel.Text = $"Quota switch-project: {switchProject?.GetFirstBoolean() switch { true => "on", false => "off", null => "--" }}";
            _quotaSwitchPreviewLabel.Text = $"Quota switch-preview-model: {switchPreview?.GetFirstBoolean() switch { true => "on", false => "off", null => "--" }}";
            _fingerprintLabel.Text = $"Codex fingerprint: {BuildFingerprintSummary(fingerprint?.IdentityFingerprint?.Codex)}";

            RebindGrid();
            SetBusy(false, $"Loaded {_allEntries.Count} auth entries");
        }
        catch (Exception ex)
        {
            SetBusy(false, "Refresh failed");
            MessageBox.Show(this, ex.Message, "CliRelay Codex Switch Dashboard", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ReconcileSelectedAsync()
    {
        var selected = GetSelectedEntry();
        if (selected is null || string.IsNullOrWhiteSpace(selected.AuthIndex))
        {
            MessageBox.Show(this, "Please select an auth row with a valid auth_index first.", "CliRelay Codex Switch Dashboard", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetBusy(true, $"Reconciling {selected.AuthIndex}...");
        try
        {
            var baseUrl = NormalizeBaseUrl(_baseUrlTextBox.Text);
            var payload = JsonSerializer.Serialize(new { auth_index = selected.AuthIndex });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync($"{baseUrl}/v0/management/quota/reconcile", content);
            var responseText = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"quota reconcile failed: {(int)response.StatusCode} {response.ReasonPhrase}\n{responseText}");
            }

            await RefreshDashboardAsync();
            SetBusy(false, $"Reconciled {selected.AuthIndex}");
        }
        catch (Exception ex)
        {
            SetBusy(false, "Reconcile failed");
            MessageBox.Show(this, ex.Message, "CliRelay Codex Switch Dashboard", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task<T?> GetJsonAsync<T>(string url)
    {
        using var response = await _httpClient.GetAsync(url);
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{url}\n{(int)response.StatusCode} {response.ReasonPhrase}\n{content}");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return default;
        }
        return JsonSerializer.Deserialize<T>(content, JsonOptions);
    }

    private void RebindGrid()
    {
        var currentSelection = GetSelectedEntry()?.Id;
        _grid.Rows.Clear();

        IEnumerable<AuthFileEntry> entries = _allEntries;
        if (_onlyCodexCheckBox.Checked)
        {
            entries = entries.Where(IsCodex);
        }

        foreach (var entry in entries)
        {
            var rowIndex = _grid.Rows.Add(
                entry.AuthIndex ?? string.Empty,
                entry.Label ?? entry.Name ?? string.Empty,
                entry.Provider ?? entry.Type ?? string.Empty,
                entry.PlanType ?? string.Empty,
                entry.Status ?? string.Empty,
                BuildAvailability(entry),
                BuildAccount(entry),
                FormatInstant(entry.NextRetryAfter ?? entry.Modtime),
                BuildRestrictionSummary(entry.Restrictions)
            );
            _grid.Rows[rowIndex].Tag = entry;
            if (string.Equals(entry.Id, currentSelection, StringComparison.OrdinalIgnoreCase))
            {
                _grid.Rows[rowIndex].Selected = true;
            }
        }

        if (_grid.Rows.Count > 0 && _grid.SelectedRows.Count == 0)
        {
            _grid.Rows[0].Selected = true;
        }
        ShowSelectedDetails();
    }

    private void ShowSelectedDetails()
    {
        var selected = GetSelectedEntry();
        if (selected is null)
        {
            _detailsTextBox.Text = string.Empty;
            _reconcileButton.Enabled = false;
            return;
        }

        _reconcileButton.Enabled = !string.IsNullOrWhiteSpace(selected.AuthIndex);
        _detailsTextBox.Text = JsonSerializer.Serialize(selected, JsonOptions);
    }

    private AuthFileEntry? GetSelectedEntry()
    {
        if (_grid.SelectedRows.Count == 0)
        {
            return null;
        }
        return _grid.SelectedRows[0].Tag as AuthFileEntry;
    }

    private void OpenManagePage()
    {
        try
        {
            var target = NormalizeBaseUrl(_baseUrlTextBox.Text) + "/manage";
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "CliRelay Codex Switch Dashboard", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SetBusy(bool busy, string status)
    {
        _refreshButton.Enabled = !busy;
        _reconcileButton.Enabled = !busy && GetSelectedEntry() is not null;
        _openManageButton.Enabled = !busy;
        _baseUrlTextBox.Enabled = !busy;
        _onlyCodexCheckBox.Enabled = !busy;
        _statusLabel.Text = status;
    }

    private static string NormalizeBaseUrl(string raw)
    {
        var trimmed = (raw ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new InvalidOperationException("Management base URL is required.");
        }
        return trimmed;
    }

    private static bool IsCodex(AuthFileEntry entry)
    {
        var provider = entry.Provider ?? entry.Type ?? string.Empty;
        return provider.Equals("codex", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildAccount(AuthFileEntry entry)
    {
        var account = FirstNonEmpty(entry.Label, entry.Email, entry.Account, entry.Name, entry.Id);
        var accountType = entry.AccountType?.Trim();
        if (string.IsNullOrWhiteSpace(accountType))
        {
            return account;
        }
        return $"{account} ({accountType})";
    }

    private static string BuildAvailability(AuthFileEntry entry)
    {
        if (entry.Disabled)
        {
            return "disabled";
        }
        if (entry.Unavailable)
        {
            return "unavailable";
        }
        if (string.Equals(entry.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            return "active";
        }
        return entry.Status ?? "--";
    }

    private static string BuildFingerprintSummary(CodexIdentityFingerprintPayload? fingerprint)
    {
        if (fingerprint is null)
        {
            return "--";
        }

        var enabled = fingerprint.Enabled ? "on" : "off";
        var sessionMode = string.IsNullOrWhiteSpace(fingerprint.SessionMode) ? "--" : fingerprint.SessionMode.Trim();
        var userAgent = string.IsNullOrWhiteSpace(fingerprint.UserAgent) ? "--" : fingerprint.UserAgent.Trim();
        return $"{enabled} | session-mode={sessionMode} | ua={userAgent}";
    }

    private static string BuildRestrictionSummary(JsonElement restrictions)
    {
        if (restrictions.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var item in restrictions.EnumerateArray())
        {
            var scope = item.TryGetProperty("scope", out var scopeNode) ? scopeNode.GetString() : null;
            var model = item.TryGetProperty("model", out var modelNode) ? modelNode.GetString() : null;
            var reason = item.TryGetProperty("reason", out var reasonNode) ? reasonNode.GetString() : null;
            var status = item.TryGetProperty("status", out var statusNode) ? statusNode.GetString() : null;

            var left = string.IsNullOrWhiteSpace(model) ? scope : $"{scope}:{model}";
            var right = FirstNonEmpty(reason, status, "restricted");
            if (!string.IsNullOrWhiteSpace(left))
            {
                parts.Add($"{left}={right}");
            }
        }

        return string.Join("; ", parts);
    }

    private static string FormatInstant(DateTimeOffset? value)
    {
        return value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }
        return string.Empty;
    }

    private sealed class AuthFilesResponse
    {
        [JsonPropertyName("files")]
        public List<AuthFileEntry> Files { get; set; } = new();
    }

    private sealed class AuthFileEntry
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("auth_index")]
        public string? AuthIndex { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("provider")]
        public string? Provider { get; set; }

        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("account_type")]
        public string? AccountType { get; set; }

        [JsonPropertyName("account")]
        public string? Account { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("status_message")]
        public string? StatusMessage { get; set; }

        [JsonPropertyName("plan_type")]
        public string? PlanType { get; set; }

        [JsonPropertyName("disabled")]
        public bool Disabled { get; set; }

        [JsonPropertyName("unavailable")]
        public bool Unavailable { get; set; }

        [JsonPropertyName("runtime_only")]
        public bool RuntimeOnly { get; set; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset? CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset? UpdatedAt { get; set; }

        [JsonPropertyName("modtime")]
        public DateTimeOffset? Modtime { get; set; }

        [JsonPropertyName("last_refresh")]
        public DateTimeOffset? LastRefresh { get; set; }

        [JsonPropertyName("next_retry_after")]
        public DateTimeOffset? NextRetryAfter { get; set; }

        [JsonPropertyName("restrictions")]
        public JsonElement Restrictions { get; set; }
    }

    private sealed class BoolPayload
    {
        [JsonExtensionData]
        public Dictionary<string, JsonElement> Data { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public bool? GetFirstBoolean()
        {
            foreach (var value in Data.Values)
            {
                if (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                {
                    return value.GetBoolean();
                }
            }
            return null;
        }
    }

    private sealed class IdentityFingerprintResponse
    {
        [JsonPropertyName("identity-fingerprint")]
        public IdentityFingerprintPayload? IdentityFingerprint { get; set; }
    }

    private sealed class IdentityFingerprintPayload
    {
        [JsonPropertyName("codex")]
        public CodexIdentityFingerprintPayload? Codex { get; set; }
    }

    private sealed class CodexIdentityFingerprintPayload
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("user-agent")]
        public string? UserAgent { get; set; }

        [JsonPropertyName("session-mode")]
        public string? SessionMode { get; set; }
    }
}
