using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Exploration.History;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

public sealed partial class ExplorationHistoryPresentationTests
{
    [AvaloniaTheory]
    [InlineData("blue-dark", 1120, "en-US", "+500,000 CR")]
    [InlineData("blue-light", 760, "en-US", "+500,000 CR")]
    [InlineData("blue-light", 760, "de-DE", "+500.000 CR")]
    [InlineData("monochrome-dark", 900, "en-US", "+500,000 CR")]
    public async Task PaidReceiptUsesTheLedgerColumnsAndExposesItsDetailsInEachTheme(
        string theme,
        int width,
        string culture,
        string expectedPayout
    )
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        using var fixture = new Fixture(theme);
        await fixture.PrepareAsync();
        ExplorationHistoryViewModel model = fixture.Main.ExplorationHistory;
        model.SelectedStatus = ExplorationHistoryViewModel.StatusOptions.Single(option =>
            option.Status == ExplorationDataStatus.Paid
        );
        await model.PendingQuery;
        var view = new ExplorationHistoryView { DataContext = model };
        var window = new Window
        {
            Width = width,
            Height = 850,
            Content = view,
        };
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            SavePreview(frame, $"paid-ledger-{theme}-{width}");
            Border receipt = Assert.Single(
                view.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("history-row")
            );
            Assert.Contains("paid", receipt.Classes);
            TextBlock title = receipt
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(block => block.Text == "Exploration data sold");
            TextBlock value = receipt
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(block => block.Text == expectedPayout);
            TextBlock status = receipt.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == "Paid");
            Assert.Equal(2, Grid.GetColumnSpan(Assert.IsType<StackPanel>(title.Parent)));
            Assert.Equal(3, Grid.GetColumn(Assert.IsType<StackPanel>(value.Parent)));
            Assert.Equal(4, Grid.GetColumn(Assert.IsType<StackPanel>(status.Parent!.Parent)));
            Assert.True(title.IsEffectivelyVisible);
            Assert.True(value.IsEffectivelyVisible);
            Assert.True(status.IsEffectivelyVisible);
            Point valueAt = value.TranslatePoint(default, receipt)!.Value;
            Point statusAt = status.TranslatePoint(default, receipt)!.Value;
            Assert.True(valueAt.X + value.Bounds.Width <= statusAt.X);
            Assert.True(statusAt.X + status.Bounds.Width <= receipt.Bounds.Width);
            Assert.All(
                receipt.GetVisualDescendants().OfType<Button>(),
                button => Assert.False(button.IsEffectivelyVisible)
            );
            ListBox list = view.FindControl<ListBox>("ExplorationHistoryRows")!;
            Click(window, title);
            await model.PendingDetails;
            Assert.Same(Assert.Single(model.Rows), list.SelectedItem);
            Assert.True(model.HasPaymentSelection);
            Assert.Contains("500000", model.RawJson);
            using WriteableBitmap? details = window.CaptureRenderedFrame();
            SavePreview(details!, $"paid-details-{theme}-{width}");
            Assert.Equal(
                "Survey system 1, Survey system 2, Survey system 3",
                string.Join(", ", model.SelectedRow!.Data.Payment!.Systems.Take(3))
            );
        }
        finally
        {
            window.Close();
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [AvaloniaFact]
    public async Task SurveyResetButtonsStayTogetherBelowTheMetricsAndConfirmOnlyTheirOwnScope()
    {
        using var fixture = new Fixture("blue-dark");
        await fixture.PrepareAsync();
        var view = new ExplorationView { DataContext = fixture.Main };
        var window = new Window
        {
            Width = 760,
            Height = 900,
            Content = view,
        };
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            SavePreview(frame!, "granular-survey-resets-blue-dark");
            Button everything = view.FindControl<Button>("ResetAllSurveyTotals")!;
            Button odometer = view.FindControl<Button>("ResetSurveyOdometer")!;
            Button value = view.FindControl<Button>("ResetSurveyValue")!;
            Assert.Equal("Reset Everything Above", everything.Content);
            Assert.Equal("Reset Odometer", odometer.Content);
            Assert.Equal("Reset Exp Value", value.Content);
            Assert.Same(everything.Parent, odometer.Parent);
            Assert.Same(everything.Parent, value.Parent);
            Click(window, odometer);
            Assert.Equal("Confirm Reset Odometer", odometer.Content);
            Assert.Equal("Reset Everything Above", everything.Content);
            Click(window, value);
            Assert.Equal("Confirm Reset Exp Value", value.Content);
            Assert.Equal("Reset Odometer", odometer.Content);
            Assert.True(fixture.Main.IsResetExplorationPending);
        }
        finally
        {
            window.Close();
        }
    }
}
