using System.Text.Json;

namespace CliRelay.CodexSwitchDashboard;

internal sealed class UserPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public UserPreferencesStore()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CliRelay",
            "CodexSwitchDashboard");
        Directory.CreateDirectory(root);
        _settingsPath = Path.Combine(root, "settings.json");
    }

    public UserPreferences Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new UserPreferences();
            }

            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<UserPreferences>(json, JsonOptions) ?? new UserPreferences();
        }
        catch
        {
            return new UserPreferences();
        }
    }

    public void Save(UserPreferences preferences)
    {
        var json = JsonSerializer.Serialize(preferences, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }
}

internal sealed class UserPreferences
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:8317";

    public string ManagementKey { get; set; } = string.Empty;

    public bool RememberManagementKey { get; set; }

    public DashboardThemeMode ThemeMode { get; set; } = DashboardThemeMode.Light;

    public bool SidebarCollapsed { get; set; }
}
