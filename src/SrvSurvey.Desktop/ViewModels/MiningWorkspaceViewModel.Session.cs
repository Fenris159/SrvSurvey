using System.Windows.Input;
using SrvSurvey.Desktop.Input;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class MiningWorkspaceViewModel
{
    public bool ShouldShowSession => CanShowShipOverlays && Current is not null;
    public IReadOnlyList<InputBindingViewModel> SessionBindings { get; private set; } = [];

    internal void UseSessionBindings(GlobalInputSettingsViewModel input) =>
        SessionBindings = input
            .Bindings.Where(binding =>
                binding.Definition.Action
                    is GlobalInputAction.StartMiningSession
                        or GlobalInputAction.PauseResumeMiningSession
                        or GlobalInputAction.EndMiningSession
            )
            .ToArray();

    public bool ExecuteSessionControl(GlobalInputAction action)
    {
        ICommand? command = action switch
        {
            GlobalInputAction.StartMiningSession => StartCommand,
            GlobalInputAction.PauseResumeMiningSession => PauseCommand,
            GlobalInputAction.EndMiningSession => StopCommand,
            _ => null,
        };
        if (command?.CanExecute(null) != true)
        {
            return false;
        }

        command.Execute(null);
        return true;
    }
}
