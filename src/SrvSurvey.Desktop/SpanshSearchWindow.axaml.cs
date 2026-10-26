using Avalonia.Controls;
using Avalonia.Interactivity;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class SpanshSearchWindow : Window
{
    private readonly SpanshSearchViewModel viewModel;

    /// <summary>
    /// Initializes a standalone planner window with its workspace search model.
    /// </summary>
    public SpanshSearchWindow()
        : this(
            new RouteWorkspaceViewModel(
                new FollowRouteService(
                    new FollowRouteStore(Path.Combine(Path.GetTempPath(), "SrvSurvey-Spansh-Design"))
                ),
                new RouteNameImporter(new SpanshStarSystemResolver()),
                new SpanshRouteClient(),
                plannerClient: new SpanshPlannerClient()
            ).SpanshSearch!
        ) { }

    /// <summary>
    /// Initializes a standalone planner window with its workspace search model.
    /// </summary>
    public SpanshSearchWindow(SpanshSearchViewModel viewModel)
    {
        this.viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>Reloads the latest ship range when a retained search form is reopened.</summary>
    protected override void OnOpened(EventArgs e)
    {
        viewModel.PrepareForOpen();
        base.OnOpened(e);
    }

    /// <summary>
    /// Submits the current form and closes the window after a successful itinerary import.
    /// </summary>
    private async void Search_Click(object? sender, RoutedEventArgs e)
    {
        if (await viewModel.SearchAsync())
        {
            Close();
        }
    }

    /// <summary>
    /// Cancels the current planner request and result polling.
    /// </summary>
    private void Cancel_Click(object? sender, RoutedEventArgs e) => viewModel.Cancel();

    /// <summary>
    /// Closes the search dialog through its normal cancellation lifecycle.
    /// </summary>
    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Cancels outstanding work when the search dialog closes.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        viewModel.Cancel();
        base.OnClosed(e);
    }
}
