using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using TaskbarLyrics.Core.Services;
using TaskbarLyrics.Core.Utilities;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace TaskbarLyrics.App;

public partial class SettingsWindow : Wpf.Ui.Controls.FluentWindow
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AppSettings _settings;
    private bool _isWebReady;
    private bool _useLightTheme;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        AppIconProvider.ApplyWindowIcon(this);
        _settings = settings;
        _useLightTheme = settings.UseLightSettingsWindow;
        ApplySettingsWindowTheme();

        SourceInitialized += SettingsWindow_SourceInitialized;
        Loaded += SettingsWindow_Loaded;
        Activated += SettingsWindow_Activated;
        StateChanged += SettingsWindow_StateChanged;
        Closed += SettingsWindow_Closed;
    }

    private void SettingsWindow_SourceInitialized(object? sender, EventArgs e)
    {
        ApplyWindowChromeAttributes();
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero && HwndSource.FromHwnd(hwnd) is { } source)
        {
            source.AddHook(WndProc);
        }
    }

    private async void SettingsWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        await InitializeSettingsWebViewAsync();
    }

    private void SettingsWindow_Activated(object? sender, EventArgs e)
    {
        ApplyWindowChromeAttributes();
    }

    private void SettingsWindow_StateChanged(object? sender, EventArgs e)
    {
        UpdateMaximizeRestoreIcon();
    }

    private void SettingsWindow_Closed(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero && HwndSource.FromHwnd(hwnd) is { } source)
        {
            source.RemoveHook(WndProc);
        }

        if (SettingsWebView.CoreWebView2 is not null)
        {
            SettingsWebView.CoreWebView2.WebMessageReceived -= SettingsWebView_WebMessageReceived;
            SettingsWebView.CoreWebView2.Navigate("about:blank");
        }

        SettingsWebView.Dispose();
    }

    private async Task InitializeSettingsWebViewAsync()
    {
        if (_isWebReady)
        {
            return;
        }

        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LyricsBar",
            "WebView2",
            "Settings");
        var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        await SettingsWebView.EnsureCoreWebView2Async(environment);

        var core = SettingsWebView.CoreWebView2;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsBuiltInErrorPageEnabled = false;
        core.WebMessageReceived += SettingsWebView_WebMessageReceived;

        var htmlPath = Path.Combine(AppContext.BaseDirectory, "Web", "Settings", "settings.html");
        SettingsWebView.Source = new Uri(htmlPath);
        _isWebReady = true;
    }

    private async void SettingsWebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var messageJson = e.TryGetWebMessageAsString();
        if (string.IsNullOrWhiteSpace(messageJson))
        {
            messageJson = e.WebMessageAsJson;
        }

        var message = JsonSerializer.Deserialize<WebSettingsMessage>(messageJson, JsonOptions);
        if (message?.Type is null)
        {
            return;
        }

        switch (message.Type)
        {
            case "ready":
                await PushSettingsToWebAsync();
                break;
            case "update":
                ApplyWebSettingUpdate(message.Key, message.Value);
                SaveSettings();
                break;
            case "reorderSources":
                ApplySourceOrder(message.Value);
                SaveSettings();
                break;
            case "placeFloatingWindow":
                ApplyFloatingWindowPlacement(message.Value);
                SaveSettings();
                await PushSettingsToWebAsync();
                break;
            case "resetDefaults":
                var defaultSettings = new AppSettings();
                App.ApplyStartupForegroundColor(defaultSettings);
                CopySettings(defaultSettings, _settings);
                SaveSettings();
                await PushSettingsToWebAsync();
                break;
            case "clearCache":
                ClearLyricCache();
                break;
            case "openSpectrumTuning":
                if (System.Windows.Application.Current is App app)
                {
                    app.OpenSpectrumTuningWindow();
                }
                break;
            case "pickColor":
                await PickForegroundColorAsync();
                break;
        }
    }

    private async Task PushSettingsToWebAsync()
    {
        if (!_isWebReady || SettingsWebView.CoreWebView2 is null)
        {
            return;
        }

        var payload = CreateSettingsPayload();
        var settingsJson = JsonSerializer.Serialize(payload, JsonOptions);
        var fontsJson = JsonSerializer.Serialize(GetFontOptions(), JsonOptions);
        await SettingsWebView.ExecuteScriptAsync($"window.settingsApp?.setState({settingsJson}, {fontsJson});");
    }

    public async void ApplyExternalSettings(AppSettings settings)
    {
        CopySettings(settings, _settings);
        await PushSettingsToWebAsync();
    }

    private WebSettingsPayload CreateSettingsPayload()
    {
        return new WebSettingsPayload
        {
            SourceRecognitionOrder = NormalizeSourceOrder(_settings.SourceRecognitionOrder),
            EnableNetease = _settings.EnableNetease,
            EnableQQMusic = _settings.EnableQQMusic,
            EnableKugou = _settings.EnableKugou,
            EnableSpotify = _settings.EnableSpotify,
            ShowLyricsOnStartup = _settings.ShowLyricsOnStartup,
            StartWithWindows = _settings.StartWithWindows,
            ShowLyricTranslation = _settings.ShowLyricTranslation,
            EnablePureMusicSpectrum = _settings.EnablePureMusicSpectrum,
            UseLightSettingsWindow = _settings.UseLightSettingsWindow,
            FontSize = _settings.FontSize,
            FontFamily = ResolveInstalledFontFamily(_settings.FontFamily) ?? ResolveInstalledFontFamily(AppSettings.DefaultFontFamily) ?? "Microsoft YaHei UI",
            FontWeight = NormalizeFontWeight(_settings.FontWeight),
            ForegroundColorMode = _settings.ForegroundColorMode,
            ForegroundColor = _settings.ForegroundColor,
            ShowBackground = _settings.ShowBackground,
            BackgroundOpacity = _settings.BackgroundOpacity,
            UseCoverColorBackground = _settings.UseCoverColorBackground,
            UseCoverColorTaskbarLyrics = _settings.UseCoverColorTaskbarLyrics,
            ShowBorder = _settings.ShowBorder,
            ShowTextShadow = _settings.ShowTextShadow,
            EnableFloatingWindowMode = _settings.EnableFloatingWindowMode,
            WindowWidth = _settings.WindowWidth,
            FloatingWindowWidth = _settings.FloatingWindowWidth,
            FloatingWindowHeight = _settings.FloatingWindowHeight,
            HorizontalAnchor = _settings.HorizontalAnchor,
            XOffset = _settings.XOffset,
            YOffset = _settings.YOffset,
            EnableSmtcTimelineMonitor = _settings.EnableSmtcTimelineMonitor
        };
    }

    private static List<string> NormalizeSourceOrder(IEnumerable<string>? order)
    {
        var known = new[] { "QQMusic", "Netease", "Kugou", "Spotify" };
        var result = new List<string>();

        foreach (var source in order ?? Enumerable.Empty<string>())
        {
            if (known.Contains(source) && !result.Contains(source))
            {
                result.Add(source);
            }
        }

        foreach (var source in known)
        {
            if (!result.Contains(source))
            {
                result.Add(source);
            }
        }

        return result;
    }

    private static List<FontOption> GetFontOptions()
    {
        var fonts = Fonts.SystemFontFamilies
            .Select(x => new FontOption
            {
                Value = x.Source,
                Label = GetLocalizedFontName(x)
            })
            .OrderBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (!fonts.Any(x => string.Equals(x.Value, AppSettings.BundledFontFamily, StringComparison.OrdinalIgnoreCase)))
        {
            fonts.Insert(0, new FontOption
            {
                Value = AppSettings.BundledFontFamily,
                Label = $"{AppSettings.BundledFontFamily} (内置)"
            });
        }

        return fonts;
    }

    private static string GetLocalizedFontName(System.Windows.Media.FontFamily fontFamily)
    {
        var languages = new[]
        {
            XmlLanguage.GetLanguage("zh-CN"),
            XmlLanguage.GetLanguage("zh-Hans"),
            XmlLanguage.GetLanguage(CultureInfo.CurrentUICulture.IetfLanguageTag),
            XmlLanguage.GetLanguage("en-US")
        };

        foreach (var language in languages)
        {
            if (fontFamily.FamilyNames.TryGetValue(language, out var name) &&
                !string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        return fontFamily.FamilyNames.Values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
            ?? fontFamily.Source;
    }

    private string? ResolveInstalledFontFamily(string? fontFamily)
    {
        if (string.IsNullOrWhiteSpace(fontFamily))
        {
            return null;
        }

        var fonts = GetFontOptions();
        var byValue = fonts.ToDictionary(x => x.Value, x => x.Value, StringComparer.OrdinalIgnoreCase);
        var byLabel = fonts
            .GroupBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Value, StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in fontFamily.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(candidate, AppSettings.BundledFontFamily, StringComparison.OrdinalIgnoreCase))
            {
                return AppSettings.BundledFontFamily;
            }

            if (byValue.TryGetValue(candidate, out var value) ||
                byLabel.TryGetValue(candidate, out value))
            {
                return value;
            }
        }

        return null;
    }

    private static string NormalizeFontWeight(string? value)
    {
        return value?.Trim() switch
        {
            "Light" => "Light",
            "Normal" => "Normal",
            "Medium" => "Medium",
            "SemiBold" => "SemiBold",
            "Bold" => "Bold",
            _ => "SemiBold"
        };
    }

    private void ApplyWebSettingUpdate(string? key, JsonElement? value)
    {
        if (key is null || value is null)
        {
            return;
        }

        var element = value.Value;
        switch (key)
        {
            case "enableQQMusic":
                _settings.EnableQQMusic = ReadBool(element, _settings.EnableQQMusic);
                break;
            case "enableNetease":
                _settings.EnableNetease = ReadBool(element, _settings.EnableNetease);
                break;
            case "enableKugou":
                _settings.EnableKugou = ReadBool(element, _settings.EnableKugou);
                break;
            case "enableSpotify":
                _settings.EnableSpotify = ReadBool(element, _settings.EnableSpotify);
                break;
            case "showLyricsOnStartup":
                _settings.ShowLyricsOnStartup = ReadBool(element, _settings.ShowLyricsOnStartup);
                break;
            case "startWithWindows":
                _settings.StartWithWindows = ReadBool(element, _settings.StartWithWindows);
                break;
            case "showLyricTranslation":
                _settings.ShowLyricTranslation = ReadBool(element, _settings.ShowLyricTranslation);
                break;
            case "enablePureMusicSpectrum":
                _settings.EnablePureMusicSpectrum = ReadBool(element, _settings.EnablePureMusicSpectrum);
                break;
            case "useLightSettingsWindow":
                _settings.UseLightSettingsWindow = ReadBool(element, _settings.UseLightSettingsWindow);
                _useLightTheme = _settings.UseLightSettingsWindow;
                ApplySettingsWindowTheme();
                break;
            case "showBackground":
                _settings.ShowBackground = ReadBool(element, _settings.ShowBackground);
                break;
            case "useCoverColorBackground":
                _settings.UseCoverColorBackground = ReadBool(element, _settings.UseCoverColorBackground);
                break;
            case "useCoverColorTaskbarLyrics":
                _settings.UseCoverColorTaskbarLyrics = ReadBool(element, _settings.UseCoverColorTaskbarLyrics);
                break;
            case "showBorder":
                _settings.ShowBorder = ReadBool(element, _settings.ShowBorder);
                break;
            case "showTextShadow":
                _settings.ShowTextShadow = ReadBool(element, _settings.ShowTextShadow);
                break;
            case "enableFloatingWindowMode":
                _settings.EnableFloatingWindowMode = ReadBool(element, _settings.EnableFloatingWindowMode);
                break;
            case "enableSmtcTimelineMonitor":
                _settings.EnableSmtcTimelineMonitor = ReadBool(element, _settings.EnableSmtcTimelineMonitor);
                break;
            case "fontSize":
                _settings.FontSize = Math.Clamp(ReadDouble(element, _settings.FontSize), 10, 40);
                break;
            case "fontFamily":
                _settings.FontFamily = ReadString(element, _settings.FontFamily);
                break;
            case "fontWeight":
                _settings.FontWeight = NormalizeFontWeight(ReadString(element, _settings.FontWeight));
                break;
            case "foregroundColor":
                _settings.ForegroundColorMode = ForegroundColorMode.Custom;
                _settings.ForegroundColor = NormalizeColor(ReadString(element, _settings.ForegroundColor));
                break;
            case "foregroundColorMode":
                _settings.ForegroundColorMode = ReadForegroundColorMode(element, _settings.ForegroundColorMode);
                ApplyForegroundColorMode();
                break;
            case "backgroundOpacity":
                _settings.BackgroundOpacity = Math.Clamp(ReadDouble(element, _settings.BackgroundOpacity), 0, 1);
                break;
            case "windowWidth":
                _settings.WindowWidth = Math.Clamp(ReadDouble(element, _settings.WindowWidth), 320, 1400);
                break;
            case "horizontalAnchor":
                if (Enum.TryParse<LyricsHorizontalAnchor>(ReadString(element, _settings.HorizontalAnchor.ToString()), out var anchor))
                {
                    _settings.HorizontalAnchor = anchor;
                }
                break;
            case "xOffset":
                _settings.XOffset = Math.Clamp(ReadDouble(element, _settings.XOffset), -2000, 2000);
                break;
            case "yOffset":
                _settings.YOffset = Math.Clamp(ReadDouble(element, _settings.YOffset), -2000, 2000);
                break;
        }
    }

    private void ApplySourceOrder(JsonElement? value)
    {
        if (value is null || value.Value.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        _settings.SourceRecognitionOrder = NormalizeSourceOrder(value.Value.EnumerateArray()
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!));
    }

    private void ApplyFloatingWindowPlacement(JsonElement? value)
    {
        var corner = value?.ValueKind == JsonValueKind.String
            ? value.Value.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(corner))
        {
            return;
        }

        var workArea = SystemParameters.WorkArea;
        var width = Math.Clamp(_settings.FloatingWindowWidth > 0 ? _settings.FloatingWindowWidth : 560, 320, 1400);
        var height = Math.Clamp(_settings.FloatingWindowHeight > 0 ? _settings.FloatingWindowHeight : 72, 48, 260);

        _settings.EnableFloatingWindowMode = true;
        _settings.FloatingWindowWidth = width;
        _settings.FloatingWindowHeight = height;
        _settings.FloatingWindowLeft = corner switch
        {
            "topRight" or "bottomRight" => Math.Max(workArea.Left, workArea.Right - width),
            _ => workArea.Left
        };
        _settings.FloatingWindowTop = corner switch
        {
            "bottomLeft" or "bottomRight" => Math.Max(workArea.Top, workArea.Bottom - height),
            _ => workArea.Top
        };
    }

    private async Task PickForegroundColorAsync()
    {
        using var dialog = new Forms.ColorDialog
        {
            FullOpen = true
        };

        if (TryParseMediaColor(_settings.ForegroundColor, out var currentColor))
        {
            dialog.Color = Drawing.Color.FromArgb(currentColor.R, currentColor.G, currentColor.B);
        }

        if (dialog.ShowDialog() != Forms.DialogResult.OK)
        {
            return;
        }

        _settings.ForegroundColor = $"#FF{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        _settings.ForegroundColorMode = ForegroundColorMode.Custom;
        SaveSettings();
        await PushSettingsToWebAsync();
    }

    private void ApplyForegroundColorMode()
    {
        _settings.ForegroundColor = _settings.ForegroundColorMode switch
        {
            ForegroundColorMode.Dark => AppSettings.DarkForegroundColor,
            ForegroundColorMode.Light => AppSettings.LightForegroundColor,
            _ => _settings.ForegroundColor
        };
    }

    private void ClearLyricCache()
    {
        LyricProviderBase.ClearCache();
        GenericSmtcLyricProvider.ClearCache();
    }

    private void SaveSettings()
    {
        if (System.Windows.Application.Current is App app)
        {
            app.SaveSettings(_settings.Clone());
        }
    }

    private static bool ReadBool(JsonElement element, bool fallback)
    {
        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(element.GetString(), out var value) => value,
            _ => fallback
        };
    }

    private static double ReadDouble(JsonElement element, double fallback)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetDouble(out var value) => value,
            JsonValueKind.String when double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) => value,
            _ => fallback
        };
    }

    private static string ReadString(JsonElement element, string fallback)
    {
        return element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? fallback
            : fallback;
    }

    private static string NormalizeColor(string color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return AppSettings.LightForegroundColor;
        }

        var trimmed = color.Trim();
        return trimmed.Length == 7 && trimmed.StartsWith('#')
            ? $"#FF{trimmed[1..]}"
            : trimmed;
    }

    private static ForegroundColorMode ReadForegroundColorMode(JsonElement element, ForegroundColorMode fallback)
    {
        if (element.ValueKind == JsonValueKind.String &&
            Enum.TryParse<ForegroundColorMode>(element.GetString(), out var stringValue))
        {
            return stringValue;
        }

        if (element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out var intValue) &&
            Enum.IsDefined(typeof(ForegroundColorMode), intValue))
        {
            return (ForegroundColorMode)intValue;
        }

        return fallback;
    }

    private static bool TryParseMediaColor(string? color, out System.Windows.Media.Color parsedColor)
    {
        parsedColor = Colors.White;
        if (string.IsNullOrWhiteSpace(color))
        {
            return false;
        }

        try
        {
            if (System.Windows.Media.ColorConverter.ConvertFromString(color.Trim()) is System.Windows.Media.Color mediaColor)
            {
                parsedColor = mediaColor;
                return true;
            }
        }
        catch (FormatException)
        {
            return false;
        }

        return false;
    }

    private static void CopySettings(AppSettings source, AppSettings target)
    {
        target.SourceRecognitionOrder = source.SourceRecognitionOrder.ToList();
        target.EnableNetease = source.EnableNetease;
        target.EnableQQMusic = source.EnableQQMusic;
        target.EnableKugou = source.EnableKugou;
        target.EnableSpotify = source.EnableSpotify;
        target.ShowLyricsOnStartup = source.ShowLyricsOnStartup;
        target.StartWithWindows = source.StartWithWindows;
        target.ShowLyricTranslation = source.ShowLyricTranslation;
        target.EnablePureMusicSpectrum = source.EnablePureMusicSpectrum;
        target.UseLightSettingsWindow = source.UseLightSettingsWindow;
        target.FontSize = source.FontSize;
        target.FontFamily = source.FontFamily;
        target.FontWeight = source.FontWeight;
        target.ForegroundColorMode = source.ForegroundColorMode;
        target.ForegroundColor = source.ForegroundColor;
        target.ShowBackground = source.ShowBackground;
        target.BackgroundOpacity = source.BackgroundOpacity;
        target.UseCoverColorBackground = source.UseCoverColorBackground;
        target.UseCoverColorTaskbarLyrics = source.UseCoverColorTaskbarLyrics;
        target.ShowBorder = source.ShowBorder;
        target.ShowTextShadow = source.ShowTextShadow;
        target.EnableFloatingWindowMode = source.EnableFloatingWindowMode;
        target.WindowWidth = source.WindowWidth;
        target.FloatingWindowWidth = source.FloatingWindowWidth;
        target.FloatingWindowHeight = source.FloatingWindowHeight;
        target.FloatingWindowLeft = source.FloatingWindowLeft;
        target.FloatingWindowTop = source.FloatingWindowTop;
        target.HorizontalAnchor = source.HorizontalAnchor;
        target.XOffset = source.XOffset;
        target.YOffset = source.YOffset;
        target.EnableSmtcTimelineMonitor = source.EnableSmtcTimelineMonitor;
    }

    private void CaptionDragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            BeginNativeWindowDrag();
        }
    }

    private void BeginNativeWindowDrag()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        _ = ReleaseCapture();
        _ = SendMessage(hwnd, WindowMessageNonClientLeftButtonDown, HitTestCaption, 0);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WindowMessageNonClientHitTest || WindowState == WindowState.Maximized)
        {
            return IntPtr.Zero;
        }

        if (!GetWindowRect(hwnd, out var rect))
        {
            return IntPtr.Zero;
        }

        var x = GetSignedLowWord(lParam);
        var y = GetSignedHighWord(lParam);
        const int border = 8;

        var left = x >= rect.Left && x < rect.Left + border;
        var right = x <= rect.Right && x > rect.Right - border;
        var top = y >= rect.Top && y < rect.Top + border;
        var bottom = y <= rect.Bottom && y > rect.Bottom - border;

        handled = true;
        if (top && left) return new IntPtr(HitTestTopLeft);
        if (top && right) return new IntPtr(HitTestTopRight);
        if (bottom && left) return new IntPtr(HitTestBottomLeft);
        if (bottom && right) return new IntPtr(HitTestBottomRight);
        if (left) return new IntPtr(HitTestLeft);
        if (right) return new IntPtr(HitTestRight);
        if (top) return new IntPtr(HitTestTop);
        if (bottom) return new IntPtr(HitTestBottom);

        handled = false;
        return IntPtr.Zero;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximizeRestore();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMaximizeRestore()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        UpdateMaximizeRestoreIcon();
    }

    private void UpdateMaximizeRestoreIcon()
    {
        MaximizeRestoreIcon.Text = WindowState == WindowState.Maximized
            ? "\uE923"
            : "\uE922";
    }

    private void ApplyWindowChromeAttributes()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } compositionTarget })
        {
            compositionTarget.BackgroundColor = _useLightTheme
                ? System.Windows.Media.Color.FromRgb(247, 249, 252)
                : System.Windows.Media.Color.FromRgb(7, 14, 29);
        }

        var darkMode = _useLightTheme ? 0 : 1;
        _ = DwmSetWindowAttribute(hwnd, DwmWindowAttributeUseImmersiveDarkMode, ref darkMode, Marshal.SizeOf<int>());

        var cornerPreference = DwmWindowCornerPreferenceRound;
        _ = DwmSetWindowAttribute(hwnd, DwmWindowAttributeWindowCornerPreference, ref cornerPreference, Marshal.SizeOf<int>());
    }

    private void ApplySettingsWindowTheme()
    {
        var background = _useLightTheme
            ? System.Windows.Media.Color.FromRgb(247, 249, 252)
            : System.Windows.Media.Color.FromRgb(7, 14, 29);
        var foreground = _useLightTheme
            ? System.Windows.Media.Color.FromRgb(15, 23, 42)
            : System.Windows.Media.Color.FromRgb(248, 250, 252);
        var title = _useLightTheme
            ? System.Windows.Media.Color.FromArgb(150, 15, 23, 42)
            : System.Windows.Media.Color.FromArgb(122, 255, 255, 255);

        Background = new SolidColorBrush(background);
        Foreground = new SolidColorBrush(foreground);
        RootGrid.Background = new SolidColorBrush(background);
        CaptionBar.Background = new SolidColorBrush(background);
        CaptionTitle.Foreground = new SolidColorBrush(title);
        MinimizeIcon.Foreground = new SolidColorBrush(foreground);
        MaximizeRestoreIcon.Foreground = new SolidColorBrush(foreground);
        CloseIcon.Foreground = new SolidColorBrush(foreground);
        SettingsWebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(background.A, background.R, background.G, background.B);

        foreach (var button in CaptionButtons.Children.OfType<System.Windows.Controls.Button>())
        {
            button.Foreground = new SolidColorBrush(foreground);
        }

        ApplyWindowChromeAttributes();
    }

    private const int DwmWindowAttributeUseImmersiveDarkMode = 20;
    private const int DwmWindowAttributeWindowCornerPreference = 33;
    private const int DwmWindowCornerPreferenceRound = 2;
    private const int WindowMessageNonClientHitTest = 0x0084;
    private const int WindowMessageNonClientLeftButtonDown = 0x00A1;
    private const int HitTestCaption = 2;
    private const int HitTestLeft = 10;
    private const int HitTestRight = 11;
    private const int HitTestTop = 12;
    private const int HitTestTopLeft = 13;
    private const int HitTestTopRight = 14;
    private const int HitTestBottom = 15;
    private const int HitTestBottomLeft = 16;
    private const int HitTestBottomRight = 17;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll", PreserveSig = true)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", PreserveSig = true)]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, int wParam, int lParam);

    private static int GetSignedLowWord(IntPtr value)
    {
        return unchecked((short)((long)value & 0xFFFF));
    }

    private static int GetSignedHighWord(IntPtr value)
    {
        return unchecked((short)(((long)value >> 16) & 0xFFFF));
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRect
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
    }

    private sealed class WebSettingsMessage
    {
        public string? Type { get; set; }

        public string? Key { get; set; }

        public JsonElement? Value { get; set; }
    }

    private sealed class WebSettingsPayload
    {
        public List<string> SourceRecognitionOrder { get; set; } = new();
        public bool EnableNetease { get; set; }
        public bool EnableQQMusic { get; set; }
        public bool EnableKugou { get; set; }
        public bool EnableSpotify { get; set; }
        public bool ShowLyricsOnStartup { get; set; }
        public bool StartWithWindows { get; set; }
        public bool ShowLyricTranslation { get; set; }
        public bool EnablePureMusicSpectrum { get; set; }
        public bool UseLightSettingsWindow { get; set; }
        public double FontSize { get; set; }
        public string FontFamily { get; set; } = "";
        public string FontWeight { get; set; } = "";
        public ForegroundColorMode ForegroundColorMode { get; set; }
        public string ForegroundColor { get; set; } = "";
        public bool ShowBackground { get; set; }
        public double BackgroundOpacity { get; set; }
        public bool UseCoverColorBackground { get; set; }
        public bool UseCoverColorTaskbarLyrics { get; set; }
        public bool ShowBorder { get; set; }
        public bool ShowTextShadow { get; set; }
        public bool EnableFloatingWindowMode { get; set; }
        public double WindowWidth { get; set; }
        public double FloatingWindowWidth { get; set; }
        public double FloatingWindowHeight { get; set; }
        public LyricsHorizontalAnchor HorizontalAnchor { get; set; }
        public double XOffset { get; set; }
        public double YOffset { get; set; }
        public bool EnableSmtcTimelineMonitor { get; set; }
    }

    private sealed class FontOption
    {
        public string Value { get; set; } = "";

        public string Label { get; set; } = "";
    }
}
