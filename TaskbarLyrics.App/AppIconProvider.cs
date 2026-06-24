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
        var accent = GetPlayerAccentColor(sourceApp);
        TintIconBody(bitmap, accent);

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
            return Color.FromArgb(0, 194, 122);
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

    private static void TintIconBody(Bitmap bitmap, Color accent)
    {
        var darkAccent = Mix(Color.FromArgb(8, 20, 36), accent, 0.54);
        var lightAccent = Mix(Color.White, accent, 0.56);

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.A < 24 || !ShouldTint(pixel))
                {
                    continue;
                }

                var brightness = (pixel.R + pixel.G + pixel.B) / (255d * 3d);
                var tinted = Mix(darkAccent, lightAccent, Math.Clamp(brightness * 1.18, 0, 1));
                bitmap.SetPixel(x, y, Color.FromArgb(pixel.A, tinted));
            }
        }
    }

    private static bool ShouldTint(Color pixel)
    {
        var max = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B));
        var min = Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));
        var brightness = (pixel.R + pixel.G + pixel.B) / 3;
        return brightness < 150 || (brightness < 205 && max - min > 18 && pixel.B >= pixel.R);
    }

    private static Color Mix(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            (int)Math.Round(from.R + (to.R - from.R) * amount),
            (int)Math.Round(from.G + (to.G - from.G) * amount),
            (int)Math.Round(from.B + (to.B - from.B) * amount));
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
