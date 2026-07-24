using System.Text.Json.Nodes;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class SystemNotesViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-system-notes-vm-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task LoadsAndSavesCurrentSystemWithoutLosingSystemData()
    {
        var systemsDirectory = Path.Combine(
            temporaryDirectory,
            "systems",
            "F123");
        Directory.CreateDirectory(systemsDirectory);
        var path = Path.Combine(systemsDirectory, "Test System_42.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "name": "Test System",
              "address": 42,
              "notes": "Before",
              "futureField": { "enabled": true }
            }
            """);
        var viewModel = CreateViewModel();
        viewModel.UpdateContext(
            "F123",
            "Drew",
            "Test System",
            42,
            new GalacticCoordinate(1, 2, 3));

        var loaded = await viewModel.LoadCurrentAsync();
        viewModel.Notes = "After";
        var saved = await viewModel.SaveAsync();

        Assert.True(loaded);
        Assert.True(saved);
        Assert.Equal("Test System", viewModel.SystemName);
        Assert.Equal("42", viewModel.SystemAddress);
        Assert.False(viewModel.IsDirty);
        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.Equal("After", root["notes"]!.GetValue<string>());
        Assert.True(root["futureField"]!["enabled"]!.GetValue<bool>());
    }

    [Fact]
    public async Task AlwaysOnTopUsesLosslessLegacySetting()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var settingsPath = Path.Combine(temporaryDirectory, "settings.json");
        await File.WriteAllTextAsync(
            settingsPath,
            "{\"systemNotesTopMost\":false,\"futureSetting\":42}");
        var viewModel = CreateViewModel();

        await viewModel.SetAlwaysOnTopAsync(true);

        Assert.True(viewModel.AlwaysOnTop);
        var root = JsonNode.Parse(
            await File.ReadAllTextAsync(settingsPath))!.AsObject();
        Assert.True(root["systemNotesTopMost"]!.GetValue<bool>());
        Assert.Equal(42, root["futureSetting"]!.GetValue<int>());
    }

    [Fact]
    public async Task ProvidesEveryLegacyLinkAndSystemImagesAction()
    {
        var screenshotRoot = Path.Combine(temporaryDirectory, "screenshots");
        var imagesDirectory = Path.Combine(screenshotRoot, "Test- System");
        Directory.CreateDirectory(imagesDirectory);
        var settings = new JsonObject
        {
            ["screenshotTargetFolder"] = screenshotRoot,
        };
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "settings.json"),
            settings.ToJsonString());
        var viewModel = CreateViewModel();
        var openedUris = new List<Uri>();
        DirectoryInfo? openedDirectory = null;
        viewModel.SetPlatformServices(
            uri =>
            {
                openedUris.Add(uri);
                return Task.FromResult(true);
            },
            directory =>
            {
                openedDirectory = directory;
                return Task.FromResult(true);
            });
        viewModel.UpdateContext(
            "F123",
            "Drew",
            "Test: System",
            42,
            null);
        Assert.True(await viewModel.LoadCurrentAsync());

        await viewModel.OpenCanonnAsync();
        await viewModel.OpenSpanshAsync();
        await viewModel.OpenEdsmAsync();
        await viewModel.OpenImagesAsync();

        Assert.True(viewModel.HasImagesDirectory);
        Assert.Equal(
            "https://canonn-science.github.io/canonn-signals/?system=Test%3A%20System",
            openedUris[0].AbsoluteUri);
        Assert.Equal(
            "https://spansh.co.uk/system/42",
            openedUris[1].AbsoluteUri);
        Assert.Equal(
            "https://www.edsm.net/en/system?systemID64=42",
            openedUris[2].AbsoluteUri);
        Assert.Equal(imagesDirectory, openedDirectory?.FullName);
    }

    [Fact]
    public async Task WindowCommandRequiresCurrentSystemAndConnectedWindow()
    {
        var viewModel = CreateViewModel();
        var opened = false;
        viewModel.SetWindowOpener(() =>
        {
            opened = true;
            return Task.FromResult(true);
        });

        Assert.False(viewModel.OpenWindowCommand.CanExecute(null));
        Assert.False(await viewModel.LoadCurrentAsync());

        viewModel.UpdateContext("F123", "Drew", "Test System", 42, null);

        Assert.True(viewModel.OpenWindowCommand.CanExecute(null));
        viewModel.OpenWindowCommand.Execute(null);
        await WaitUntilAsync(() => opened);
        Assert.True(opened);
    }

    private SystemNotesViewModel CreateViewModel()
    {
        return new SystemNotesViewModel(
            new SystemNoteStore(temporaryDirectory),
            new SystemNotesSettingsStore(temporaryDirectory));
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        for (var attempt = 0; attempt < 50 && !predicate(); attempt++)
        {
            await Task.Delay(10);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
