using System.Windows.Input;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class GuardianZoomOverlayViewModel
{
    public GuardianZoomOverlayViewModel(Action<bool> adjustZoom)
    {
        ArgumentNullException.ThrowIfNull(adjustZoom);
        ZoomInCommand = new DelegateCommand(() => adjustZoom(true));
        ZoomOutCommand = new DelegateCommand(() => adjustZoom(false));
    }

    public ICommand ZoomInCommand { get; }

    public ICommand ZoomOutCommand { get; }

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
