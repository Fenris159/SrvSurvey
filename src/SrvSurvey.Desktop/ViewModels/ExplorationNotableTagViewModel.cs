using SrvSurvey.Core.Exploration.History;

namespace SrvSurvey.Desktop.ViewModels;

public sealed record ExplorationNotableTagViewModel(string Label, string Description)
{
    private static readonly (ExplorationNotables Flag, string Label, string Description)[] Definitions =
    [
        (ExplorationNotables.EarthLikeWorld, "ELW", "Earth-like world"),
        (ExplorationNotables.WaterWorld, "WW", "Water world"),
        (ExplorationNotables.AmmoniaWorld, "AW", "Ammonia world"),
        (ExplorationNotables.TerraformableWorld, "TFC", "Terraformable world"),
        (ExplorationNotables.GreenGasGiant, "GGG", "Green gas giant"),
        (ExplorationNotables.StellarPhenomena, "NSP", "Notable stellar phenomena"),
        (ExplorationNotables.NeutronStar, "NS", "Neutron star"),
        (ExplorationNotables.BlackHole, "BH", "Black hole"),
    ];

    public static IReadOnlyList<ExplorationNotableTagViewModel> From(ExplorationNotables flags) =>
        Definitions
            .Where(entry => (flags & entry.Flag) != 0)
            .Select(entry => new ExplorationNotableTagViewModel(entry.Label, entry.Description))
            .ToArray();
}
