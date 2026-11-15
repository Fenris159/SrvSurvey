using System.Text.Json;
using SrvSurvey.Core.Discord;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class DiscordCarrierImageCropSettingsTests
{
    [Fact]
    public void EachCarrierKeepsItsCropAndAReplacementImageStartsCentered()
    {
        var original = new DiscordCarrierMessageSettings();
        var editor = new DiscordCarrierMessageEditorViewModel(original, previewTime: DateTimeOffset.UnixEpoch);
        editor.SelectImage("personal.png");
        Assert.Equal(new DiscordCarrierImageCrop(), editor.ImageCrop);
        editor.ImageZoom = 2;
        editor.ImageCrop = editor.ImageCrop! with { CenterX = 0.7, CenterY = 0.6 };
        editor.SelectedCarrier = 1;
        editor.SelectImage("squadron.png");
        editor.ImageZoom = 3;
        editor.SelectedCarrier = 0;
        Assert.Equal("personal.png", editor.ImagePath);
        Assert.Equal(new DiscordCarrierImageCrop(2, 0.7, 0.6), editor.ImageCrop);
        DiscordCarrierMessageSettings saved = JsonSerializer.Deserialize<DiscordCarrierMessageSettings>(
            JsonSerializer.Serialize(editor.Settings)
        )!;
        Assert.Equal(new DiscordCarrierImageCrop(2, 0.7, 0.6), saved.PersonalImageCrop);
        Assert.Equal(new DiscordCarrierImageCrop(3), saved.SquadronImageCrop);
        Assert.Null(original.PersonalImageCrop);
        Assert.Equal("", original.PersonalImagePath);
        editor.SelectImage("replacement.jpg");
        Assert.Equal(new DiscordCarrierImageCrop(), editor.ImageCrop);
        editor.ImageZoom = 2;
        editor.ResetImageCrop();
        Assert.Equal(1, editor.ImageZoom);
        editor.SelectedCarrier = 1;
        Assert.Equal(new DiscordCarrierImageCrop(3), editor.ImageCrop);
        editor.ImagePath = "";
        Assert.Null(editor.ImageCrop);
    }
}
