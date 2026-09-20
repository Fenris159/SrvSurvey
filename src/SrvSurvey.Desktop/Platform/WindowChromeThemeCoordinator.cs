using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Media;
using SrvSurvey.Desktop.Theming;

namespace SrvSurvey.Desktop.Platform;

internal readonly record struct WindowChromeThemePalette(Color Caption, Color Border, Color Text)
{
    private const double InactiveCaptionFade = 0.35;
    private const double InactiveBorderFade = 0.50;

    public static WindowChromeThemePalette Create(RavenThemeDefinition theme, bool isActive)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var window = Color.Parse(theme.WindowColor);
        var caption = Color.Parse(theme.AccentMutedColor);
        var border = Color.Parse(theme.StrongBorderColor);
        return new WindowChromeThemePalette(
            isActive ? caption : Mix(caption, window, InactiveCaptionFade),
            isActive ? border : Mix(border, window, InactiveBorderFade),
            Color.Parse(isActive ? theme.TextColor : theme.MutedTextColor)
        );
    }

    private static Color Mix(Color source, Color target, double amount)
    {
        static byte Blend(byte source, byte target, double amount) =>
            (byte)Math.Round(source + ((target - source) * amount));

        return Color.FromRgb(
            Blend(source.R, target.R, amount),
            Blend(source.G, target.G, amount),
            Blend(source.B, target.B, amount)
        );
    }
}

internal interface IWindowChromeThemePlatform
{
    void Prepare(Window window);

    void Apply(Window window, WindowChromeThemePalette palette);
}

internal sealed class WindowChromeThemeCoordinator : IDisposable
{
    private readonly Window window;
    private readonly RavenThemeService themeService;
    private readonly IWindowChromeThemePlatform platform;
    private bool isOpen;
    private bool disposed;

    public WindowChromeThemeCoordinator(Window window, RavenThemeService themeService)
        : this(window, themeService, CreateCurrentPlatform()) { }

    internal WindowChromeThemeCoordinator(
        Window window,
        RavenThemeService themeService,
        IWindowChromeThemePlatform platform
    )
    {
        this.window = window ?? throw new ArgumentNullException(nameof(window));
        this.themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        this.platform = platform ?? throw new ArgumentNullException(nameof(platform));
        platform.Prepare(window);
        window.Opened += HandleOpened;
        window.Activated += HandleActivated;
        window.Deactivated += HandleDeactivated;
        window.Closed += HandleClosed;
        themeService.ThemeChanged += HandleThemeChanged;
    }

    internal void Apply(bool isActive)
    {
        platform.Apply(window, WindowChromeThemePalette.Create(themeService.Current, isActive));
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        window.Opened -= HandleOpened;
        window.Activated -= HandleActivated;
        window.Deactivated -= HandleDeactivated;
        window.Closed -= HandleClosed;
        themeService.ThemeChanged -= HandleThemeChanged;
    }

    private void HandleOpened(object? sender, EventArgs eventArgs)
    {
        isOpen = true;
        Apply(window.IsActive);
    }

    private void HandleActivated(object? sender, EventArgs eventArgs)
    {
        Apply(isActive: true);
    }

    private void HandleDeactivated(object? sender, EventArgs eventArgs)
    {
        Apply(isActive: false);
    }

    private void HandleClosed(object? sender, EventArgs eventArgs)
    {
        isOpen = false;
        Dispose();
    }

    private void HandleThemeChanged(object? sender, EventArgs eventArgs)
    {
        if (isOpen)
        {
            Apply(window.IsActive);
        }
    }

    private static IWindowChromeThemePlatform CreateCurrentPlatform() =>
        OperatingSystem.IsLinux() ? LinuxWindowChromeThemePlatform.Instance : WindowsWindowChromeThemePlatform.Instance;
}

internal sealed class LinuxWindowChromeThemePlatform : IWindowChromeThemePlatform
{
    internal const string ChromeTextBrushKey = nameof(ChromeResourceKey.SrvSurveyWindowChromeTextBrush);
    private const string CaptionBackgroundBrushKey = nameof(ChromeResourceKey.TitleBarBackgroundBrush);
    private const string CaptionButtonBackgroundBrushKey = nameof(ChromeResourceKey.CaptionButtonBackground);
    private const string CaptionButtonBorderBrushKey = nameof(ChromeResourceKey.CaptionButtonBorderBrush);
    private const string CaptionButtonForegroundBrushKey = nameof(ChromeResourceKey.CaptionButtonForeground);
    private const string FullscreenCaptionBackgroundBrushKey = nameof(
        ChromeResourceKey.SystemControlBackgroundChromeMediumLowBrush
    );
    private const string FrameBrushKey = nameof(ChromeResourceKey.SystemControlForegroundBaseMediumBrush);

    public static LinuxWindowChromeThemePlatform Instance { get; } = new();

    private LinuxWindowChromeThemePlatform() { }

    public void Prepare(Window window) { }

    public void Apply(Window window, WindowChromeThemePalette palette)
    {
        window.Resources[CaptionBackgroundBrushKey] = new SolidColorBrush(palette.Caption);
        window.Resources[FullscreenCaptionBackgroundBrushKey] = new SolidColorBrush(palette.Caption);
        window.Resources[FrameBrushKey] = new SolidColorBrush(palette.Border);
        window.Resources[CaptionButtonForegroundBrushKey] = new SolidColorBrush(palette.Text);
        window.Resources[ChromeTextBrushKey] = new SolidColorBrush(palette.Text);
        window.Resources[CaptionButtonBackgroundBrushKey] = CreateTranslucentBrush(palette.Border, 0x66);
        window.Resources[CaptionButtonBorderBrushKey] = CreateTranslucentBrush(palette.Border, 0x99);
    }

    private static SolidColorBrush CreateTranslucentBrush(Color color, byte alpha) =>
        new(Color.FromArgb(alpha, color.R, color.G, color.B));

    private enum ChromeResourceKey
    {
        CaptionButtonBackground,
        CaptionButtonBorderBrush,
        CaptionButtonForeground,
        SrvSurveyWindowChromeTextBrush,
        SystemControlBackgroundChromeMediumLowBrush,
        SystemControlForegroundBaseMediumBrush,
        TitleBarBackgroundBrush,
    }
}

internal sealed partial class WindowsWindowChromeThemePlatform : IWindowChromeThemePlatform
{
    private const int BorderColorAttribute = 34;
    private const int CaptionColorAttribute = 35;
    private const int TextColorAttribute = 36;

    public static WindowsWindowChromeThemePlatform Instance { get; } = new();

    private WindowsWindowChromeThemePlatform() { }

    public void Prepare(Window window) { }

    public void Apply(Window window, WindowChromeThemePalette palette)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        nint handle = window.TryGetPlatformHandle()?.Handle ?? nint.Zero;
        if (handle == nint.Zero)
        {
            return;
        }

        SetColor(handle, CaptionColorAttribute, palette.Caption);
        SetColor(handle, BorderColorAttribute, palette.Border);
        SetColor(handle, TextColorAttribute, palette.Text);
    }

    private static void SetColor(nint window, int attribute, Color color)
    {
        uint colorReference = color.R | ((uint)color.G << 8) | ((uint)color.B << 16);
        _ = DwmSetWindowAttribute(window, attribute, ref colorReference, sizeof(uint));
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint window, int attribute, ref uint value, int valueSize);
}
