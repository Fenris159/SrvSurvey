using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class GuardianOverlayCoordinator : IDisposable
{
    private readonly GuardianViewModel guardian;
    private readonly IOverlayPlatformService platform;
    private GuardianOverlayWindow? window;
    private bool disposed;

    public GuardianOverlayCoordinator(
        GuardianViewModel guardian,
        IOverlayPlatformService platform)
    {
        this.guardian = guardian
            ?? throw new ArgumentNullException(nameof(guardian));
        this.platform = platform
            ?? throw new ArgumentNullException(nameof(platform));
        this.guardian.PropertyChanged += OnGuardianPropertyChanged;
        SynchronizeWindow();
    }

    public bool IsVisible => window is not null;

    public string PlatformStatus => platform.Capabilities.StatusText;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        guardian.PropertyChanged -= OnGuardianPropertyChanged;
        CloseWindow();
    }

    private void OnGuardianPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(GuardianViewModel.HasActiveSite))
        {
            SynchronizeWindow();
        }
    }

    private void SynchronizeWindow()
    {
        if (disposed
            || !guardian.HasActiveSite
            || !platform.Capabilities.SupportsPassiveOverlay
            || !platform.Capabilities.SupportsClickThrough)
        {
            CloseWindow();
            return;
        }

        if (window is not null)
        {
            return;
        }

        var viewModel = new GuardianOverlayViewModel(
            guardian,
            platform.Capabilities);
        var overlay = new GuardianOverlayWindow(viewModel);
        overlay.Opened += (_, _) =>
        {
            PositionWindow(overlay);
            var preparation = platform.PreparePassiveWindow(overlay);
            viewModel.ApplyPreparation(preparation);
            if (!preparation.IsClickThrough)
            {
                CloseWindow();
            }
        };
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(window, overlay))
            {
                window = null;
            }
        };
        window = overlay;
        overlay.Show();
    }

    private static void PositionWindow(Window window)
    {
        var screen = window.Screens.ScreenFromWindow(window)
            ?? window.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        const int margin = 20;
        var width = (int)Math.Ceiling(window.Width * screen.Scaling);
        var height = (int)Math.Ceiling(window.Height * screen.Scaling);
        window.Position = new PixelPoint(
            Math.Max(
                screen.WorkingArea.X + margin,
                screen.WorkingArea.Right - width - margin),
            Math.Max(
                screen.WorkingArea.Y + margin,
                screen.WorkingArea.Bottom - height - margin));
    }

    private void CloseWindow()
    {
        var overlay = window;
        window = null;
        overlay?.Close();
    }
}
