using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class MiningIconTests
{
    [AvaloniaFact]
    public void PowerStateAndStationArtworkLoads()
    {
        foreach (
            string kind in new[]
            {
                "Stronghold",
                "Fortified",
                "Exploited",
                "Unoccupied",
                "Expansion",
                "Contested",
                "Planet",
                "RingIcy",
                "RingRocky",
                "RingMetallic",
                "RingMetalRich",
                "Hotspot",
                "ReservePristine",
                "ReserveMajor",
                "ReserveUnknown",
                "Coriolis",
                "Orbis",
                "Ocellus",
                "Asteroid",
                "SurfacePort",
                "Settlement",
                "Outpost",
            }
        )
        {
            var icon = new MiningIcon
            {
                Kind = kind,
                Accent = "Archon Delaine",
                Width = 16,
                Height = 16,
            };
            var window = new Window
            {
                Content = icon,
                Width = 40,
                Height = 40,
            };
            try
            {
                window.Show();
                Assert.True(icon.Bounds.Width > 0);
            }
            finally
            {
                window.Close();
            }
        }
    }
}
