using System.Globalization;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationSystemEditorViewModelTests
{
    [Fact]
    public async Task FinishingAnObsoleteCatalogRequestDoesNotRemoveTheNewRequestsCancellation()
    {
        var obsolete = new TaskCompletionSource<IReadOnlyList<ColonizationArchitectSystem>>();
        var current = new TaskCompletionSource<IReadOnlyList<ColonizationArchitectSystem>>();
        var client = new StubClient { CatalogRead = () => obsolete.Task };
        using ColonizationSystemEditorViewModel editor = Create(client);
        editor.UpdateContext(Context());
        Task first = editor.RefreshSystemsAsync();
        CancellationToken firstToken = client.CatalogToken;
        editor.UpdateContext(Context() with { CommanderName = "Other Cmdr" });
        client.CatalogRead = () => current.Task;
        Task second = editor.RefreshSystemsAsync();
        CancellationToken secondToken = client.CatalogToken;

        obsolete.SetResult([new("Obsolete", 47)]);
        await first;

        Assert.True(firstToken.IsCancellationRequested);
        Assert.True(editor.IsRefreshingSystems);
        Assert.False(secondToken.IsCancellationRequested);
        editor.Dispose();
        Assert.True(secondToken.IsCancellationRequested);
        current.SetCanceled(secondToken);
        await second;
        Assert.False(editor.IsRefreshingSystems);
        Assert.Empty(editor.SystemNames);
    }

    [Fact]
    public async Task CatalogSurvivesRestartRefreshesDailyAndAllowsManualRefresh()
    {
        using var folder = new CatalogTestFolder();
        var now = DateTimeOffset.Parse("2026-10-19T12:00:00Z", CultureInfo.InvariantCulture);
        var store = new ColonizationSettingsStore(folder.SettingsPath);
        var client = new StubClient { CatalogSystems = [new("Synuefai A", 47), new("Synuefai B", 48)] };
        using (var editor = new ColonizationSystemEditorViewModel(client, TinyCatalog(), store, () => now))
        {
            editor.UpdateContext(Context());
            editor.ActivateSystemCatalog();
            await editor.SystemCatalogRefreshTask;
            Assert.Equal(["Synuefai A", "Synuefai B"], editor.SystemNames);
            editor.ActivateSystemCatalog();
            Assert.Equal(1, client.CatalogReadCount);
        }
        using var restarted = new ColonizationSystemEditorViewModel(client, TinyCatalog(), store, () => now);
        restarted.UpdateContext(Context() with { CommanderName = "test cmdr" });
        Assert.Equal(["Synuefai A", "Synuefai B"], restarted.SystemNames);
        restarted.ActivateSystemCatalog();
        Assert.Equal(1, client.CatalogReadCount);
        now += TimeSpan.FromHours(23);
        restarted.ActivateSystemCatalog();
        Assert.Equal(1, client.CatalogReadCount);
        now += TimeSpan.FromHours(1);
        restarted.ActivateSystemCatalog();
        await restarted.SystemCatalogRefreshTask;
        Assert.Equal(2, client.CatalogReadCount);
        client.CatalogSystems = [new("Refreshed", 49)];
        await restarted.RefreshSystemsAsync();
        Assert.Equal(3, client.CatalogReadCount);
        Assert.Equal("Refreshed", Assert.Single(restarted.SystemNames));
        Assert.Equal(now, store.LoadSystemCatalog("Test Cmdr")!.RefreshedAtUtc);
    }

    [Fact]
    public async Task FailedRefreshPreservesCachedSystemsAndPersistsTheDailyAttempt()
    {
        using var folder = new CatalogTestFolder();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var store = new ColonizationSettingsStore(folder.SettingsPath);
        store.SaveSystemCatalog("Test Cmdr", new(now.AddDays(-2), now.AddDays(-2), [new("Cached", 47)]));
        var client = new StubClient
        {
            CatalogRead = () =>
                Task.FromException<IReadOnlyList<ColonizationArchitectSystem>>(new HttpRequestException("Offline")),
        };
        using (var editor = new ColonizationSystemEditorViewModel(client, TinyCatalog(), store, () => now))
        {
            editor.UpdateContext(Context());
            editor.ActivateSystemCatalog();
            await editor.SystemCatalogRefreshTask;
            Assert.Equal("Cached", Assert.Single(editor.SystemNames));
            Assert.Contains("Offline", editor.SystemsStatusMessage);
            Assert.False(editor.IsRefreshingSystems);
            editor.ActivateSystemCatalog();
            Assert.Equal(1, client.CatalogReadCount);
        }
        using var restarted = new ColonizationSystemEditorViewModel(client, TinyCatalog(), store, () => now);
        restarted.UpdateContext(Context());
        restarted.ActivateSystemCatalog();
        Assert.Equal(1, client.CatalogReadCount);
        await restarted.RefreshSystemsAsync();
        Assert.Equal(2, client.CatalogReadCount);
        Assert.Equal("Cached", Assert.Single(restarted.SystemNames));
        client.CatalogRead = null;
        await restarted.RefreshSystemsAsync();
        Assert.Empty(restarted.SystemNames);
        Assert.Empty(store.LoadSystemCatalog("Test Cmdr")!.Systems);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ObsoleteCatalogResponsesCannotReplaceTheActiveCommander(bool dispose)
    {
        using var folder = new CatalogTestFolder();
        var store = new ColonizationSettingsStore(folder.SettingsPath);
        store.SaveSystemCatalog("Other Cmdr", new(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [new("Other", 99)]));
        var response = new TaskCompletionSource<IReadOnlyList<ColonizationArchitectSystem>>();
        var client = new StubClient { CatalogRead = () => response.Task };
        using var editor = new ColonizationSystemEditorViewModel(client, TinyCatalog(), store);
        editor.UpdateContext(Context());
        Task pending = editor.RefreshSystemsAsync();
        Assert.True(editor.IsRefreshingSystems);
        editor.ActivateSystemCatalog();
        Assert.Equal(1, client.CatalogReadCount);
        CancellationToken token = client.CatalogToken;
        if (dispose)
        {
            string statusBeforeDisposal = editor.SystemsStatusMessage;
            editor.Dispose();
            Assert.Equal(statusBeforeDisposal, editor.SystemsStatusMessage);
            Assert.False(editor.CanRefreshSystems);
            Assert.False(editor.CanUseCurrentSystem);
        }
        else
        {
            editor.UpdateContext(Context() with { CommanderName = "Other Cmdr" });
            Assert.Equal("Other", Assert.Single(editor.SystemNames));
        }
        Assert.True(token.IsCancellationRequested);
        response.SetResult([new("Obsolete", 47)]);
        await pending;
        Assert.DoesNotContain("Obsolete", editor.SystemNames);
        Assert.Null(store.LoadSystemCatalog("Test Cmdr")!.RefreshedAtUtc);
        Assert.Equal("Other", Assert.Single(store.LoadSystemCatalog("Other Cmdr")!.Systems).Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConsentAndCancellationLeaveTheCatalogUsable(bool hasCachedSystems)
    {
        using var folder = new CatalogTestFolder();
        var store = new ColonizationSettingsStore(folder.SettingsPath);
        if (hasCachedSystems)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            store.SaveSystemCatalog("Test Cmdr", new(now, now, [new("Cached", 47)]));
        }
        var response = new TaskCompletionSource<IReadOnlyList<ColonizationArchitectSystem>>();
        var client = new StubClient { CatalogRead = () => response.Task };
        using var editor = new ColonizationSystemEditorViewModel(client, TinyCatalog(), store);
        editor.UpdateContext(Context() with { IsExternalDataEnabled = false });
        await editor.RefreshSystemsAsync();
        Assert.Equal(0, client.CatalogReadCount);
        editor.UpdateContext(Context());
        string cachedStatus = editor.SystemsStatusMessage;
        string[] cachedNames = editor.SystemNames.ToArray();
        Task pending = editor.RefreshSystemsAsync();
        Assert.StartsWith("Refreshing", editor.SystemsStatusMessage);
        CancellationToken token = client.CatalogToken;
        editor.UpdateContext(Context() with { IsExternalDataEnabled = false });
        Assert.Equal(cachedStatus, editor.SystemsStatusMessage);
        Assert.Equal(cachedNames, editor.SystemNames);
        response.SetCanceled(token);
        await pending;
        Assert.False(editor.IsRefreshingSystems);
        Assert.Equal(cachedStatus, editor.SystemsStatusMessage);
        Assert.Equal(cachedNames, editor.SystemNames);
        Assert.False(editor.CanRefreshSystems);
    }

    [Fact]
    public async Task CatalogSelectionLoadsTheSelectedSystemAndUseCurrentWorksOutsideTheCatalog()
    {
        var client = new StubClient { CatalogSystems = [new("Remote Colony", 47)] };
        using ColonizationSystemEditorViewModel editor = Create(client);
        editor.UpdateContext(Context());
        await editor.RefreshSystemsAsync();
        editor.SystemNameInput = "Remote Colony";
        client.Current = System() with { Name = "Remote Colony", SystemAddress = 47 };
        await editor.LoadAsync();
        Assert.Equal("47", Assert.Single(client.SystemIdentifiers));
        Assert.Equal(47, editor.LoadedSystemAddress);
        editor.Sites[0].Name = "Local edit";
        editor.UpdateContext(Context() with { SystemName = "Outside Catalog", SystemAddress = 99 });
        Assert.Equal("Remote Colony", editor.SystemNameInput);
        Assert.Equal(47, editor.LoadedSystemAddress);
        Assert.True(editor.HasLocalChanges);
        editor.UseCurrentSystemCommand.Execute(null);
        Assert.Equal("Outside Catalog", editor.SystemNameInput);
        Assert.False(editor.IsLoaded);
        client.Current = System() with { Name = "Outside Catalog", SystemAddress = 99 };
        await editor.LoadAsync();
        Assert.Equal("99", client.SystemIdentifiers[^1]);
        editor.SystemNameInput = "Another Unlisted System";
        await editor.LoadAsync();
        Assert.Equal("Another Unlisted System", client.SystemIdentifiers[^1]);
        editor.SystemNameInput = "";
        Assert.False(editor.CanLoad);
        editor.UseCurrentSystem();
        Assert.Equal("Outside Catalog", editor.SystemNameInput);
    }

    [Fact]
    public async Task CatalogRefreshDoesNotDiscardEditsToAnInitiallyUnlistedSystem()
    {
        var client = new StubClient { Current = System() with { Name = "Remote", SystemAddress = 47 } };
        using ColonizationSystemEditorViewModel editor = Create(client);
        editor.UpdateContext(Context());
        editor.SystemNameInput = "Remote";
        await editor.LoadAsync();
        editor.Sites[0].Name = "Keep this edit";
        client.CatalogSystems = [new("Remote", 47)];
        await editor.RefreshSystemsAsync();
        editor.UpdateContext(Context() with { SystemName = "Another live system", SystemAddress = 999 });
        Assert.Equal("Remote", editor.SystemNameInput);
        Assert.Equal(47, editor.LoadedSystemAddress);
        Assert.Equal("Keep this edit", Assert.Single(editor.Sites).Name);
        Assert.True(editor.HasLocalChanges);
    }

    [Fact]
    public async Task InvalidCacheAndUnavailableLocationsDoNotBlockManualInput()
    {
        using var folder = new CatalogTestFolder();
        var store = new ColonizationSettingsStore(folder.SettingsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(folder.SettingsPath)!);
        await File.WriteAllTextAsync(folder.SettingsPath + ".systems.json", "{\"TEST CMDR\":{\"Systems\":{}}}");
        Assert.Null(store.LoadSystemCatalog("Test Cmdr"));
        using var editor = new ColonizationSystemEditorViewModel(new StubClient(), TinyCatalog(), store);
        editor.UpdateContext(Context() with { SystemName = null, SystemAddress = null });
        Assert.False(editor.CanUseCurrentSystem);
        editor.UseCurrentSystem();
        Assert.Empty(editor.SystemNameInput);
        editor.SystemNameInput = "Typed System";
        Assert.True(editor.CanLoad);
        editor.UpdateContext(Context() with { CommanderName = "Other Cmdr", SystemName = null, SystemAddress = 123 });
        Assert.Equal("123", editor.SystemNameInput);
        Assert.Empty(editor.SystemNames);
    }

    [Fact]
    public async Task CacheNormalizesEntriesAndDoesNotReplaceItWhenSavingFails()
    {
        using var folder = new CatalogTestFolder();
        var store = new ColonizationSettingsStore(folder.SettingsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(folder.SettingsPath)!);
        await File.WriteAllTextAsync(
            folder.SettingsPath + ".systems.json",
            """
            {"TEST CMDR":{"LastAttemptUtc":"2026-10-18T00:00:00Z","Systems":[
                {"Name":"B","SystemAddress":2},null,{"Name":"a","SystemAddress":1},
                {"Name":"A","SystemAddress":1},{"Name":"bad","SystemAddress":-1},{"Name":" "}]}}
            """
        );
        Assert.Equal(["a", "B"], store.LoadSystemCatalog("test cmdr")!.Systems.Select(system => system.Name));
        using var editor = new ColonizationSystemEditorViewModel(new StubClient(), TinyCatalog(), store);
        editor.UpdateContext(Context());
        File.Delete(folder.SettingsPath + ".systems.json");
        Directory.CreateDirectory(folder.SettingsPath + ".systems.json");
        await editor.RefreshSystemsAsync();
        Assert.Equal(["a", "B"], editor.SystemNames);
        Assert.Contains("could not be refreshed", editor.SystemsStatusMessage);
    }

    private sealed class CatalogTestFolder : IDisposable
    {
        private readonly string directory = Path.Combine(
            Path.GetTempPath(),
            "SrvSurvey-system-catalog-" + Guid.NewGuid().ToString("N")
        );

        public string SettingsPath => Path.Combine(directory, "ui.json");

        public void Dispose()
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
