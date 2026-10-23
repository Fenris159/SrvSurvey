using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.Runtime;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

public sealed partial class ColonizationNexusView : UserControl
{
    public ColonizationNexusView() => InitializeComponent();

    private void SystemRemovalPrompt_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == IsVisibleProperty && sender is Border { IsVisible: true })
        {
            this.FindControl<Button>("CancelNexusSystemRemoval")?.Focus();
        }
    }

    private void NexusRoot_KeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Escape && DataContext is ColonizationNexusViewModel { IsSystemRemovalVisible: true } model)
        {
            model.CancelSystemRemovalCommand.Execute(null);
            args.Handled = true;
        }
    }

    private static void SystemsPanel_SizeChanged(object? sender, SizeChangedEventArgs args)
    {
        if (sender is WrapPanel panel)
        {
            panel.ItemWidth = Math.Floor(args.NewSize.Width / (args.NewSize.Width >= 560 ? 2 : 1));
        }
    }

    private async void OpenRavenLink_Click(object? sender, RoutedEventArgs args)
    {
        if (!DesktopExternalEffectPolicy.IsAllowed || sender is not Button { Tag: Uri uri })
        {
            return;
        }
        try
        {
            if (TopLevel.GetTopLevel(this)?.Launcher is not { } launcher || !await launcher.LaunchUriAsync(uri))
            {
                ReportLinkFailure();
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            ReportLinkFailure();
        }
    }

    private void ReportLinkFailure()
    {
        if (DataContext is ColonizationNexusViewModel model)
        {
            model.ReportLinkFailure();
        }
    }
}
