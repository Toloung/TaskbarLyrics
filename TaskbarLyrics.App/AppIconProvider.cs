using System.Drawing;
using System.Runtime.InteropServices;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace TaskbarLyrics.App;

internal static class AppIconProvider
{
    private static string IconPath => Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon", "TaskbarLyrics.ico");

    public static Icon LoadTrayIcon()
    {
        return File.Exists(IconPath)
            ? new Icon(IconPath)
            : (Icon)SystemIcons.Application.Clone();
    }

    public static Icon LoadTrayIcon(string? sourceApp)
    {
        if (string.IsNullOrWhiteSpace(sourceApp))
        {
            return LoadTrayIcon();
        }

        using var baseIcon = LoadTrayIcon();
        using var bitmap = new Bitmap(baseIcon.ToBitmap(), new System.Drawing.Size(64, 64));
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var accent = GetPlayerAccentColor(sourceApp);
        using var tintBrush = new SolidBrush(Color.FromArgb(72, accent));
        using var borderPen = new Pen(Color.FromArgb(235, accent), 7);
        using var shadowBrush = new SolidBrush(Color.FromArgb(92, 0, 0, 0));
        using var outerBrush = new SolidBrush(Color.FromArgb(248, 255, 255, 255));
        using var accentBrush = new SolidBrush(accent);
        graphics.FillEllipse(tintBrush, 2, 2, 60, 60);
        graphics.DrawEllipse(borderPen, 5, 5, 54, 54);
        graphics.FillEllipse(shadowBrush, 32, 34, 29, 29);
        graphics.FillEllipse(outerBrush, 30, 30, 30, 30);
        graphics.FillEllipse(accentBrush, 34, 34, 22, 22);

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            _ = DestroyIcon(handle);
        }
    }

    public static void ApplyWindowIcon(Window window)
    {
        if (!File.Exists(IconPath))
        {
            return;
        }

        using var stream = File.OpenRead(IconPath);
        window.Icon = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
    }

    private static Color GetPlayerAccentColor(string sourceApp)
    {
        if (sourceApp.Equals("QQMusic", StringComparison.OrdinalIgnoreCase))
        {
            return Color.FromArgb(41, 182, 246);
        }

        if (sourceApp.Equals("Spotify", StringComparison.OrdinalIgnoreCase))
        {
            return Color.FromArgb(30, 215, 96);
        }

        if (sourceApp.Equals("Netease", StringComparison.OrdinalIgnoreCase))
        {
            return Color.FromArgb(229, 57, 53);
        }

        if (sourceApp.Equals("Kugou", StringComparison.OrdinalIgnoreCase))
        {
            return Color.FromArgb(52, 152, 219);
        }

        return Color.FromArgb(108, 165, 254);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
