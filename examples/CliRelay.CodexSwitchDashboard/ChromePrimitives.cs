using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace CliRelay.CodexSwitchDashboard;

internal enum DashboardThemeMode
{
    Light,
    Dark
}

internal enum BackgroundScene
{
    Login,
    App
}

internal sealed class AmbientBackgroundPanel : Panel
{
    private DashboardThemeMode _themeMode;
    private BackgroundScene _scene;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DashboardThemeMode ThemeMode
    {
        get => _themeMode;
        set
        {
            _themeMode = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public BackgroundScene Scene
    {
        get => _scene;
        set
        {
            _scene = value;
            Invalidate();
        }
    }

    public AmbientBackgroundPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var bounds = ClientRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            base.OnPaintBackground(e);
            return;
        }

        var palette = ThemeMode == DashboardThemeMode.Dark
            ? new BackgroundPalette(
                Color.FromArgb(24, 24, 27),
                Color.FromArgb(55, 65, 81),
                Color.FromArgb(37, 99, 235),
                Color.FromArgb(20, 184, 166),
                Color.FromArgb(168, 85, 247),
                Color.FromArgb(255, 255, 255, 30))
            : new BackgroundPalette(
                Color.FromArgb(248, 250, 252),
                Color.White,
                Color.FromArgb(59, 130, 246),
                Color.FromArgb(20, 184, 166),
                Color.FromArgb(168, 85, 247),
                Color.FromArgb(255, 255, 255, 110));

        using var backgroundBrush = new SolidBrush(palette.BaseColor);
        e.Graphics.FillRectangle(backgroundBrush, bounds);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        DrawBlob(e.Graphics, new RectangleF(-160, -150, 560, 560), Color.FromArgb(ThemeMode == DashboardThemeMode.Dark ? 50 : 34, palette.BlueBlob));
        DrawBlob(e.Graphics, new RectangleF(bounds.Width - 360, -120, 500, 500), Color.FromArgb(ThemeMode == DashboardThemeMode.Dark ? 46 : 28, palette.TealBlob));
        DrawBlob(e.Graphics, new RectangleF(bounds.Width / 5f, bounds.Height - 320, 560, 560), Color.FromArgb(ThemeMode == DashboardThemeMode.Dark ? 34 : 24, palette.PurpleBlob));

        if (Scene == BackgroundScene.Login)
        {
            DrawBlob(
                e.Graphics,
                new RectangleF(bounds.Width / 2f - 220, bounds.Height / 2f - 220, 440, 440),
                palette.CenterGlow);
        }
    }

    private static void DrawBlob(Graphics graphics, RectangleF ellipseBounds, Color centerColor)
    {
        using var path = new GraphicsPath();
        path.AddEllipse(ellipseBounds);
        using var brush = new PathGradientBrush(path)
        {
            CenterColor = centerColor,
            SurroundColors = [Color.FromArgb(0, centerColor)]
        };
        graphics.FillEllipse(brush, ellipseBounds);
    }

    private readonly record struct BackgroundPalette(
        Color BaseColor,
        Color SurfaceColor,
        Color BlueBlob,
        Color TealBlob,
        Color PurpleBlob,
        Color CenterGlow);
}

internal sealed class RoundedSurfacePanel : Panel
{
    private int _cornerRadius = 24;
    private Color _surfaceColor = Color.White;
    private Color _borderColor = Color.Transparent;
    private int _borderWidth = 1;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius
    {
        get => _cornerRadius;
        set
        {
            _cornerRadius = Math.Max(0, value);
            UpdateRegion();
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color SurfaceColor
    {
        get => _surfaceColor;
        set
        {
            _surfaceColor = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor
    {
        get => _borderColor;
        set
        {
            _borderColor = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int BorderWidth
    {
        get => _borderWidth;
        set
        {
            _borderWidth = Math.Max(0, value);
            Invalidate();
        }
    }

    public RoundedSurfacePanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        UpdateRegion();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateRegion();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = GraphicsHelper.CreateRoundedPath(ClientRectangle, CornerRadius);
        using var fill = new SolidBrush(SurfaceColor);
        e.Graphics.FillPath(fill, path);

        if (BorderWidth > 0 && BorderColor.A > 0)
        {
            using var pen = new Pen(BorderColor, BorderWidth);
            e.Graphics.DrawPath(pen, path);
        }
    }

    private void UpdateRegion()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        using var path = GraphicsHelper.CreateRoundedPath(ClientRectangle, CornerRadius);
        Region = new Region(path);
    }
}

internal static class GraphicsHelper
{
    public static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        var diameter = Math.Max(0, radius * 2);
        var path = new GraphicsPath();
        if (diameter <= 0)
        {
            path.AddRectangle(bounds);
            path.CloseFigure();
            return path;
        }

        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}
