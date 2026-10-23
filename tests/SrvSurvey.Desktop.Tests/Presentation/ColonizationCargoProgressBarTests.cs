using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Controls;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ColonizationCargoProgressBarTests
{
    [AvaloniaTheory]
    [InlineData(236304, 1196, 48)]
    [InlineData(100, 100, 20)]
    [InlineData(100, 0, 200)]
    [InlineData(0, 0, 0)]
    [InlineData(100, -10, -2)]
    public void NonzeroReadyCargoIsVisibleAndAllSegmentsFitWithinTheBar(long total, long delivered, long ready)
    {
        var bar = new ColonizationCargoProgressBar
        {
            Height = 10,
            Width = 1000,
            Cargo = new ColonizationNexusCargo(total, delivered, 0, ready, true),
            Background = Brushes.Gray,
            DeliveredBrush = Brushes.Gold,
            ReadyBrush = Brushes.Green,
        };
        var window = new Window
        {
            Content = bar,
            Width = 1100,
            Height = 120,
        };
        try
        {
            window.Show();
            Assert.NotNull(window.CaptureRenderedFrame());
            Assert.Same(Brushes.Gold, bar.DeliveredBrush);
            Assert.Same(Brushes.Green, bar.ReadyBrush);
            Assert.Same(Brushes.Gold, Assert.IsType<Border>(bar.Children[0]).Background);
            Assert.Same(Brushes.Green, Assert.IsType<Border>(bar.Children[1]).Background);
            Assert.InRange(Math.Abs(bar.ColumnDefinitions.Sum(column => column.ActualWidth) - bar.Bounds.Width), 0, 1);
            double readyWidth = bar.ColumnDefinitions[1].ActualWidth;
            Assert.Equal(total > 0 && delivered < total && ready > 0, readyWidth >= 3);
            if (total == 236304)
            {
                Assert.InRange(readyWidth, 3, 4);
                Assert.InRange(bar.ColumnDefinitions[0].ActualWidth, 5, 6);
            }
            bar.ReadyBrush = Brushes.Cyan;
            Assert.Same(Brushes.Cyan, Assert.IsType<Border>(bar.Children[1]).Background);
            bar.Cargo = null;
            Assert.NotNull(window.CaptureRenderedFrame());
            Assert.Null(bar.Cargo);
            Assert.Equal(0, bar.ColumnDefinitions[0].ActualWidth);
            Assert.Equal(0, bar.ColumnDefinitions[1].ActualWidth);
            Assert.Equal(1000, bar.ColumnDefinitions[2].ActualWidth);
        }
        finally
        {
            window.Close();
        }
    }
}
