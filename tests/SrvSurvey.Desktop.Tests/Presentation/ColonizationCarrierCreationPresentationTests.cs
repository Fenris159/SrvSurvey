using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

public sealed partial class ColonizationShoppingPresentationTests
{
    [AvaloniaTheory]
    [InlineData(820)]
    [InlineData(1180)]
    public async Task CarrierCreationFormOffersOptInAllOrOneCarrierAndReviewsTheChosenCount(int width)
    {
        using var http = new HttpClient(
            new RavenHandler
            {
                SystemArchitect = "Example Cmdr",
                LinkedCarriers =
                [
                    new()
                    {
                        MarketId = 42,
                        Name = "N4W-T0Z",
                        DisplayName = "DIRE ECLIPSE",
                    },
                    new()
                    {
                        MarketId = 43,
                        Name = "BBB-222",
                        DisplayName = "Colony supplies",
                    },
                ],
            }
        );
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithExternalNetworkClient(http)
        );
        main.Colonization.IsEnabled = true;
        await main.Colonization.SetCommanderAsync("Example Cmdr");
        ColonizationProjectEditorViewModel editor = main.Colonization.ProjectEditor;
        editor.UpdateContext(
            new(
                true,
                "Example Cmdr",
                "Sol",
                [1, 2, 3],
                new(10, 42, "Sol", "Orbital Construction Site: Hope", null, ["colonisationcontribution"]),
                new(
                    DateTimeOffset.Parse("2026-11-05T12:00:00Z", CultureInfo.InvariantCulture),
                    10,
                    0,
                    false,
                    false,
                    [new("steel", "Steel", 100, 0, 1)]
                ),
                CurrentBodyId: 12,
                CurrentBodyName: "Sol 1"
            )
        );
        await editor.PrepareAsync();
        editor.SelectedBuild = editor.BuildOptions.Single(option => option.Build.BuildType == "no_truss");
        editor.SelectedLayout = "no_truss";
        var view = new ColonizationView { DataContext = main };
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = 900,
        };
        try
        {
            window.Show();
            CheckBox include = view.FindControl<CheckBox>("LinkBuildCarriersCheckBox")!;
            CarrierMultiSelect selector = view.FindControl<CarrierMultiSelect>("BuildCarrierSelector")!;
            Assert.False(include.IsChecked);
            Assert.True(include.IsEffectivelyEnabled);
            Assert.False(selector.IsEffectivelyEnabled);
            Assert.Equal(new long[] { 0, 43, 42 }, editor.CarrierOptions.Select(choice => choice.MarketId));
            include.IsChecked = true;
            Assert.True(selector.IsEffectivelyEnabled);
            Assert.Equal("Link 2 carriers after this build is created.", editor.CarrierLinkHint);
            SetCarrierChecks(selector, 43);
            await editor.ReviewAsync();
            Assert.True(editor.IsConfirmationPending);
            Assert.Contains("Link 1 carrier after creation.", editor.ConfirmationSummary);
            SetCarrierChecks(selector, 0);
            Assert.False(editor.IsConfirmationPending);
            await editor.ReviewAsync();
            Assert.Contains("Link 2 carriers after creation.", editor.ConfirmationSummary);
            view.FindControl<StackPanel>("BuildCarrierLinkOptions")!.BringIntoView();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            WrapPanel row = Assert.IsType<WrapPanel>(selector.Parent);
            Assert.True(include.Bounds.Right <= row.Bounds.Width + 1);
            Assert.True(selector.Bounds.Right <= row.Bounds.Width + 1);
            frame.Save(
                Path.Combine(Path.GetTempPath(), $"srvsurvey-carrier-creation-{width}.png"),
                PngBitmapEncoderOptions.Default
            );
            SetCarrierChecks(selector, 43);
            await editor.ReviewAsync();
            editor.UpdateCarrierOptions([
                new()
                {
                    MarketId = 42,
                    Name = "N4W-T0Z",
                    DisplayName = "DIRE ECLIPSE",
                },
            ]);
            using WriteableBitmap? refreshed = window.CaptureRenderedFrame();
            Assert.Equal(43, editor.SelectedCreationCarrierMarketId);
            Assert.False(editor.IsConfirmationPending);
            await editor.ReviewAsync();
            Assert.False(editor.IsConfirmationPending);
            Assert.Contains("Choose an available carrier", editor.StatusMessage);
        }
        finally
        {
            window.Close();
        }
    }
}
