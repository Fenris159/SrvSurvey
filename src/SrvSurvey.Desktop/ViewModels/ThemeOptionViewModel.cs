using System.Windows.Input;
using Avalonia.Media;
using SrvSurvey.Desktop.Theming;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class ThemeOptionViewModel
{
    public ThemeOptionViewModel(
        RavenThemeDefinition definition,
        Action<ThemeOptionViewModel> select)
    {
        Definition = definition;
        WindowBrush = Brush.Parse(definition.WindowColor);
        SurfaceBrush = Brush.Parse(definition.RaisedSurfaceColor);
        AccentBrush = Brush.Parse(definition.AccentColor);
        TextBrush = Brush.Parse(definition.TextColor);
        SelectCommand = new DelegateCommand(() => select(this));
    }

    public RavenThemeDefinition Definition { get; }

    public string DisplayName => Definition.DisplayName;

    public string ModeLabel => Definition.IsDark ? "DARK" : "LIGHT";

    public IBrush WindowBrush { get; }

    public IBrush SurfaceBrush { get; }

    public IBrush AccentBrush { get; }

    public IBrush TextBrush { get; }

    public ICommand SelectCommand { get; }

    private sealed class DelegateCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => execute();
    }
}
