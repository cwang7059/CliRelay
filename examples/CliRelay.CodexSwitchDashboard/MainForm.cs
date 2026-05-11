namespace CliRelay.CodexSwitchDashboard;

internal sealed class MainForm : Form
{
    private const string OverviewPageKey = "overview";

    private readonly UserPreferencesStore _preferencesStore = new();
    private readonly UserPreferences _preferences;
    private readonly ManagementApiClient _client = new();
    private readonly Dictionary<string, ManagementPageBase> _pages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Button> _navButtons = new(StringComparer.OrdinalIgnoreCase);

    private DashboardThemeMode _themeMode;
    private bool _sidebarCollapsed;
    private bool _signingIn;
    private string _currentPageKey = OverviewPageKey;
    private bool _showManagementKey;

    private AmbientBackgroundPanel _loginScene = null!;
    private AmbientBackgroundPanel _appScene = null!;

    private Button _loginThemeButton = null!;
    private RoundedSurfacePanel _baseUrlInputShell = null!;
    private RoundedSurfacePanel _managementKeyInputShell = null!;
    private TextBox _baseUrlTextBox = null!;
    private TextBox _managementKeyTextBox = null!;
    private Button _showManagementKeyButton = null!;
    private CheckBox _rememberKeyCheckBox = null!;
    private Button _signInButton = null!;
    private Label _managementEndpointLabel = null!;
    private Label _loginStatusLabel = null!;
    private Label _heroTitleLabel = null!;
    private Label _heroDescriptionLabel = null!;
    private Label _brandNameLabel = null!;

    private Panel _sidebarHost = null!;
    private RoundedSurfacePanel _sidebarSurface = null!;
    private Label _sidebarBrandTitleLabel = null!;
    private Label _sidebarBrandSubtitleLabel = null!;
    private TableLayoutPanel _sidebarNavLayout = null!;
    private Label _sidebarFooterTitleLabel = null!;
    private Label _sidebarFooterSubtitleLabel = null!;

    private RoundedSurfacePanel _headerSurface = null!;
    private Label _shellTitleLabel = null!;
    private Label _shellSubtitleLabel = null!;
    private Label _statusLabel = null!;
    private Button _sidebarToggleButton = null!;
    private Button _refreshButton = null!;
    private Button _shellThemeButton = null!;
    private Button _logoutButton = null!;
    private Panel _contentHost = null!;

    public MainForm()
    {
        _preferences = _preferencesStore.Load();
        _themeMode = _preferences.ThemeMode;
        _sidebarCollapsed = _preferences.SidebarCollapsed;

        Text = "CliRelay Codex Console";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1360, 860);
        ClientSize = new Size(1480, 920);
        FormBorderStyle = FormBorderStyle.Sizable;

        BuildChrome();
        LoadPreferencesIntoLogin();
        ApplyTheme();
        ShowLoginScene();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _client.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildChrome()
    {
        SuspendLayout();

        _loginScene = BuildLoginScene();
        _appScene = BuildAppScene();

        Controls.Add(_appScene);
        Controls.Add(_loginScene);

        ResumeLayout(false);
    }

    private AmbientBackgroundPanel BuildLoginScene()
    {
        var scene = new AmbientBackgroundPanel
        {
            Dock = DockStyle.Fill,
            Scene = BackgroundScene.Login,
            ThemeMode = _themeMode
        };

        var themeHost = new Panel
        {
            Dock = DockStyle.Top,
            Height = 72,
            Padding = new Padding(0, 20, 24, 0),
            BackColor = Color.Transparent
        };
        _loginThemeButton = new Button
        {
            Dock = DockStyle.Right,
            Width = 108,
            Height = 40,
            Text = string.Empty
        };
        _loginThemeButton.Click += (_, _) => ToggleTheme();
        themeHost.Controls.Add(_loginThemeButton);
        scene.Controls.Add(themeHost);

        var rootPadding = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(52, 8, 52, 44),
            BackColor = Color.Transparent
        };
        scene.Controls.Add(rootPadding);

        var contentGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = Color.Transparent
        };
        contentGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54f));
        contentGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46f));
        rootPadding.Controls.Add(contentGrid);

        contentGrid.Controls.Add(BuildLoginHero(), 0, 0);
        contentGrid.Controls.Add(BuildLoginCard(), 1, 0);

        return scene;
    }

    private Control BuildLoginHero()
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 40, 28, 40),
            BackColor = Color.Transparent
        };

        var brandRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Location = new Point(0, 0),
            BackColor = Color.Transparent
        };
        var brandBadge = new RoundedSurfacePanel
        {
            Size = new Size(46, 46),
            CornerRadius = 18,
            BorderWidth = 1,
            Margin = new Padding(0, 0, 12, 0)
        };
        var badgeText = new Label
        {
            Dock = DockStyle.Fill,
            Text = "CP",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = DashboardStyles.CreateFont(11f, FontStyle.Bold)
        };
        brandBadge.Controls.Add(badgeText);
        brandRow.Controls.Add(brandBadge);

        _brandNameLabel = new Label
        {
            AutoSize = true,
            Text = "Code Proxy",
            Font = DashboardStyles.CreateFont(15f, FontStyle.Bold),
            Margin = new Padding(0, 11, 0, 0)
        };
        brandRow.Controls.Add(_brandNameLabel);
        host.Controls.Add(brandRow);

        _heroTitleLabel = new Label
        {
            AutoSize = false,
            Location = new Point(0, 108),
            MaximumSize = new Size(640, 0),
            Size = new Size(640, 180),
            Text = "Control every provider\nfrom one calm console",
            Font = DashboardStyles.CreateFont(31f, FontStyle.Bold)
        };
        host.Controls.Add(_heroTitleLabel);

        _heroDescriptionLabel = new Label
        {
            AutoSize = false,
            Location = new Point(0, 308),
            MaximumSize = new Size(580, 0),
            Size = new Size(580, 110),
            Text = "Use the management key to connect your CliRelay node, review account health, switch Codex auths, and keep the whole proxy layer in one predictable place.",
            Font = DashboardStyles.CreateFont(10.5f, FontStyle.Regular)
        };
        host.Controls.Add(_heroDescriptionLabel);

        var trustedLabel = new Label
        {
            AutoSize = true,
            Location = new Point(0, 448),
            Text = "PROVIDER COVERAGE",
            Font = DashboardStyles.CreateFont(8.5f, FontStyle.Bold)
        };
        host.Controls.Add(trustedLabel);

        var providers = new FlowLayoutPanel
        {
            AutoSize = true,
            Location = new Point(0, 480),
            MaximumSize = new Size(620, 0),
            BackColor = Color.Transparent
        };
        providers.Controls.Add(DashboardStyles.CreatePill("OpenAI", Color.Transparent, Color.Empty));
        providers.Controls.Add(DashboardStyles.CreatePill("Gemini", Color.Transparent, Color.Empty));
        providers.Controls.Add(DashboardStyles.CreatePill("Claude", Color.Transparent, Color.Empty));
        providers.Controls.Add(DashboardStyles.CreatePill("Vertex", Color.Transparent, Color.Empty));
        host.Controls.Add(providers);

        return host;
    }

    private Control BuildLoginCard()
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(30, 24, 0, 24)
        };

        var card = new RoundedSurfacePanel
        {
            Dock = DockStyle.Fill,
            CornerRadius = 34,
            BorderWidth = 1,
            Padding = new Padding(34, 32, 34, 28)
        };
        host.Controls.Add(card);

        var titleLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 54,
            Text = "Sign in",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = DashboardStyles.CreateFont(21f, FontStyle.Bold)
        };
        card.Controls.Add(titleLabel);

        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 9,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 10, 0, 0)
        };
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        card.Controls.Add(stack);
        stack.BringToFront();

        var dividerRow = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        var dividerLineLeft = new Panel { Location = new Point(0, 21), Size = new Size(138, 1) };
        var dividerLineRight = new Panel { Anchor = AnchorStyles.Top | AnchorStyles.Right, Location = new Point(252, 21), Size = new Size(138, 1) };
        var dividerLabel = new Label
        {
            AutoSize = true,
            Location = new Point(147, 12),
            Text = "Continue with key",
            Font = DashboardStyles.CreateFont(8.25f, FontStyle.Regular)
        };
        dividerRow.Controls.Add(dividerLineLeft);
        dividerRow.Controls.Add(dividerLineRight);
        dividerRow.Controls.Add(dividerLabel);
        dividerRow.Resize += (_, _) =>
        {
            dividerLineLeft.Width = Math.Max(60, dividerRow.Width / 2 - 112);
            dividerLineRight.Width = dividerLineLeft.Width;
            dividerLineRight.Left = dividerRow.Width - dividerLineRight.Width;
            dividerLabel.Left = Math.Max(16, (dividerRow.Width - dividerLabel.Width) / 2);
        };
        stack.Controls.Add(dividerRow, 0, 0);

        stack.Controls.Add(CreateFieldLabel("Connection"), 0, 2);
        _baseUrlInputShell = CreateInputShell();
        _baseUrlTextBox = CreateInputTextBox();
        _baseUrlTextBox.TextChanged += (_, _) => UpdateManagementEndpointLabel();
        _baseUrlTextBox.KeyDown += HandleLoginEnter;
        _baseUrlInputShell.Controls.Add(_baseUrlTextBox);
        stack.Controls.Add(_baseUrlInputShell, 0, 3);

        _managementEndpointLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = DashboardStyles.CreateFont(8.25f, FontStyle.Regular)
        };
        stack.Controls.Add(_managementEndpointLabel, 0, 4);

        stack.Controls.Add(CreateFieldLabel("Management key"), 0, 5);
        _managementKeyInputShell = CreateInputShell();
        _managementKeyTextBox = CreateInputTextBox();
        _managementKeyTextBox.UseSystemPasswordChar = true;
        _managementKeyTextBox.KeyDown += HandleLoginEnter;
        _showManagementKeyButton = new Button
        {
            Dock = DockStyle.Right,
            Width = 72,
            Text = "Show"
        };
        _showManagementKeyButton.Click += (_, _) => ToggleManagementKeyVisibility();
        _managementKeyInputShell.Controls.Add(_showManagementKeyButton);
        _managementKeyInputShell.Controls.Add(_managementKeyTextBox);
        _managementKeyTextBox.Dock = DockStyle.Fill;
        stack.Controls.Add(_managementKeyInputShell, 0, 6);

        var rememberRow = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };
        _rememberKeyCheckBox = new CheckBox
        {
            AutoSize = true,
            Location = new Point(0, 11),
            Text = "Remember management key"
        };
        rememberRow.Controls.Add(_rememberKeyCheckBox);
        stack.Controls.Add(rememberRow, 0, 7);

        var actionHost = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 12, 0, 0)
        };
        actionHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        actionHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));
        actionHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        stack.Controls.Add(actionHost, 0, 8);

        _signInButton = new Button
        {
            Dock = DockStyle.Fill,
            Height = 46,
            Text = "Sign in"
        };
        _signInButton.Click += async (_, _) => await SignInAsync();
        actionHost.Controls.Add(_signInButton, 0, 0);

        _loginStatusLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            Font = DashboardStyles.CreateFont(8.75f, FontStyle.Regular)
        };
        actionHost.Controls.Add(_loginStatusLabel, 0, 2);

        return host;
    }

    private AmbientBackgroundPanel BuildAppScene()
    {
        var scene = new AmbientBackgroundPanel
        {
            Dock = DockStyle.Fill,
            Scene = BackgroundScene.App,
            ThemeMode = _themeMode,
            Visible = false
        };

        var frame = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            BackColor = Color.Transparent
        };
        scene.Controls.Add(frame);

        _sidebarHost = new Panel
        {
            Dock = DockStyle.Left,
            Width = _sidebarCollapsed ? 104 : 240,
            Padding = new Padding(0, 0, 16, 0),
            BackColor = Color.Transparent
        };
        frame.Controls.Add(_sidebarHost);

        _sidebarSurface = new RoundedSurfacePanel
        {
            Dock = DockStyle.Fill,
            CornerRadius = 28,
            BorderWidth = 1,
            Padding = new Padding(14, 18, 14, 16)
        };
        _sidebarHost.Controls.Add(_sidebarSurface);

        var brandPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 82,
            BackColor = Color.Transparent
        };
        _sidebarSurface.Controls.Add(brandPanel);

        var brandBadge = new RoundedSurfacePanel
        {
            Size = new Size(42, 42),
            Location = new Point(2, 4),
            CornerRadius = 16,
            BorderWidth = 0
        };
        var brandBadgeText = new Label
        {
            Dock = DockStyle.Fill,
            Text = "CP",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = DashboardStyles.CreateFont(10.5f, FontStyle.Bold)
        };
        brandBadge.Controls.Add(brandBadgeText);
        brandPanel.Controls.Add(brandBadge);

        _sidebarBrandTitleLabel = new Label
        {
            AutoSize = true,
            Location = new Point(56, 7),
            Text = "Console",
            Font = DashboardStyles.CreateFont(14f, FontStyle.Bold)
        };
        brandPanel.Controls.Add(_sidebarBrandTitleLabel);

        _sidebarBrandSubtitleLabel = new Label
        {
            AutoSize = true,
            Location = new Point(56, 34),
            Text = "CLI Proxy",
            Font = DashboardStyles.CreateFont(8.5f, FontStyle.Regular)
        };
        brandPanel.Controls.Add(_sidebarBrandSubtitleLabel);

        var footerCard = new RoundedSurfacePanel
        {
            Dock = DockStyle.Bottom,
            Height = 88,
            CornerRadius = 18,
            BorderWidth = 1,
            Padding = new Padding(14, 10, 14, 10)
        };
        _sidebarSurface.Controls.Add(footerCard);

        _sidebarFooterTitleLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 26,
            Text = "Admin",
            Font = DashboardStyles.CreateFont(10.25f, FontStyle.Bold)
        };
        footerCard.Controls.Add(_sidebarFooterTitleLabel);

        _sidebarFooterSubtitleLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 18,
            Text = "Management session",
            Font = DashboardStyles.CreateFont(8.5f, FontStyle.Regular)
        };
        footerCard.Controls.Add(_sidebarFooterSubtitleLabel);
        _sidebarFooterSubtitleLabel.BringToFront();

        _sidebarNavLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoScroll = true,
            Padding = new Padding(0, 14, 0, 14),
            BackColor = Color.Transparent
        };
        _sidebarNavLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        _sidebarSurface.Controls.Add(_sidebarNavLayout);

        AddNavigationButton("overview", "Dashboard", "DB");
        AddNavigationButton("auth-files", "Auth Files", "AU");
        AddNavigationButton("models", "Models", "MD");
        AddNavigationButton("logs", "Logs", "LG");

        var mainArea = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };
        frame.Controls.Add(mainArea);

        _headerSurface = new RoundedSurfacePanel
        {
            Dock = DockStyle.Top,
            Height = 88,
            CornerRadius = 28,
            BorderWidth = 1,
            Padding = new Padding(20, 16, 20, 12)
        };
        mainArea.Controls.Add(_headerSurface);

        var headerLeft = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };
        _headerSurface.Controls.Add(headerLeft);

        var headerRight = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 330,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0),
            BackColor = Color.Transparent
        };
        _headerSurface.Controls.Add(headerRight);

        _sidebarToggleButton = new Button
        {
            Size = new Size(52, 38),
            Text = string.Empty,
            Margin = new Padding(0, 0, 10, 0)
        };
        _sidebarToggleButton.Click += (_, _) => ToggleSidebar();
        headerRight.Controls.Add(_sidebarToggleButton);

        _refreshButton = new Button
        {
            Size = new Size(82, 38),
            Text = "Refresh",
            Margin = new Padding(0, 0, 10, 0)
        };
        _refreshButton.Click += async (_, _) => await RefreshCurrentPageAsync();
        headerRight.Controls.Add(_refreshButton);

        _shellThemeButton = new Button
        {
            Size = new Size(78, 38),
            Text = string.Empty,
            Margin = new Padding(0, 0, 10, 0)
        };
        _shellThemeButton.Click += (_, _) => ToggleTheme();
        headerRight.Controls.Add(_shellThemeButton);

        _logoutButton = new Button
        {
            Size = new Size(86, 38),
            Text = "Logout",
            Margin = new Padding(0, 0, 0, 0)
        };
        _logoutButton.Click += (_, _) => Logout();
        headerRight.Controls.Add(_logoutButton);

        _shellTitleLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = "Dashboard",
            Font = DashboardStyles.CreateFont(15.5f, FontStyle.Bold)
        };
        headerLeft.Controls.Add(_shellTitleLabel);

        _shellSubtitleLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            Text = "-",
            Font = DashboardStyles.CreateFont(9.25f, FontStyle.Regular)
        };
        headerLeft.Controls.Add(_shellSubtitleLabel);

        _statusLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 22,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = DashboardStyles.CreateFont(8.5f, FontStyle.Regular)
        };
        headerLeft.Controls.Add(_statusLabel);
        _statusLabel.BringToFront();

        var contentPadding = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 16, 0, 0),
            BackColor = Color.Transparent
        };
        mainArea.Controls.Add(contentPadding);

        _contentHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };
        contentPadding.Controls.Add(_contentHost);

        return scene;
    }

    private void AddNavigationButton(string key, string title, string shortText)
    {
        var row = _sidebarNavLayout.RowCount++;
        _sidebarNavLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        var button = new Button
        {
            Dock = DockStyle.Fill,
            Height = 44,
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderSize = 0 },
            Tag = new NavItemMeta(key, title, shortText),
            Text = title,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(18, 0, 18, 0),
            Margin = new Padding(0, 0, 0, 10)
        };
        button.Click += async (_, _) => await SwitchToPageAsync(key);

        _navButtons[key] = button;
        _sidebarNavLayout.Controls.Add(button, 0, row);
    }

    private RoundedSurfacePanel CreateInputShell()
    {
        return new RoundedSurfacePanel
        {
            Dock = DockStyle.Fill,
            CornerRadius = 22,
            BorderWidth = 1,
            Padding = new Padding(18, 14, 14, 10)
        };
    }

    private TextBox CreateInputTextBox()
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = false,
            Margin = Padding.Empty
        };
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

    private void LoadPreferencesIntoLogin()
    {
        _baseUrlTextBox.Text = _preferences.BaseUrl;
        _managementKeyTextBox.Text = _preferences.RememberManagementKey ? _preferences.ManagementKey : string.Empty;
        _rememberKeyCheckBox.Checked = _preferences.RememberManagementKey;
        UpdateManagementEndpointLabel();
    }

    private void UpdateManagementEndpointLabel()
    {
        var normalized = NormalizeBaseUrl(_baseUrlTextBox.Text, allowEmpty: true);
        _managementEndpointLabel.Text = string.IsNullOrWhiteSpace(normalized)
            ? "Management endpoint: -"
            : $"Management endpoint: {normalized}/v0/management";
    }

    private async Task SignInAsync()
    {
        if (_signingIn)
        {
            return;
        }

        var baseUrl = NormalizeBaseUrl(_baseUrlTextBox.Text, allowEmpty: false);
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            SetLoginStatus("A valid base URL is required.", isError: true);
            return;
        }

        var managementKey = _managementKeyTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(managementKey))
        {
            SetLoginStatus("Management key is required.", isError: true);
            return;
        }

        _signingIn = true;
        ToggleLoginControls(false);
        SetLoginStatus("Connecting to management endpoint...", isError: false);

        try
        {
            _client.BaseUrl = baseUrl;
            _client.ManagementKey = managementKey;

            _ = await _client.GetDashboardSummaryAsync();

            _preferences.BaseUrl = baseUrl;
            _preferences.RememberManagementKey = _rememberKeyCheckBox.Checked;
            _preferences.ManagementKey = _rememberKeyCheckBox.Checked ? managementKey : string.Empty;
            _preferences.ThemeMode = _themeMode;
            _preferences.SidebarCollapsed = _sidebarCollapsed;
            PersistPreferences();

            EnsurePagesCreated();
            ShowShellScene();
            await SwitchToPageAsync(_currentPageKey, forceRefresh: true);
            SetStatus("Connected. Dashboard synchronized.", isError: false);
            SetLoginStatus(string.Empty, isError: false);
        }
        catch (Exception ex)
        {
            SetLoginStatus(ex.Message, isError: true);
        }
        finally
        {
            _signingIn = false;
            ToggleLoginControls(true);
        }
    }

    private void ToggleLoginControls(bool enabled)
    {
        _baseUrlTextBox.Enabled = enabled;
        _managementKeyTextBox.Enabled = enabled;
        _rememberKeyCheckBox.Enabled = enabled;
        _showManagementKeyButton.Enabled = enabled;
        _signInButton.Enabled = enabled;
        _signInButton.Text = enabled ? "Sign in" : "Signing in...";
    }

    private void ToggleManagementKeyVisibility()
    {
        _showManagementKey = !_showManagementKey;
        _managementKeyTextBox.UseSystemPasswordChar = !_showManagementKey;
        _showManagementKeyButton.Text = _showManagementKey ? "Hide" : "Show";
    }

    private async Task SwitchToPageAsync(string key, bool forceRefresh = false)
    {
        if (!_pages.TryGetValue(key, out var targetPage))
        {
            return;
        }

        _currentPageKey = key;
        foreach (var page in _pages.Values)
        {
            page.Visible = false;
        }

        targetPage.Visible = true;
        targetPage.BringToFront();
        UpdateNavigationState();
        UpdateShellHeader();

        if (forceRefresh || string.Equals(key, _currentPageKey, StringComparison.OrdinalIgnoreCase))
        {
            await RefreshCurrentPageAsync();
        }
    }

    private async Task RefreshCurrentPageAsync()
    {
        if (!_pages.TryGetValue(_currentPageKey, out var page))
        {
            return;
        }

        _refreshButton.Enabled = false;
        try
        {
            SetStatus($"Refreshing {page.PageTitle.ToLowerInvariant()}...", isError: false);
            await page.RefreshAsync();
            SetStatus($"{page.PageTitle} updated.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
        finally
        {
            _refreshButton.Enabled = true;
        }
    }

    private void EnsurePagesCreated()
    {
        if (_pages.Count > 0)
        {
            return;
        }

        var pages = new ManagementPageBase[]
        {
            new OverviewPage(_client, SetStatus),
            new AuthFilesPage(_client, SetStatus),
            new ModelsPage(_client, SetStatus),
            new LogsPage(_client, SetStatus)
        };

        foreach (var page in pages)
        {
            page.Visible = false;
            page.SetTheme(_themeMode);
            _pages[page.PageKey] = page;
            _contentHost.Controls.Add(page);
        }
    }

    private void ToggleSidebar()
    {
        _sidebarCollapsed = !_sidebarCollapsed;
        _preferences.SidebarCollapsed = _sidebarCollapsed;
        PersistPreferences();
        ApplySidebarState();
    }

    private void ApplySidebarState()
    {
        _sidebarHost.Width = _sidebarCollapsed ? 104 : 240;
        _sidebarBrandTitleLabel.Text = _sidebarCollapsed ? "CP" : "Console";
        _sidebarBrandSubtitleLabel.Visible = !_sidebarCollapsed;
        _sidebarFooterTitleLabel.Text = _sidebarCollapsed ? "A" : "Admin";
        _sidebarFooterSubtitleLabel.Visible = !_sidebarCollapsed;
        _sidebarToggleButton.Text = _sidebarCollapsed ? ">>" : "<<";

        foreach (var pair in _navButtons)
        {
            if (pair.Value.Tag is not NavItemMeta meta)
            {
                continue;
            }

            pair.Value.Text = _sidebarCollapsed ? meta.ShortText : meta.Title;
            pair.Value.TextAlign = _sidebarCollapsed ? ContentAlignment.MiddleCenter : ContentAlignment.MiddleLeft;
            pair.Value.Padding = _sidebarCollapsed ? new Padding(0) : new Padding(18, 0, 18, 0);
        }
    }

    private void UpdateShellHeader()
    {
        if (_pages.TryGetValue(_currentPageKey, out var page))
        {
            _shellTitleLabel.Text = page.PageTitle;
        }
        else
        {
            _shellTitleLabel.Text = "Console";
        }

        _shellSubtitleLabel.Text = $"Connected to {_client.BaseUrl}";
    }

    private void UpdateNavigationState()
    {
        var palette = DashboardStyles.GetPalette(_themeMode);
        foreach (var pair in _navButtons)
        {
            var button = pair.Value;
            var active = string.Equals(pair.Key, _currentPageKey, StringComparison.OrdinalIgnoreCase);
            button.BackColor = active ? palette.Accent : Color.Transparent;
            button.ForeColor = active ? palette.AccentText : palette.TextSecondary;
            button.Font = DashboardStyles.CreateFont(9.5f, active ? FontStyle.Bold : FontStyle.Regular);
        }
    }

    private void ToggleTheme()
    {
        _themeMode = _themeMode == DashboardThemeMode.Light ? DashboardThemeMode.Dark : DashboardThemeMode.Light;
        _preferences.ThemeMode = _themeMode;
        PersistPreferences();
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        var palette = DashboardStyles.GetPalette(_themeMode);
        BackColor = palette.WindowBackground;
        ForeColor = palette.TextPrimary;

        _loginScene.ThemeMode = _themeMode;
        _appScene.ThemeMode = _themeMode;

        ApplyLoginTheme(palette);
        ApplyShellTheme(palette);

        foreach (var page in _pages.Values)
        {
            page.SetTheme(_themeMode);
        }
    }

    private void ApplyLoginTheme(ThemePalette palette)
    {
        _loginThemeButton.Text = _themeMode == DashboardThemeMode.Light ? "Dark Mode" : "Light Mode";
        DashboardStyles.StyleSecondaryButton(_loginThemeButton, palette);

        DashboardStyles.ApplyCardStyle(_baseUrlInputShell, palette);
        DashboardStyles.ApplyCardStyle(_managementKeyInputShell, palette);
        DashboardStyles.StyleTextBox(_baseUrlTextBox, palette);
        DashboardStyles.StyleTextBox(_managementKeyTextBox, palette);

        DashboardStyles.StyleGhostButton(_showManagementKeyButton, palette);
        DashboardStyles.StylePrimaryButton(_signInButton, palette);
        DashboardStyles.StyleCheckBox(_rememberKeyCheckBox, palette);

        _brandNameLabel.ForeColor = palette.TextPrimary;
        _heroTitleLabel.ForeColor = palette.TextPrimary;
        _heroDescriptionLabel.ForeColor = palette.TextSecondary;
        _managementEndpointLabel.ForeColor = palette.TextTertiary;
        _loginStatusLabel.ForeColor = palette.TextSecondary;

        foreach (var label in _loginScene.Controls.OfType<Control>().SelectMany(EnumerateLabels))
        {
            if (label.Text is "Connection" or "Management key" or "Continue with key" or "PROVIDER COVERAGE" or "Sign in")
            {
                label.ForeColor = label.Text == "Sign in" ? palette.TextPrimary : palette.TextSecondary;
            }
        }

        foreach (var pill in _loginScene.Controls.OfType<Control>().SelectMany(EnumerateLabels))
        {
            if (pill.Text is "OpenAI" or "Gemini" or "Claude" or "Vertex")
            {
                pill.BackColor = palette.Surface;
                pill.ForeColor = palette.TextPrimary;
            }
        }

        foreach (var panel in _loginScene.Controls.OfType<Control>().SelectMany(EnumeratePanels))
        {
            if (panel.Height == 1)
            {
                panel.BackColor = palette.Border;
            }
        }
    }

    private void ApplyShellTheme(ThemePalette palette)
    {
        DashboardStyles.ApplyCardStyle(_sidebarSurface, palette);
        _sidebarSurface.SurfaceColor = palette.SidebarBackground;
        _sidebarSurface.BorderColor = palette.SidebarBorder;
        DashboardStyles.ApplyCardStyle(_headerSurface, palette);
        DashboardStyles.ApplyCardStyle((RoundedSurfacePanel)_sidebarSurface.Controls.OfType<RoundedSurfacePanel>().First(), palette);

        _sidebarBrandTitleLabel.ForeColor = palette.TextPrimary;
        _sidebarBrandSubtitleLabel.ForeColor = palette.TextTertiary;
        _sidebarFooterTitleLabel.ForeColor = palette.TextPrimary;
        _sidebarFooterSubtitleLabel.ForeColor = palette.TextTertiary;
        _shellTitleLabel.ForeColor = palette.TextPrimary;
        _shellSubtitleLabel.ForeColor = palette.TextSecondary;

        _statusLabel.ForeColor = palette.TextTertiary;

        DashboardStyles.StyleSecondaryButton(_sidebarToggleButton, palette);
        DashboardStyles.StyleSecondaryButton(_refreshButton, palette);
        DashboardStyles.StyleSecondaryButton(_shellThemeButton, palette);
        DashboardStyles.StyleSecondaryButton(_logoutButton, palette);

        _shellThemeButton.Text = _themeMode == DashboardThemeMode.Light ? "Dark" : "Light";
        _logoutButton.ForeColor = palette.Danger;

        ApplySidebarState();
        UpdateNavigationState();
    }

    private void SetLoginStatus(string message, bool isError)
    {
        var palette = DashboardStyles.GetPalette(_themeMode);
        _loginStatusLabel.Text = message;
        _loginStatusLabel.ForeColor = isError ? palette.Danger : palette.TextSecondary;
    }

    private void SetStatus(string message, bool isError)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetStatus(message, isError));
            return;
        }

        var palette = DashboardStyles.GetPalette(_themeMode);
        _statusLabel.Text = message;
        _statusLabel.ForeColor = isError ? palette.Danger : palette.TextTertiary;
    }

    private void Logout()
    {
        _client.ManagementKey = string.Empty;
        ShowLoginScene();
        SetLoginStatus("Session closed.", isError: false);
        _managementKeyTextBox.Text = _preferences.RememberManagementKey ? _preferences.ManagementKey : string.Empty;
    }

    private void ShowLoginScene()
    {
        _appScene.Visible = false;
        _loginScene.Visible = true;
        _loginScene.BringToFront();
    }

    private void ShowShellScene()
    {
        _loginScene.Visible = false;
        _appScene.Visible = true;
        _appScene.BringToFront();
        UpdateShellHeader();
    }

    private void PersistPreferences()
    {
        _preferences.ThemeMode = _themeMode;
        _preferences.SidebarCollapsed = _sidebarCollapsed;
        _preferencesStore.Save(_preferences);
    }

    private void HandleLoginEnter(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            _ = SignInAsync();
        }
    }

    private static string NormalizeBaseUrl(string value, bool allowEmpty)
    {
        value = (value ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(value))
        {
            return allowEmpty ? string.Empty : string.Empty;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        return uri.ToString().TrimEnd('/');
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

    private static IEnumerable<Panel> EnumeratePanels(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is Panel panel)
            {
                yield return panel;
            }

            foreach (var nested in EnumeratePanels(child))
            {
                yield return nested;
            }
        }
    }

    private sealed record NavItemMeta(string Key, string Title, string ShortText);
}
