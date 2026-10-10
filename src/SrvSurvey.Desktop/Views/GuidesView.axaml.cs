using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

/// <summary>The task reader with expandable navigation and a persistent search field.</summary>
public sealed partial class GuidesView : UserControl
{
    private GuidesViewModel? guides;

    /// <summary>Loads the offline Guides interface.</summary>
    public GuidesView()
    {
        InitializeComponent();
    }

    /// <summary>Moves the reader subscription when its application context changes.</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        guides?.PropertyChanged -= OnGuideChanged;
        guides = (DataContext as MainWindowViewModel)?.Guides;
        guides?.PropertyChanged += OnGuideChanged;
    }

    /// <summary>Starts a newly opened subject or changed search at the top of its content.</summary>
    private void OnGuideChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GuidesViewModel.SelectedTopic) or nameof(GuidesViewModel.SearchText))
        {
            GuideReader.Offset = new Vector();
        }
        if (e.PropertyName == nameof(GuidesViewModel.SelectedTopic))
        {
            Dispatcher.UIThread.Post(ShowSelectedTopic, DispatcherPriority.Loaded);
        }
    }

    /// <summary>Keeps the selected navigation item in view after the accordion has laid out.</summary>
    private void ShowSelectedTopic()
    {
        this.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(button => button.DataContext is GuideTopicViewModel { IsSelected: true })
            ?.BringIntoView();
    }
}
