using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

/// <summary>Codex search criteria and matching locations, independent of other search workspaces.</summary>
public sealed partial class CodexSearchView : UserControl
{
    private NearestSystemsViewModel? model;
    private bool attached;

    public CodexSearchView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
        {
            attached = true;
            ConnectModel();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            attached = false;
            ConnectModel();
        };
        DataContextChanged += (_, _) => ConnectModel();
    }

    private void ConnectModel()
    {
        model?.PropertyChanged -= OnModelChanged;
        model = attached ? DataContext as NearestSystemsViewModel : null;
        model?.PropertyChanged += OnModelChanged;
        if (model is { IsSearching: true } or { HasResults: true })
        {
            ShowCriteria();
        }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(NearestSystemsViewModel.IsSearching) && model?.IsSearching == true)
        {
            ShowCriteria();
        }
    }

    private void ShowCriteria() =>
        Dispatcher.UIThread.Post(
            () =>
            {
                if (attached)
                {
                    double height =
                        this.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault()?.Viewport.Height
                        ?? Bounds.Height;
                    this.BringIntoView(new Rect(0, 0, Bounds.Width, Math.Min(Bounds.Height, height)));
                }
            },
            DispatcherPriority.Loaded
        );
}
