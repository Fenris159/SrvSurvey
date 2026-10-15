using Avalonia.Controls;
using Avalonia.Interactivity;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class SpanshSearchWindow : Window
{
    private readonly SpanshSearchViewModel viewModel;

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

    public SpanshSearchWindow(SpanshSearchViewModel viewModel)
    {
        this.viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    private async void Search_Click(object? sender, RoutedEventArgs e)
    {
        if (await viewModel.SearchAsync())
        {
            Close();
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => viewModel.Cancel();

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        viewModel.Cancel();
        base.OnClosed(e);
    }
}
