using Avalonia.Controls;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class ColonizationNexusEditorWindow : Window
{
    private readonly ColonizationNexusEditorViewModel model;

    public ColonizationNexusEditorWindow()
        : this(
            new ColonizationNexusEditorViewModel(
                new SrvSurvey.Core.Colonization.RavenColonialClient(),
                string.Empty,
                string.Empty,
                () => false,
                _ => Task.CompletedTask,
                []
            )
        ) { }

    public ColonizationNexusEditorWindow(ColonizationNexusEditorViewModel model)
    {
        this.model = model;
        InitializeComponent();
        DataContext = model;
        model.CloseRequested += Close;
        Closed += OnClosed;
    }

    private async void Submit_Click(object? sender, RoutedEventArgs args) => await model.SubmitAsync();

    private async void FindCarriers_Click(object? sender, RoutedEventArgs args) => await model.FindCarriersAsync();

    private void Delete_Click(object? sender, RoutedEventArgs args) => model.RequestDeletion();

    private void Keep_Click(object? sender, RoutedEventArgs args) => model.CancelDeletion();

    private async void ConfirmDelete_Click(object? sender, RoutedEventArgs args) => await model.ConfirmDeletionAsync();

    private void Cancel_Click(object? sender, RoutedEventArgs args) => Close();

    private void OnClosed(object? sender, EventArgs args)
    {
        Closed -= OnClosed;
        model.CloseRequested -= Close;
        model.Dispose();
    }
}
