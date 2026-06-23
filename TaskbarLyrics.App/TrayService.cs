using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace TaskbarLyrics.App;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private Icon _icon;
    private TrayMenuWindow? _menuWindow;
    private string _activeSourceApp = string.Empty;

    public TrayService(
        Action toggleLyricsWindow,
        Action toggleFloatingWindowMode,
        Func<bool> isFloatingWindowModeEnabled,
        Action openSettings,
        Action exitApp)
    {
        _icon = AppIconProvider.LoadTrayIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "LyricsBar",
            Icon = _icon,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => toggleLyricsWindow();
        _notifyIcon.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Right)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                    ShowMenu(toggleLyricsWindow, toggleFloatingWindowMode, isFloatingWindowModeEnabled, openSettings, exitApp));
            }
        };
    }

    public void SetPlayerSource(string? sourceApp)
    {
        var normalized = NormalizeSourceApp(sourceApp);
        if (string.Equals(_activeSourceApp, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var nextIcon = AppIconProvider.LoadTrayIcon(normalized);
        _notifyIcon.Icon = nextIcon;
        _notifyIcon.Text = string.IsNullOrWhiteSpace(normalized)
            ? "LyricsBar"
            : $"LyricsBar - {GetPlayerDisplayName(normalized)}";
        _icon.Dispose();
        _icon = nextIcon;
        _activeSourceApp = normalized;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menuWindow?.Close();
        _icon.Dispose();
    }

    private void ShowMenu(
        Action toggleLyricsWindow,
        Action toggleFloatingWindowMode,
        Func<bool> isFloatingWindowModeEnabled,
        Action openSettings,
        Action exitApp)
    {
        _menuWindow?.Close();
        _menuWindow = new TrayMenuWindow(toggleLyricsWindow, toggleFloatingWindowMode, isFloatingWindowModeEnabled, openSettings, exitApp);
        _menuWindow.Closed += (_, _) => _menuWindow = null;
        _menuWindow.ShowAtCursor();
    }

    private static string NormalizeSourceApp(string? sourceApp)
    {
        return sourceApp?.Trim() switch
        {
            "QQMusic" => "QQMusic",
            "Netease" => "Netease",
            "Kugou" => "Kugou",
            "Spotify" => "Spotify",
            _ => string.Empty
        };
    }

    private static string GetPlayerDisplayName(string sourceApp)
    {
        return sourceApp switch
        {
            "QQMusic" => "QQ音乐",
            "Netease" => "网易云音乐",
            "Kugou" => "酷狗音乐",
            "Spotify" => "Spotify",
            _ => sourceApp
        };
    }
}
