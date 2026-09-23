using System.ComponentModel;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

public sealed partial class PowerplayAcquireSharedCluster : UserControl
{
    private PowerplayAcquireClusterViewModel? cluster;
    private string lastActivePath = "";
    private string lastInactivePath = "";

    public PowerplayAcquireSharedCluster()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ChangeCluster();
        LayoutUpdated += (_, _) => UpdateLinks();
    }

    private void ChangeCluster()
    {
        if (cluster is not null)
        {
            cluster.PropertyChanged -= OnClusterChanged;
        }

        cluster = DataContext as PowerplayAcquireClusterViewModel;
        if (cluster is not null)
        {
            cluster.PropertyChanged += OnClusterChanged;
        }

        lastActivePath = "";
        lastInactivePath = "";
        UpdateLinks();
    }

    private void OnClusterChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(PowerplayAcquireClusterViewModel.SelectedSellSystem))
        {
            lastActivePath = "";
            lastInactivePath = "";
            UpdateLinks();
        }
    }

    private void UpdateLinks()
    {
        if (cluster is null || LinkCanvas.Bounds.Width <= 0)
        {
            ActiveLinks.Data = null;
            InactiveLinks.Data = null;
            return;
        }

        Dictionary<PowerplayAcquireSellNode, double> sells = AnchorPositions<PowerplayAcquireSellNode>(
            SellItems,
            "SellLinkAnchor"
        );
        Dictionary<PowerplayAcquireMiningNode, double> mining = AnchorPositions<PowerplayAcquireMiningNode>(
            MiningItems,
            "MiningLinkAnchor"
        );
        var active = new StringBuilder();
        var inactive = new StringBuilder();
        for (int index = 0; index < cluster.SellNodes.Count; index++)
        {
            PowerplayAcquireSellNode sell = cluster.SellNodes[index];
            if (!sells.TryGetValue(sell, out double sourceY))
            {
                continue;
            }

            double[] destinations = cluster
                .VisibleMiningSystems.Where(system => system.ConnectsTo(sell.Row.Target))
                .Where(mining.ContainsKey)
                .Select(system => mining[system])
                .ToArray();
            if (destinations.Length == 0)
            {
                continue;
            }

            double lane = LinkCanvas.Bounds.Width * (0.25 + 0.5 * index / Math.Max(cluster.SellNodes.Count - 1, 1));
            AppendBranch(sell.IsSelected ? active : inactive, sourceY, destinations, lane, LinkCanvas.Bounds.Width);
        }

        SetPath(ActiveLinks, active.ToString(), ref lastActivePath);
        SetPath(InactiveLinks, inactive.ToString(), ref lastInactivePath);
    }

    private Dictionary<T, double> AnchorPositions<T>(ItemsControl items, string name)
        where T : class
    {
        var positions = new Dictionary<T, double>();
        foreach (
            Border border in items
                .GetVisualDescendants()
                .OfType<Border>()
                .Where(border => border.Name == name && border.DataContext is T)
        )
        {
            Point? point = border.TranslatePoint(new Point(0, border.Bounds.Height / 2), LinkCanvas);
            if (point is { } center)
            {
                positions.Add((T)border.DataContext!, center.Y);
            }
        }

        return positions;
    }

    private static void AppendBranch(
        StringBuilder path,
        double sourceY,
        IReadOnlyList<double> destinations,
        double lane,
        double width
    )
    {
        double first = Math.Min(sourceY, destinations.Min());
        double last = Math.Max(sourceY, destinations.Max());
        path.Append(CultureInfo.InvariantCulture, $" M 0,{sourceY:0.###} H {lane:0.###}");
        path.Append(CultureInfo.InvariantCulture, $" M {lane:0.###},{first:0.###} V {last:0.###}");
        foreach (double destination in destinations)
        {
            path.Append(CultureInfo.InvariantCulture, $" M {lane:0.###},{destination:0.###} H {width:0.###}");
        }
    }

    private static void SetPath(Avalonia.Controls.Shapes.Path shape, string data, ref string previous)
    {
        if (data == previous)
        {
            return;
        }

        previous = data;
        shape.Data = data.Length == 0 ? null : Geometry.Parse(data);
    }
}
