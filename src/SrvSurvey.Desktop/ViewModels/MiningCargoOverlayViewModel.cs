using System.ComponentModel;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Desktop.ViewModels;

public sealed record MiningCargoOverlayItemViewModel(string Name, int Count, bool IsTarget)
{
    public string CountLabel => Count.ToString("N0", global::System.Globalization.CultureInfo.CurrentCulture);
}

public sealed class MiningCargoOverlayViewModel : WorkspaceObservable, IDisposable
{
    private readonly MiningWorkspaceViewModel? workspace;

    public MiningCargoOverlayViewModel(MiningWorkspaceViewModel? workspace)
    {
        this.workspace = workspace;
        workspace?.PropertyChanged += OnChanged;
    }

    public string Capacity => ReadCapacity();
    public string Remaining => workspace is null ? "529 T REMAINING" : $"{workspace.CargoRemaining:N0} T REMAINING";
    public double FillPercentage => workspace?.CargoFillPercentage ?? 44.9;
    public IReadOnlyList<MiningCargoOverlayItemViewModel> Items =>
        workspace is null ? PreviewItems : ReadItems(workspace);
    public bool HasItems => Items.Count > 0;

    private static readonly MiningCargoOverlayItemViewModel[] PreviewItems =
    [
        new("Limpets", 167, false),
        new("Platinum", 243, true),
        new("Osmium", 21, true),
    ];

    private string ReadCapacity()
    {
        if (workspace is null)
        {
            return "431 / 960 T";
        }

        return workspace.CargoCapacity > 0
            ? $"{workspace.CargoUsed:N0} / {workspace.CargoCapacity:N0} T"
            : $"{workspace.CargoUsed:N0} T / UNKNOWN";
    }

    private static MiningCargoOverlayItemViewModel[] ReadItems(MiningWorkspaceViewModel workspace) =>
        workspace
            .Cargo.Where(item => item.Count > 0)
            .Select(item => new MiningCargoOverlayItemViewModel(
                DisplayName(item),
                item.Count,
                workspace.Settings.Thresholds.ContainsKey(item.Name)
            ))
            .OrderByDescending(item => item.Name == "Limpets")
            .ThenByDescending(item => item.IsTarget)
            .ThenByDescending(item => item.Count)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    private static string DisplayName(CargoItem item)
    {
        if (item.Name.Equals("drones", StringComparison.OrdinalIgnoreCase))
        {
            return "Limpets";
        }

        string name = string.IsNullOrWhiteSpace(item.LocalizedName) ? item.Name : item.LocalizedName;
        return global::System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name);
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (
            e.PropertyName
            is not (
                nameof(MiningWorkspaceViewModel.Cargo)
                or nameof(MiningWorkspaceViewModel.CargoUsed)
                or nameof(MiningWorkspaceViewModel.CargoCapacity)
                or nameof(MiningWorkspaceViewModel.CargoRemaining)
                or nameof(MiningWorkspaceViewModel.CargoFillPercentage)
                or nameof(MiningWorkspaceViewModel.Settings)
            )
        )
        {
            return;
        }

        foreach (
            string property in new[]
            {
                nameof(Capacity),
                nameof(Remaining),
                nameof(FillPercentage),
                nameof(Items),
                nameof(HasItems),
            }
        )
        {
            Changed(property);
        }
    }

    public void Dispose() => workspace?.PropertyChanged -= OnChanged;
}
