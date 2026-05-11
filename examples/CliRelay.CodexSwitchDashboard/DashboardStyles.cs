using System.Drawing.Drawing2D;

namespace CliRelay.CodexSwitchDashboard;

internal sealed record ThemePalette(
    Color WindowBackground,
    Color Surface,
    Color SurfaceAlt,
    Color SurfaceMuted,
    Color Border,
    Color BorderStrong,
    Color TextPrimary,
    Color TextSecondary,
    Color TextTertiary,
    Color Accent,
    Color AccentStrong,
    Color AccentSoft,
    Color AccentText,
    Color Success,
    Color Warning,
    Color Danger,
    Color SidebarBackground,
    Color SidebarBorder,
    Color NavHover);

internal static class DashboardStyles
{
    private const string PreferredFontFamily = "Microsoft YaHei UI";

    public static ThemePalette GetPalette(DashboardThemeMode mode)
    {
        return mode == DashboardThemeMode.Dark
            ? new ThemePalette(
                WindowBackground: Color.FromArgb(18, 24, 38),
                Surface: Color.FromArgb(22, 30, 48),
                SurfaceAlt: Color.FromArgb(28, 37, 58),
                SurfaceMuted: Color.FromArgb(16, 23, 37),
                Border: Color.FromArgb(47, 59, 86),
                BorderStrong: Color.FromArgb(68, 84, 120),
                TextPrimary: Color.FromArgb(244, 247, 252),
                TextSecondary: Color.FromArgb(182, 191, 208),
                TextTertiary: Color.FromArgb(122, 136, 161),
                Accent: Color.FromArgb(59, 130, 246),
                AccentStrong: Color.FromArgb(37, 99, 235),
                AccentSoft: Color.FromArgb(26, 54, 93),
                AccentText: Color.White,
                Success: Color.FromArgb(52, 211, 153),
                Warning: Color.FromArgb(245, 158, 11),
                Danger: Color.FromArgb(248, 113, 113),
                SidebarBackground: Color.FromArgb(17, 24, 39),
                SidebarBorder: Color.FromArgb(40, 51, 72),
                NavHover: Color.FromArgb(32, 43, 66))
            : new ThemePalette(
                WindowBackground: Color.FromArgb(248, 250, 252),
                Surface: Color.White,
                SurfaceAlt: Color.FromArgb(243, 246, 252),
                SurfaceMuted: Color.FromArgb(235, 241, 249),
                Border: Color.FromArgb(224, 231, 242),
                BorderStrong: Color.FromArgb(203, 213, 225),
                TextPrimary: Color.FromArgb(15, 23, 42),
                TextSecondary: Color.FromArgb(71, 85, 105),
                TextTertiary: Color.FromArgb(148, 163, 184),
                Accent: Color.FromArgb(59, 130, 246),
                AccentStrong: Color.FromArgb(37, 99, 235),
                AccentSoft: Color.FromArgb(219, 234, 254),
                AccentText: Color.White,
                Success: Color.FromArgb(16, 185, 129),
                Warning: Color.FromArgb(245, 158, 11),
                Danger: Color.FromArgb(239, 68, 68),
                SidebarBackground: Color.FromArgb(255, 255, 255),
                SidebarBorder: Color.FromArgb(226, 232, 240),
                NavHover: Color.FromArgb(241, 245, 249));
    }

    public static Font CreateFont(float size, FontStyle style = FontStyle.Regular)
    {
        return new Font(PreferredFontFamily, size, style, GraphicsUnit.Point);
    }

    public static void StylePrimaryButton(Button button, ThemePalette palette)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = palette.TextPrimary;
        button.ForeColor = palette.AccentText;
        button.Font = CreateFont(9.5f, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
    }

    public static void StyleAccentButton(Button button, ThemePalette palette)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = palette.Accent;
        button.ForeColor = palette.AccentText;
        button.Font = CreateFont(9.5f, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
    }

    public static void StyleSecondaryButton(Button button, ThemePalette palette)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = palette.Border;
        button.FlatAppearance.BorderSize = 1;
        button.BackColor = palette.Surface;
        button.ForeColor = palette.TextPrimary;
        button.Font = CreateFont(9.25f, FontStyle.Regular);
        button.Cursor = Cursors.Hand;
    }

    public static void StyleGhostButton(Button button, ThemePalette palette)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = Color.Transparent;
        button.ForeColor = palette.TextSecondary;
        button.Font = CreateFont(9.25f, FontStyle.Regular);
        button.Cursor = Cursors.Hand;
    }

    public static void StyleTextBox(TextBox textBox, ThemePalette palette)
    {
        textBox.BorderStyle = BorderStyle.None;
        textBox.BackColor = palette.SurfaceAlt;
        textBox.ForeColor = palette.TextPrimary;
        textBox.Font = CreateFont(10f, FontStyle.Regular);
    }

    public static void StyleCheckBox(CheckBox checkBox, ThemePalette palette)
    {
        checkBox.Font = CreateFont(9.25f, FontStyle.Regular);
        checkBox.ForeColor = palette.TextSecondary;
        checkBox.BackColor = Color.Transparent;
    }

    public static void StyleDataGridView(DataGridView grid, ThemePalette palette)
    {
        grid.EnableHeadersVisualStyles = false;
        grid.BackgroundColor = palette.Surface;
        grid.BorderStyle = BorderStyle.None;
        grid.GridColor = palette.Border;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.AutoGenerateColumns = false;
        grid.ReadOnly = true;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = palette.SurfaceAlt,
            ForeColor = palette.TextSecondary,
            Font = CreateFont(9f, FontStyle.Bold),
            SelectionBackColor = palette.SurfaceAlt,
            SelectionForeColor = palette.TextSecondary,
            Padding = new Padding(0, 8, 0, 8)
        };
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = palette.Surface,
            ForeColor = palette.TextPrimary,
            SelectionBackColor = palette.AccentSoft,
            SelectionForeColor = palette.TextPrimary,
            Padding = new Padding(0, 6, 0, 6),
            Font = CreateFont(9f, FontStyle.Regular),
            WrapMode = DataGridViewTriState.False
        };
        grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = palette.SurfaceAlt,
            ForeColor = palette.TextPrimary,
            SelectionBackColor = palette.AccentSoft,
            SelectionForeColor = palette.TextPrimary,
            Padding = new Padding(0, 6, 0, 6),
            Font = CreateFont(9f, FontStyle.Regular)
        };
    }

    public static Label CreatePill(string text, Color backColor, Color foreColor)
    {
        return new Label
        {
            AutoSize = true,
            Text = text,
            BackColor = backColor,
            ForeColor = foreColor,
            Padding = new Padding(14, 8, 14, 8),
            Margin = new Padding(0, 0, 10, 10),
            Font = CreateFont(9f, FontStyle.Regular)
        };
    }

    public static void ApplyCardStyle(RoundedSurfacePanel panel, ThemePalette palette)
    {
        panel.SurfaceColor = palette.Surface;
        panel.BorderColor = palette.Border;
        panel.BackColor = Color.Transparent;
    }

    public static string FormatCompactNumber(long value)
    {
        return value switch
        {
            >= 1_000_000_000 => $"{value / 1_000_000_000d:0.##}B",
            >= 1_000_000 => $"{value / 1_000_000d:0.##}M",
            >= 1_000 => $"{value / 1_000d:0.##}K",
            _ => value.ToString("N0")
        };
    }

    public static string FormatCurrency(double value)
    {
        return value switch
        {
            >= 1000d or <= -1000d => $"${value:N0}",
            _ => $"${value:N2}"
        };
    }

    public static string FormatPercent(double value)
    {
        return $"{value:0.##}%";
    }

    public static string FormatDuration(long seconds)
    {
        if (seconds < 60)
        {
            return $"{seconds}s";
        }

        var time = TimeSpan.FromSeconds(seconds);
        if (time.TotalDays >= 1)
        {
            return $"{(int)time.TotalDays}d {time.Hours}h";
        }

        if (time.TotalHours >= 1)
        {
            return $"{(int)time.TotalHours}h {time.Minutes}m";
        }

        return $"{time.Minutes}m {time.Seconds}s";
    }

    public static string FormatBytes(long bytes)
    {
        return FormatBytesCore(bytes);
    }

    public static string FormatBytes(ulong bytes)
    {
        return FormatBytesCore(bytes);
    }

    public static string FormatRelative(DateTimeOffset? timestamp)
    {
        if (timestamp is null)
        {
            return "-";
        }

        var delta = DateTimeOffset.Now - timestamp.Value.ToLocalTime();
        if (delta.TotalSeconds < 60)
        {
            return "just now";
        }

        if (delta.TotalMinutes < 60)
        {
            return $"{(int)delta.TotalMinutes} min ago";
        }

        if (delta.TotalHours < 24)
        {
            return $"{(int)delta.TotalHours} hr ago";
        }

        return $"{(int)delta.TotalDays} d ago";
    }

    public static string Mask(string? value, int head = 4, int tail = 4)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        if (value.Length <= head + tail)
        {
            return value;
        }

        return $"{value[..head]}...{value[^tail..]}";
    }

    public static Color Blend(Color baseColor, Color mixColor, double ratio)
    {
        ratio = Math.Clamp(ratio, 0d, 1d);
        var inverse = 1d - ratio;
        return Color.FromArgb(
            255,
            (int)(baseColor.R * inverse + mixColor.R * ratio),
            (int)(baseColor.G * inverse + mixColor.G * ratio),
            (int)(baseColor.B * inverse + mixColor.B * ratio));
    }

    public static GraphicsPath CreateDividerPath(Rectangle bounds)
    {
        var path = new GraphicsPath();
        path.AddRectangle(bounds);
        return path;
    }

    private static string FormatBytesCore(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = bytes;
        var index = 0;
        while (value >= 1024d && index < units.Length - 1)
        {
            value /= 1024d;
            index++;
        }

        return $"{value:0.##} {units[index]}";
    }
}
