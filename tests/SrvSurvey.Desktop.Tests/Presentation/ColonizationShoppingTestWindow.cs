using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

/// <summary>Hosts the embedded market surface for focused headless interaction tests.</summary>
internal sealed class ColonizationShoppingTestWindow : Window
{
    private readonly ColonizationShoppingView view;

    public ColonizationShoppingTestWindow()
        : this(new ColonizationShoppingViewModel([], () => null)) { }

    public ColonizationShoppingTestWindow(ColonizationShoppingViewModel model)
    {
        DataContext = model;
        view = new ColonizationShoppingView { DataContext = model };
        Content = new ScrollViewer
        {
            Content = view,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        Width = 700;
        Height = 660;
        MinWidth = 640;
        MinHeight = 480;
        Closed += (_, _) => model.Dispose();
    }

    public T? FindControl<T>(string name)
        where T : Control => view.FindControl<T>(name);
}
