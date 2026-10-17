using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

/// <summary>Compact Raven-style manifest editor, with requests scoped to the popup lifetime.</summary>
public sealed partial class ColonizationCarrierManifestWindow : Window
{
    private readonly ColonizationCarrierManifestViewModel model;
    private readonly DispatcherTimer refreshAvailabilityTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public ColonizationCarrierManifestWindow()
        : this(
            new ColonizationCarrierManifestViewModel(
                new SrvSurvey.Core.Colonization.RavenColonialClient(),
                string.Empty,
                string.Empty,
                () => false,
                _ => { }
            )
        ) { }

    public ColonizationCarrierManifestWindow(ColonizationCarrierManifestViewModel model)
    {
        this.model = model;
        InitializeComponent();
        DataContext = model;
        model.CloseRequested += Close;
        Closed += OnClosed;
        Opened += OnOpened;
        refreshAvailabilityTimer.Tick += RefreshAvailability_Tick;
    }

    private async void Carrier_Changed(object? sender, SelectionChangedEventArgs args)
    {
        if (DataContext is ColonizationCarrierManifestViewModel current)
        {
            await current.LoadSelectedAsync();
        }
    }

    private void AddCommodity_Changed(object? sender, SelectionChangedEventArgs args)
    {
        if (sender is ComboBox { SelectedItem: CarrierManifestCommodityChoice { Commodity: { } commodity } } picker)
        {
            model.NewCommodity = commodity;
            model.AddCommodity();
            picker.SelectedItem = null;
        }
    }

    private async void Refresh_Click(object? sender, RoutedEventArgs args) => await model.RefreshAsync();

    private async void Save_Click(object? sender, RoutedEventArgs args) => await model.SaveAsync();

    private void Cancel_Click(object? sender, RoutedEventArgs args) => Close();

    private void OnOpened(object? sender, EventArgs args) => refreshAvailabilityTimer.Start();

    private void RefreshAvailability_Tick(object? sender, EventArgs args) => model.UpdateRefreshAvailability();

    private void OnClosed(object? sender, EventArgs args)
    {
        refreshAvailabilityTimer.Stop();
        refreshAvailabilityTimer.Tick -= RefreshAvailability_Tick;
        Opened -= OnOpened;
        model.CloseRequested -= Close;
        Closed -= OnClosed;
        model.Dispose();
    }
}
