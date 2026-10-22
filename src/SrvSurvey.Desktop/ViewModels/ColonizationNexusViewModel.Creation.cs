using System.Windows.Input;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ColonizationNexusViewModel
{
    private readonly WorkspaceCommand createCommand;
    private Action? createOpener;
    private readonly WorkspaceCommand editCommand;
    private Action? editOpener;
    public ICommand EditNexusCommand => editCommand;
    private bool CanEditSelected =>
        CanLoad
        && !IsBusy
        && reader is IRavenColonialNexusWriter
        && Detail is { } current
        && (
            string.Equals(current.Nexus.Owner, commander, StringComparison.OrdinalIgnoreCase)
            || current.Nexus.Commanders.Contains(commander!, StringComparer.OrdinalIgnoreCase)
        );

    internal void SetEditOpener(Action? opener)
    {
        editOpener = opener;
        editCommand.Refresh();
    }

    internal event Action<string>? NexusDeleted;
    private int contextVersion;
    internal event Action? ContextChanged;
    public ICommand CreateNexusCommand => createCommand;

    internal void SetCreateOpener(Action? opener)
    {
        createOpener = opener;
        createCommand.Refresh();
    }

    internal ColonizationNexusEditorViewModel CreateEditor(
        IReadOnlyList<ColonizationFleetCarrier> carriers,
        bool edit = false
    )
    {
        if (!CanLoad || reader is not IRavenColonialNexusWriter writer || edit && !CanEditSelected)
        {
            throw new InvalidOperationException("Load a commander and save a Raven API key before creating a Nexus.");
        }
        int version = contextVersion;
        string? editedId = edit ? Detail!.Nexus.Id : null;
        return new ColonizationNexusEditorViewModel(
            writer,
            commander!,
            apiKey!,
            () => CanLoad && contextVersion == version,
            async nexus =>
            {
                if (CanLoad && contextVersion == version)
                {
                    var summary = new ColonizationNexusSummary
                    {
                        Id = nexus.Id,
                        Name = nexus.Name,
                        Owner = nexus.Owner,
                        Open = nexus.Open,
                    };
                    Nexuses = Nexuses.Where(item => item.Id != nexus.Id).Append(summary).ToArray();
                    selectedNexus = summary;
                    Changed(nameof(SelectedNexus));
                    Detail = MakeDetail(nexus);
                    await RefreshAsync();
                }
            },
            carriers,
            edit ? Detail!.Nexus : null
        )
        {
            OnDeleted = async () =>
            {
                if (CanLoad && contextVersion == version && edit)
                {
                    await RemoveDeletedAsync(editedId!);
                }
            },
        };
    }

    private async Task RemoveDeletedAsync(string id)
    {
        Nexuses = Nexuses.Where(item => item.Id != id).ToArray();
        if (selectedNexus?.Id == id)
        {
            selectedNexus = null;
            Changed(nameof(SelectedNexus));
            Detail = null;
        }
        NexusDeleted?.Invoke(id);
        await RefreshAsync();
    }
}
