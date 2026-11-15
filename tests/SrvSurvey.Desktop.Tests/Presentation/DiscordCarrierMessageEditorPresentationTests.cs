using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SrvSurvey.Core.Discord;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class DiscordCarrierMessageEditorPresentationTests
{
    [AvaloniaTheory]
    [InlineData("BoldButton", "**hello**")]
    [InlineData("ItalicButton", "*hello*")]
    [InlineData("UnderlineButton", "__hello__")]
    [InlineData("StrikeButton", "~~hello~~")]
    [InlineData("CodeButton", "`hello`")]
    [InlineData("QuoteButton", "> hello")]
    [InlineData("ListButton", "- hello")]
    public void ToolbarFormatsSelectedTextAndUpdatesPreview(string button, string expected)
    {
        var model = new DiscordCarrierMessageEditorViewModel(new(), previewTime: DateTimeOffset.UnixEpoch)
        {
            Message = "hello",
            IncludeDetails = false,
        };
        using var editor = new EditorFixture(model);
        TextBox box = editor.Window.FindControl<TextBox>("MessageEditor")!;
        box.SelectionStart = 0;
        box.SelectionEnd = 5;
        Click(editor.Window, button);
        Assert.Equal(expected, box.Text);
        Assert.Equal(expected, model.Message);
        Assert.Equal(expected, editor.Window.FindControl<DiscordMessagePreview>("PreviewMessage")!.Markdown);
    }

    [AvaloniaTheory]
    [InlineData("QuoteButton", "intro\n> first\n> second")]
    [InlineData("ListButton", "intro\n- first\n- second")]
    public void QuoteAndListButtonsFormatEverySelectedLine(string button, string expected)
    {
        var model = new DiscordCarrierMessageEditorViewModel(new(), previewTime: DateTimeOffset.UnixEpoch)
        {
            Message = "intro\nfirst\nsecond",
        };
        using var editor = new EditorFixture(model);
        TextBox box = editor.Window.FindControl<TextBox>("MessageEditor")!;
        box.SelectionStart = 18;
        box.SelectionEnd = 6;
        Assert.Equal("first\nsecond", box.SelectedText);
        Click(editor.Window, button);
        Assert.Equal(expected, model.Message);
    }

    [AvaloniaFact]
    public void FieldsInsertAtCaretAndApplyPreservesOtherEventsWhileCancelDiscardsChanges()
    {
        var model = new DiscordCarrierMessageEditorViewModel(new(), previewTime: DateTimeOffset.UnixEpoch)
        {
            Message = "Depart ",
        };
        using (var editor = new EditorFixture(model))
        {
            TextBox box = editor.Window.FindControl<TextBox>("MessageEditor")!;
            box.CaretIndex = 7;
            box.SelectionStart = 7;
            box.SelectionEnd = 7;
            editor.Window.FindControl<ComboBox>("FieldChoice")!.SelectedItem = "{countdown}";
            Assert.Equal("Depart {countdown}", model.Message);
            Assert.Equal(
                "Depart <t:900:R>",
                editor.Window.FindControl<DiscordMessagePreview>("PreviewMessage")!.Markdown
            );
            box.CaretIndex = 0;
            box.SelectionStart = 0;
            box.SelectionEnd = 0;
            Click(editor.Window, "BoldButton");
            Assert.Equal("**text**Depart {countdown}", model.Message);
            editor.Window.FindControl<ComboBox>("EventChoice")!.SelectedIndex = 1;
            editor.Window.FindControl<TextBox>("TitleEditor")!.Text = "Holding position";
            editor.Window.FindControl<CheckBox>("IncludeDetailsChoice")!.IsChecked = false;
            Assert.Empty(model.Preview.Fields);
            Click(editor.Window, "ApplyEditorButton");
            Assert.Equal("Holding position", editor.Applied!.Cancelled!.Title);
            Assert.Equal("**text**Depart {countdown}", editor.Applied.Scheduled!.Message);
        }
        using var cancelled = new EditorFixture(new(new(), previewTime: DateTimeOffset.UnixEpoch));
        cancelled.Window.FindControl<TextBox>("MessageEditor")!.Text = "discard me";
        Click(cancelled.Window, "CancelEditorButton");
        Assert.Null(cancelled.Applied);
    }

    [AvaloniaFact]
    public void ValidationDisablesApplyAndRestoringDefaultRepairsTheCurrentEvent()
    {
        using var editor = new EditorFixture(new(new(), previewTime: DateTimeOffset.UnixEpoch));
        editor.Window.FindControl<TextBox>("MessageEditor")!.Text = "{misspelled}";
        Assert.False(editor.Window.FindControl<Button>("ApplyEditorButton")!.IsEnabled);
        Click(editor.Window, "RestoreDefaultButton");
        Assert.True(editor.Window.FindControl<Button>("ApplyEditorButton")!.IsEnabled);
        Assert.Equal("{carrier_kind}: {carrier}", editor.Window.FindControl<TextBox>("MessageEditor")!.Text);
    }

    [AvaloniaTheory]
    [InlineData("blue-dark", 1120)]
    [InlineData("blue-light", 860)]
    public async Task CarrierImageAppearsBesideFormattedMessageAndCanBeRemoved(string palette, int width)
    {
        string root = Path.Combine(Path.GetTempPath(), "carrier-editor-render-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "carrier.png");
        var theme = new RavenThemeService(
            Application.Current!,
            new ThemePreferenceStore(Path.Combine(root, "theme.json"))
        );
        string previous = theme.Current.Key;
        try
        {
            theme.Select(palette);
            theme.ApplyCurrent();
            using (var bitmap = new WriteableBitmap(new PixelSize(240, 120), new Vector(96, 96)))
            {
                bitmap.Save(path, PngBitmapEncoderOptions.Default);
            }
            var model = new DiscordCarrierMessageEditorViewModel(
                new(PersonalImagePath: path),
                previewTime: new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero)
            )
            {
                Message =
                    "**{carrier}**\nPreparing for departure to *{destination}*.\n> All aboard!\nDeparture {countdown}",
            };
            using var editor = new EditorFixture(model, width);
            await editor.Window.PendingImage;
            Assert.Equal(
                new Size(240, 120),
                Assert.IsType<IImage>(editor.Window.FindControl<Image>("CarrierImage")!.Source, exactMatch: false).Size
            );
            Assert.False(editor.Window.FindControl<TextBlock>("ImagePlaceholder")!.IsVisible);
            Assert.Equal("", editor.Window.FindControl<TextBlock>("ImageError")!.Text);
            editor.Window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            using WriteableBitmap? frame = editor.Window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            string? output = Environment.GetEnvironmentVariable("SRVSURVEY_DISCORD_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(output))
            {
                Directory.CreateDirectory(output);
                frame.Save(Path.Combine(output, "editor-" + palette + ".png"), PngBitmapEncoderOptions.Default);
            }
            Click(editor.Window, "RemoveImageButton");
            Assert.Null(editor.Window.FindControl<Image>("CarrierImage")!.Source);
            Assert.Equal("", model.ImagePath);
            model.SelectedCarrier = 1;
            model.ImagePath = Path.Combine(root, "missing.png");
            await editor.Window.PendingImage;
            Assert.Equal(
                "Cannot open this image. Choose a PNG or JPEG up to 8 MB, 16 megapixels, and 16,384 pixels per side.",
                editor.Window.FindControl<TextBlock>("ImageError")!.Text
            );
            Assert.True(editor.Window.FindControl<TextBlock>("ImagePlaceholder")!.IsVisible);
        }
        finally
        {
            theme.Select(previous);
            theme.ApplyCurrent();
            Directory.Delete(root, true);
        }
    }

    [AvaloniaFact]
    public async Task SettingsButtonOpensTheEditorAndApplyReturnsItsDraftToSettings()
    {
        string root = Path.Combine(Path.GetTempPath(), "carrier-editor-open-" + Guid.NewGuid());
        var paths = new AppDataPaths(
            Path.Combine(root, "config"),
            Path.Combine(root, "data"),
            Path.Combine(root, "cache"),
            []
        );
        await using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithAppDataPaths(paths)
        );
        var view = new SettingsView { DataContext = main };
        var owner = new Window { Content = view };
        try
        {
            owner.Show();
            main.SettingsWorkspace.SelectCategory("application");
            view.FindControl<Button>("EditDiscordCarrierMessageButton")!
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            DiscordCarrierMessageEditorWindow editor = Assert.Single(
                owner.OwnedWindows.OfType<DiscordCarrierMessageEditorWindow>()
            );
            editor.FindControl<TextBox>("TitleEditor")!.Text = "All aboard";
            Click(editor, "ApplyEditorButton");
            Assert.Equal("All aboard", main.DiscordCarrierAnnouncements.MessageSettings.Scheduled!.Title);
            Assert.True(main.DiscordCarrierAnnouncements.HasChanges);
            Assert.Null(new DiscordCarrierSettingsStore(paths.UiSettingsPath).Load().MessageSettings);
        }
        finally
        {
            owner.Close();
            await main.DisposeAsync();
            Directory.Delete(root, true);
        }
    }

    [AvaloniaFact]
    public void PreviewDisplaysEmphasisCodeListsQuotesAndRelativeTimes()
    {
        var preview = new DiscordMessagePreview
        {
            ReferenceTime = DateTimeOffset.UnixEpoch,
            Markdown =
                "**bold** *italic* __underlined__ ~~struck~~ `code` ***both*** **first\nsecond**\n> quote\n- list\n<t:900:R> <t:-120:R> <t:999999999999999999999:R>",
        };
        Span[] spans = preview.Inlines!.OfType<Span>().ToArray();
        Assert.Equal(FontWeight.Bold, spans[0].FontWeight);
        Assert.Equal(FontStyle.Italic, spans[1].FontStyle);
        Assert.Equal(TextDecorations.Underline, spans[2].TextDecorations);
        Assert.Equal(TextDecorations.Strikethrough, spans[3].TextDecorations);
        Assert.Equal("monospace", spans[4].FontFamily.Name);
        Assert.Equal(FontWeight.Bold, spans[5].FontWeight);
        Assert.Equal(FontStyle.Italic, spans[5].FontStyle);
        Assert.Equal("first\nsecond", Assert.IsType<Run>(Assert.Single(spans[6].Inlines)).Text);
        string text = string.Concat(preview.Inlines!.OfType<Run>().Select(run => run.Text));
        Assert.Equal("      \n┃ quote\n• list\nin 15 minutes 2 minutes ago <t:999999999999999999999:R>", text);
    }

    [AvaloniaTheory]
    [InlineData("**hello *world***", true, false)]
    [InlineData("*hello **world***", false, false)]
    [InlineData("***hello* world**", true, true)]
    [InlineData("***hello** world*", false, true)]
    public void PreviewPreservesNestedBoldAndItalicWithoutShowingDelimiters(
        string markdown,
        bool boldOutside,
        bool innerFirst
    )
    {
        var preview = new DiscordMessagePreview { Markdown = markdown };
        Span outer = Assert.IsType<Span>(Assert.Single(preview.Inlines!));
        Assert.Equal(boldOutside ? FontWeight.Bold : FontWeight.Normal, outer.FontWeight);
        Assert.Equal(boldOutside ? FontStyle.Normal : FontStyle.Italic, outer.FontStyle);
        Assert.Equal(innerFirst ? " world" : "hello ", Assert.IsType<Run>(outer.Inlines[innerFirst ? 1 : 0]).Text);
        Span inner = Assert.IsType<Span>(outer.Inlines[innerFirst ? 0 : 1]);
        if (boldOutside)
        {
            Assert.Equal(FontStyle.Italic, inner.FontStyle);
        }
        else
        {
            Assert.Equal(FontWeight.Bold, inner.FontWeight);
        }
        Assert.Equal(innerFirst ? "hello" : "world", Assert.IsType<Run>(Assert.Single(inner.Inlines)).Text);
        Assert.Equal(2, outer.Inlines.Count);
    }

    [AvaloniaTheory]
    [InlineData("**Board now")]
    [InlineData("***Board now")]
    [InlineData("``Board now")]
    public void UnmatchedFormattingRemainsVisibleInThePreview(string markdown)
    {
        var preview = new DiscordMessagePreview { Markdown = markdown };
        Assert.Equal(markdown, string.Concat(preview.Inlines!.Select(inline => Assert.IsType<Run>(inline).Text)));
    }

    [AvaloniaFact]
    public void InsertingAFieldReplacesSelectedText()
    {
        var model = new DiscordCarrierMessageEditorViewModel(new(), previewTime: DateTimeOffset.UnixEpoch)
        {
            Message = "Depart Sol",
            IncludeDetails = false,
        };
        using var editor = new EditorFixture(model);
        TextBox box = editor.Window.FindControl<TextBox>("MessageEditor")!;
        box.SelectionStart = 10;
        box.SelectionEnd = 7;
        editor.Window.FindControl<ComboBox>("FieldChoice")!.SelectedItem = "{destination}";
        Assert.Equal("Depart {destination}", model.Message);
        Assert.Equal("Depart Lave", model.Preview.Description);
    }

    [AvaloniaTheory]
    [InlineData("QuoteButton", "> first\nsecond")]
    [InlineData("ListButton", "- first\nsecond")]
    public void FormattingAWholeLineDoesNotChangeTheNextUnselectedLine(string button, string expected)
    {
        var model = new DiscordCarrierMessageEditorViewModel(new(), previewTime: DateTimeOffset.UnixEpoch)
        {
            Message = "first\nsecond",
        };
        using var editor = new EditorFixture(model);
        TextBox box = editor.Window.FindControl<TextBox>("MessageEditor")!;
        box.SelectionStart = 0;
        box.SelectionEnd = 6;
        Click(editor.Window, button);
        Assert.Equal(expected, model.Message);
    }

    [AvaloniaTheory]
    [InlineData(@"\*literal\*", "*literal*", false)]
    [InlineData(@"*literal\* tail*", "literal* tail", true)]
    public void PreviewHonorsEscapedOpeningAndClosingDelimiters(string markdown, string expected, bool italic)
    {
        var preview = new DiscordMessagePreview { Markdown = markdown };
        Assert.Equal(expected, string.Concat(preview.Inlines!.Select(VisibleText)));
        if (italic)
        {
            Assert.Equal(FontStyle.Italic, Assert.IsType<Span>(Assert.Single(preview.Inlines!)).FontStyle);
        }
        else
        {
            Assert.Empty(preview.Inlines!.OfType<Span>());
        }
    }

    private static string VisibleText(Inline inline) =>
        inline switch
        {
            Run run => run.Text ?? "",
            Span span => string.Concat(span.Inlines.Select(VisibleText)),
            _ => "",
        };

    [AvaloniaFact]
    public async Task InvalidImagesShowAnErrorAndReselectionRetriesTheSamePath()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        await File.WriteAllBytesAsync(path, [0, 1, 2]);
        var model = new DiscordCarrierMessageEditorViewModel(new(), previewTime: DateTimeOffset.UnixEpoch);
        using var editor = new EditorFixture(model);
        try
        {
            model.SelectImage(path);
            await editor.Window.PendingImage;
            Assert.Equal(
                "Cannot open this image. Choose a PNG or JPEG up to 8 MB, 16 megapixels, and 16,384 pixels per side.",
                editor.Window.FindControl<TextBlock>("ImageError")!.Text
            );
            using (var bitmap = new WriteableBitmap(new PixelSize(120, 60), new Vector(96, 96)))
            {
                bitmap.Save(path, PngBitmapEncoderOptions.Default);
            }
            model.SelectImage(path);
            await editor.Window.PendingImage;
            Assert.Equal("", editor.Window.FindControl<TextBlock>("ImageError")!.Text);
            Assert.Equal(
                new PixelSize(120, 60),
                editor.Window.FindControl<CarrierImageCropSelector>("ImageCropSelector")!.Source!.PixelSize
            );
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public async Task ReselectingAnOverwrittenImageRefreshesThePreview()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        var model = new DiscordCarrierMessageEditorViewModel(new(), previewTime: DateTimeOffset.UnixEpoch);
        using var editor = new EditorFixture(model);
        try
        {
            using (var bitmap = new WriteableBitmap(new PixelSize(120, 60), new Vector(96, 96)))
            {
                bitmap.Save(path, PngBitmapEncoderOptions.Default);
            }
            model.SelectImage(path);
            await editor.Window.PendingImage;
            Assert.Equal(
                new PixelSize(120, 60),
                editor.Window.FindControl<CarrierImageCropSelector>("ImageCropSelector")!.Source!.PixelSize
            );
            using (var bitmap = new WriteableBitmap(new PixelSize(240, 120), new Vector(96, 96)))
            {
                bitmap.Save(path, PngBitmapEncoderOptions.Default);
            }
            model.SelectImage(path);
            await editor.Window.PendingImage;
            Assert.Equal(
                new PixelSize(240, 120),
                editor.Window.FindControl<CarrierImageCropSelector>("ImageCropSelector")!.Source!.PixelSize
            );
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void Click(Window window, string name) =>
        window.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private sealed class EditorFixture : IDisposable
    {
        public EditorFixture(DiscordCarrierMessageEditorViewModel model, int width = 1120)
        {
            Window = new(model, settings => Applied = settings) { Width = width };
            Window.Show();
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
        }

        public DiscordCarrierMessageEditorWindow Window { get; }
        public DiscordCarrierMessageSettings? Applied { get; private set; }

        public void Dispose() => Window.Close();
    }
}
