namespace CliRelay.CodexSwitchDashboard;

internal abstract class ManagementPageBase : UserControl
{
    private readonly Action<string, bool> _setStatus;

    protected ManagementApiClient Client { get; }

    protected DashboardThemeMode ThemeMode { get; private set; } = DashboardThemeMode.Light;

    protected ThemePalette Palette => DashboardStyles.GetPalette(ThemeMode);

    protected ManagementPageBase(ManagementApiClient client, Action<string, bool> setStatus)
    {
        Client = client;
        _setStatus = setStatus;
        Dock = DockStyle.Fill;
        DoubleBuffered = true;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
    }

    public abstract string PageKey { get; }

    public abstract string PageTitle { get; }

    public abstract Task RefreshAsync();

    public void SetTheme(DashboardThemeMode themeMode)
    {
        ThemeMode = themeMode;
        BackColor = Palette.WindowBackground;
        ApplyThemeCore();
    }

    protected void ReportStatus(string message, bool isError = false)
    {
        _setStatus(message, isError);
    }

    protected abstract void ApplyThemeCore();
}
