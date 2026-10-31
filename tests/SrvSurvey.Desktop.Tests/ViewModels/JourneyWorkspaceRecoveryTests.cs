using SrvSurvey.Core.Journeys;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class JourneyWorkspaceViewModelTests
{
    [Fact]
    public async Task RefreshKeepsCatalogAccessErrorsVisibleAlongsideTheActiveJourney()
    {
        if (OperatingSystem.IsWindows() || string.Equals(Environment.UserName, "root", StringComparison.Ordinal))
        {
            return;
        }
        JourneyDocument saved = await SeedHistoricalJourneyAsync(1, complete: false);
        await new CommanderProfileStore(DataDirectory).SaveActiveJourneyAsync("F123", "Drew", true, saved.FileName);
        JourneyWorkspaceViewModel model = CreateViewModel();
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        string folder = Path.GetDirectoryName(saved.FilePath)!;
        UnixFileMode permissions = File.GetUnixFileMode(folder);
        try
        {
            File.SetUnixFileMode(folder, UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            await model.RefreshAsync();
            Assert.True(model.HasActiveJourney);
            Assert.Contains("Journey history could not be loaded", model.StatusMessage, StringComparison.Ordinal);
        }
        finally
        {
            File.SetUnixFileMode(folder, permissions);
        }
    }

    [Fact]
    public async Task AnOlderJourneyStaysVisibleAndCanBeCompletedWithoutItsOriginalJournals()
    {
        JourneyDocument original = await SeedHistoricalJourneyAsync(1, complete: false);
        await new CommanderProfileStore(DataDirectory).SaveActiveJourneyAsync("F123", "Drew", true, original.FileName);
        JourneyWorkspaceViewModel model = CreateViewModel();
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        Assert.True(model.HasActiveJourney);
        Assert.Equal(original.FileName, Assert.Single(model.Journeys).Document.FileName);
        Assert.True(model.RequestConcludeCommand.CanExecute(null));
        Assert.Contains("restored from its saved history", model.StatusMessage, StringComparison.Ordinal);
        await model.ConfirmConcludeAsync();
        Assert.False(model.HasActiveJourney);
        Assert.False(Assert.IsType<JourneyListItemViewModel>(model.SelectedJourney).Document.IsActive);
        Assert.Null(
            (await new CommanderProfileStore(DataDirectory).LoadAsync("F123", true)).Data!.ActiveJourneyFileName
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnUnavailableJourneyDoesNotBlockStartingFromAnEmptyLibrary(bool futureVersion)
    {
        await WriteJournalAsync(
            """
            {"timestamp":"2026-07-01T00:00:00Z","event":"Fileheader","Odyssey":true}
            {"timestamp":"2026-07-01T00:00:01Z","event":"Commander","Name":"Drew","FID":"F123"}
            {"timestamp":"2026-07-01T00:05:00Z","event":"Location","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            """
        );
        string folder = Path.Combine(DataDirectory, "journey", "F123");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "unavailable.json");
        string original = futureVersion ? """{"schemaVersion":999,"startTime":"2026-07-01T00:00:00Z"}""" : "{broken";
        await File.WriteAllTextAsync(path, original);
        var profiles = new CommanderProfileStore(DataDirectory);
        await profiles.SaveActiveJourneyAsync("F123", "Drew", true, "unavailable");
        JourneyWorkspaceViewModel model = CreateViewModel();
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        Assert.True(model.HasUnavailableJourney);
        Assert.False(model.HasActiveJourney);
        Assert.Empty(model.Journeys);
        Assert.True(model.NewJourneyCommand.CanExecute(null));
        Assert.True(model.ClearUnavailableJourneyCommand.CanExecute(null));
        await model.StartNewJourneyAsync();
        model.NewJourneyName = "Recovered";
        Assert.True(model.BeginJourneyCommand.CanExecute(null));
        await model.BeginJourneyAsync();
        Assert.False(model.HasUnavailableJourney);
        Assert.True(model.HasActiveJourney, model.StartStatus);
        Assert.Equal("Recovered", model.JourneyName);
        Assert.Equal(
            model.SelectedJourney!.Document.FileName,
            (await profiles.LoadAsync("F123", true)).Data!.ActiveJourneyFileName
        );
        Assert.Equal(original, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task RefreshRetriesInitializationAfterAnUnavailableJourneyIsRepaired()
    {
        JourneyDocument saved = await SeedHistoricalJourneyAsync(1, complete: false);
        string original = await File.ReadAllTextAsync(saved.FilePath);
        await File.WriteAllTextAsync(saved.FilePath, "{broken");
        await new CommanderProfileStore(DataDirectory).SaveActiveJourneyAsync("F123", "Drew", true, saved.FileName);
        JourneyWorkspaceViewModel model = CreateViewModel();
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        Assert.True(model.HasUnavailableJourney);
        await File.WriteAllTextAsync(saved.FilePath, original);
        await model.RefreshAsync();
        Assert.False(model.HasUnavailableJourney);
        Assert.True(model.HasActiveJourney);
        Assert.Equal(saved.Name, model.JourneyName);
        Assert.True(model.RequestConcludeCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AReadableSavedJourneyCanBeResumedFromTheLibraryAndCompleted(bool unavailableReference)
    {
        JourneyDocument original = await SeedHistoricalJourneyAsync(1, complete: false);
        if (unavailableReference)
        {
            await new CommanderProfileStore(DataDirectory).SaveActiveJourneyAsync("F123", "Drew", true, ".");
        }
        JourneyWorkspaceViewModel model = CreateViewModel();
        await model.UpdateContextAsync("F123", "Drew", true, "Sol", 42);
        Assert.False(model.HasActiveJourney);
        Assert.Equal(unavailableReference, model.HasUnavailableJourney);
        Assert.Equal("Active", Assert.Single(model.Journeys).State);
        Assert.False(model.NewJourneyCommand.CanExecute(null));
        await model.StartNewJourneyAsync();
        model.NewJourneyName = "Another";
        Assert.False(model.BeginJourneyCommand.CanExecute(null));
        Assert.True(model.CanResumeJourney);
        Assert.True(model.ResumeJourneyCommand.CanExecute(null));
        await model.ResumeJourneyAsync();
        Assert.True(model.HasActiveJourney);
        Assert.False(model.HasUnavailableJourney);
        Assert.False(model.CanResumeJourney);
        Assert.False(model.NewJourneyCommand.CanExecute(null));
        Assert.Equal(original.FileName, model.SelectedJourney!.Document.FileName);
        Assert.True(model.RequestConcludeCommand.CanExecute(null));
        await model.ConfirmConcludeAsync();
        Assert.False(model.HasActiveJourney);
        Assert.False(model.CanResumeJourney);
        Assert.True(model.NewJourneyCommand.CanExecute(null));
    }
}
