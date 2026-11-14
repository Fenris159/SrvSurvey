using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using SrvSurvey.Core.Discord;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Presentation;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class DiscordCarrierMessageEditorWindow : Window
{
    private readonly DiscordCarrierMessageEditorViewModel model;
    private readonly Action<DiscordCarrierMessageSettings> apply;
    private Bitmap? image;
    private string? requestedImagePath;
    private int requestedImageSelection = -1;
    private int imageRevision;
    internal Task PendingImage { get; private set; } = Task.CompletedTask;

    public DiscordCarrierMessageEditorWindow()
        : this(new DiscordCarrierMessageEditorViewModel(new()), _ => { }) { }

    public DiscordCarrierMessageEditorWindow(
        DiscordCarrierMessageEditorViewModel model,
        Action<DiscordCarrierMessageSettings> apply
    )
    {
        this.model = model;
        this.apply = apply;
        InitializeComponent();
        PreviewMessage.ReferenceTime = model.PreviewTime;
        DataContext = model;
        FieldChoice.ItemsSource = DiscordCarrierMessageFormatter.Tokens.Select(token => "{" + token + "}").ToArray();
        model.PropertyChanged += ModelChanged;
        Opened += async (_, _) =>
        {
            PendingImage = RefreshImageAsync();
            await PendingImage;
        };
        Closed += (_, _) =>
        {
            model.PropertyChanged -= ModelChanged;
            imageRevision++;
            CarrierImage.Source = null;
            ImageCropSelector.Source = null;
            image?.Dispose();
        };
        UpdatePreview();
    }

    private async void ModelChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        UpdatePreview();
        UpdateImageCrop();
        PendingImage = RefreshImageAsync();
        await PendingImage;
    }

    private void UpdatePreview()
    {
        DiscordCarrierMessage preview = model.Preview;
        PreviewEmbed.BorderBrush = new SolidColorBrush(Color.FromUInt32(0xFF000000u | (uint)preview.Color));
        PreviewFields.Children.Clear();
        PreviewFields.RowDefinitions.Clear();
        int row = 0;
        int column = 0;
        foreach (DiscordCarrierMessageField field in preview.Fields)
        {
            if (!field.Inline && column > 0)
            {
                row++;
                column = 0;
            }
            if (PreviewFields.RowDefinitions.Count <= row)
            {
                PreviewFields.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            }
            var content = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 12) };
            content.Children.Add(
                new TextBlock
                {
                    Text = field.Name,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(Color.Parse("#F2F3F5")),
                    TextWrapping = TextWrapping.Wrap,
                }
            );
            content.Children.Add(
                new DiscordMessagePreview
                {
                    ReferenceTime = model.PreviewTime,
                    Markdown = field.Value,
                    Foreground = new SolidColorBrush(Color.Parse("#DBDEE1")),
                    TextWrapping = TextWrapping.Wrap,
                }
            );
            Grid.SetRow(content, row);
            Grid.SetColumn(content, column);
            Grid.SetColumnSpan(content, field.Inline ? 1 : 3);
            PreviewFields.Children.Add(content);
            if (!field.Inline || column == 2)
            {
                row++;
                column = 0;
            }
            else
            {
                column = 2;
            }
        }
    }

    private async Task RefreshImageAsync()
    {
        string path = model.ImagePath;
        if (path == requestedImagePath && model.ImageSelectionVersion == requestedImageSelection)
        {
            return;
        }
        requestedImagePath = path;
        requestedImageSelection = model.ImageSelectionVersion;
        int revision = ++imageRevision;
        CarrierImage.Source = null;
        ImageCropSelector.Source = null;
        ImageCropPanel.IsVisible = false;
        image?.Dispose();
        image = null;
        ImageError.Text = "";
        ImagePlaceholder.IsVisible = true;
        try
        {
            DiscordCarrierImage? selected = await DiscordCarrierImage.LoadAsync(path);
            if (revision != imageRevision || selected is null)
            {
                return;
            }
            using var stream = new MemoryStream(selected.Bytes);
            image = new Bitmap(stream);
            ImageCropSelector.Source = image;
            ImageCropPanel.IsVisible = true;
            UpdateImageCrop();
            ImagePlaceholder.IsVisible = false;
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or InvalidDataException
                        or UnauthorizedAccessException
                        or ArgumentException
                        or NotSupportedException
            )
        {
            if (revision == imageRevision)
            {
                ImageError.Text = "Cannot open this image. Choose a readable PNG or JPEG up to 8 MB.";
            }
        }
    }

    private void UpdateImageCrop()
    {
        if (image is null)
        {
            CarrierImage.Source = null;
            return;
        }
        CarrierImage.Source = model.ImageCrop is { } crop ? new CarrierCroppedImage(image, crop) : image;
    }

    private void ResetImageCrop_Click(object? sender, RoutedEventArgs eventArgs) => model.ResetImageCrop();

    private async void ChooseImage_Click(object? sender, RoutedEventArgs eventArgs)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Choose your carrier image",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Carrier images")
                    {
                        Patterns = ["*.png", "*.jpg", "*.jpeg"],
                        MimeTypes = ["image/png", "image/jpeg"],
                    },
                ],
            }
        );
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            model.SelectImage(path);
            await PendingImage;
            UpdateLayout();
            ImageCropPanel.BringIntoView();
        }
    }

    private void RemoveImage_Click(object? sender, RoutedEventArgs eventArgs) => model.ImagePath = "";

    private void RestoreDefault_Click(object? sender, RoutedEventArgs eventArgs) => model.RestoreDefault();

    private void Cancel_Click(object? sender, RoutedEventArgs eventArgs) => Close();

    private void Apply_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (model.CanApply)
        {
            apply(model.Settings);
            Close();
        }
    }

    private void Field_SelectionChanged(object? sender, SelectionChangedEventArgs eventArgs)
    {
        if (FieldChoice.SelectedItem is string token)
        {
            Insert(token, "", "");
            FieldChoice.SelectedIndex = -1;
        }
    }

    private void Format_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: string marker })
        {
            if (marker is "> " or "- ")
            {
                int start = Math.Min(MessageEditor.SelectionStart, MessageEditor.SelectionEnd);
                string text = MessageEditor.Text ?? "";
                int lineStart = start == 0 ? 0 : text.LastIndexOf('\n', start - 1) + 1;
                int end = Math.Max(MessageEditor.SelectionStart, MessageEditor.SelectionEnd);
                string selection = text[lineStart..end];
                string formatted = marker + selection.Replace("\n", "\n" + marker, StringComparison.Ordinal);
                MessageEditor.Text = text[..lineStart] + formatted + text[end..];
                MessageEditor.Focus();
                MessageEditor.SelectionStart = lineStart + marker.Length;
                MessageEditor.SelectionEnd = lineStart + formatted.Length;
            }
            else
            {
                Insert(marker, marker, "text");
            }
        }
    }

    private void Insert(string before, string after, string placeholder)
    {
        string text = MessageEditor.Text ?? "";
        int start = Math.Min(MessageEditor.SelectionStart, MessageEditor.SelectionEnd);
        int end = Math.Max(MessageEditor.SelectionStart, MessageEditor.SelectionEnd);
        string selection = end > start ? text[start..end] : placeholder;
        MessageEditor.Text = text[..start] + before + selection + after + text[end..];
        MessageEditor.Focus();
        MessageEditor.SelectionStart = start + before.Length;
        MessageEditor.SelectionEnd = start + before.Length + selection.Length;
    }
}
