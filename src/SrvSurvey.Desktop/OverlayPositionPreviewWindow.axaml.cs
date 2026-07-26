using Avalonia.Controls;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class OverlayPositionPreviewWindow : Window
{
    public OverlayPositionPreviewWindow()
    {
        InitializeComponent();
        Definition = OverlayLayoutCatalog.Supported[0];
        DataContext = OverlayPositionPreviewViewModel.Create(Definition);
    }

    public OverlayPositionPreviewWindow(OverlayLayoutDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        InitializeComponent();
        DataContext = OverlayPositionPreviewViewModel.Create(definition);
        Width = definition.PreviewSize.Width;
        Height = definition.PreviewSize.Height;
        MinWidth = Width;
        MinHeight = Height;
        MaxWidth = Width;
        MaxHeight = Height;
        Title = $"{definition.DisplayName} position preview";
    }

    public OverlayLayoutDefinition Definition { get; }
}
