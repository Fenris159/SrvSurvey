using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

public sealed partial class OverlaySettingsView : UserControl
{
    private readonly OverlaySettingsCategory category;

    public OverlaySettingsView()
        : this(OverlaySettingsCategory.Global)
    {
    }

    public OverlaySettingsView(OverlaySettingsCategory category)
    {
        this.category = category;
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        ApplyCategory(category);
    }

    private void OnDataContextChanged(object? sender, EventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            PanelVisibilityItems.ItemsSource = null;
            return;
        }

        var panels = viewModel.OverlayPanelVisibility.ForCategory(category);
        PanelVisibilityItems.ItemsSource = panels;
        PanelVisibilityCard.IsVisible = panels.Count > 0;
    }

    private void ApplyCategory(OverlaySettingsCategory category)
    {
        var isGlobal = category == OverlaySettingsCategory.Global;
        MiningShortcutsCard.IsVisible = category == OverlaySettingsCategory.Mining;
        PassiveNotificationCard.IsVisible = isGlobal;
        GlobalOverlayBehaviorCard.IsVisible = isGlobal;
        PulseOverlayCard.IsVisible = isGlobal;
        StreamOverlayCard.IsVisible = isGlobal;
        OpenVrOverlayCard.IsVisible = isGlobal;

        GalaxyMapCard.IsVisible = category == OverlaySettingsCategory.Exploration;
        BoxelOverlayCard.IsVisible = category == OverlaySettingsCategory.Boxel;
        CombatOverlayCard.IsVisible = category == OverlaySettingsCategory.Quests;
        GuardianOverlayCard.IsVisible = category == OverlaySettingsCategory.Guardian;
        StationInformationCard.IsVisible = category == OverlaySettingsCategory.Travel;
        HumanSettlementCard.IsVisible = category == OverlaySettingsCategory.Quests;
        HumanTemplateAuthoringExpander.IsVisible =
            category == OverlaySettingsCategory.Quests;
        JumpInformationCard.IsVisible = category == OverlaySettingsCategory.Travel;
        ColonizationShoppingCard.IsVisible =
            category == OverlaySettingsCategory.Colonization;

        var isExploration = category == OverlaySettingsCategory.Exploration;
        var isExobiology = category == OverlaySettingsCategory.Exobiology;
        SystemSurveyCard.IsVisible = isExploration || isExobiology;
        ExplorationSurveyGrid.IsVisible = isExploration;
        BodyInformationSeparator.IsVisible = isExploration;
        BodyInformationGrid.IsVisible = isExploration;
        ExobiologyExternalDataSeparator.IsVisible = isExobiology;
        ExobiologyExternalDataGrid.IsVisible = isExobiology;
        SurfaceRadarSeparator.IsVisible = isExobiology;
        SurfaceRadarPanel.IsVisible = isExobiology;
        ExobiologySurveySeparator.IsVisible = isExobiology;
        ExobiologySurveyGrid.IsVisible = isExobiology;
        BiologyRewardSeparator.IsVisible = isExobiology;
        BiologyRewardPanel.IsVisible = isExobiology;

        if (isGlobal)
        {
            return;
        }

        var definition = OverlaySettingsCategoryCatalog.All.Single(candidate =>
            candidate.Category == category);
        OverlaySettingsEyebrow.Text = $"{definition.Eyebrow} OVERLAYS";
        OverlaySettingsTitle.Text = $"{definition.DisplayName} overlay settings";
        OverlaySettingsDescription.Text = definition.Description;

        if (isExploration)
        {
            SystemSurveyCardTitle.Text = "Exploration survey overlays";
            SystemSurveyCardDescription.Text =
                "Configure body details, the FSS body feed, and compact system-completion overlays.";
        }
        else if (isExobiology)
        {
            SystemSurveyCardTitle.Text = "Exobiology overlays";
            SystemSurveyCardDescription.Text =
                "Configure biological surveys, prior scans, surface radar, and reward presentation.";
        }
    }

    private void BeginVrAdjustment_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            _ = viewModel.BeginVrAdjustment();
        }
    }

    private async void ExportHumanSiteTemplates_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null
            || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Export the settlement template catalog",
                SuggestedFileName = "humanSiteTemplates.json",
                FileTypeChoices =
                [
                    new FilePickerFileType("JSON catalog")
                    {
                        Patterns = ["*.json"],
                        MimeTypes = ["application/json"],
                    },
                ],
            });
        if (file is not null)
        {
            await viewModel.HumanSite.TemplateAuthor.ExportAsync(
                file.Path.LocalPath);
        }
    }
}
