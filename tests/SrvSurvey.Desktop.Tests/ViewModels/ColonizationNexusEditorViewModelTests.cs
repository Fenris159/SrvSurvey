using System.Net;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationNexusEditorViewModelTests
{
    [Fact]
    public async Task ReusesOrderedEntriesAndPublishesTheCompleteDraftThenRefreshesMembership()
    {
        var api = new Writer();
        using var nexus = new ColonizationNexusViewModel(api, ColonizationBuildCatalog.LoadEmbedded(), _ => { });
        Assert.Throws<InvalidOperationException>(() => nexus.CreateEditor([]));
        nexus.SetContext(true, "Fenris", "personal-test-key");
        int opens = 0;
        nexus.SetCreateOpener(() => opens++);
        Assert.True(nexus.CreateNexusCommand.CanExecute(null));
        nexus.CreateNexusCommand.Execute(null);
        Assert.Equal(1, opens);
        using ColonizationNexusEditorViewModel model = nexus.CreateEditor([]);
        bool closed = false;
        model.CloseRequested += () => closed = true;
        model.Name = " New Nexus ";
        model.Notes = " Route notes ";
        model.IsPrivate = false;
        model.Systems.Draft = "Beta";
        Assert.False(model.CanSubmit);
        model.Systems.CommitCommand.Execute(null);
        model.Systems.Draft = "Alpha";
        model.Systems.CommitCommand.Execute(null);
        model.Systems.Rows[1].UpCommand.Execute(null);
        model.Commanders.Draft = "Other";
        model.Commanders.CommitCommand.Execute(null);
        Assert.True(model.CanSubmit);
        await model.SubmitAsync();
        Assert.True(closed);
        Assert.Equal(1, api.Creates);
        Assert.Equal(1, api.Configures);
        Assert.Equal("Fenris", api.Commander);
        Assert.Equal("personal-test-key", api.Key);
        Assert.Equal("New Nexus", api.Draft!.Name);
        Assert.Equal("Route notes", api.Draft.Notes);
        Assert.False(api.Draft.IsPrivate);
        Assert.Equal(["Alpha", "Beta"], api.Draft.Systems);
        Assert.Equal("Other", Assert.Single(api.Draft.Commanders));
        Assert.Equal(1, api.IndexReads);
        Assert.Equal("new", nexus.SelectedNexus!.Id);
        Assert.Equal("new", nexus.Detail!.Nexus.Id);
        Assert.False(model.CanSubmit);
        await model.SubmitAsync();
        Assert.Equal(1, api.Creates);
    }

    [Fact]
    public async Task SupportsCarrierSearchUniqueAddAndRemovalAndDraftCorrections()
    {
        var api = new Writer();
        using ColonizationNexusEditorViewModel model = Create(
            api,
            [
                new()
                {
                    MarketId = 42,
                    Name = "ABC-123",
                    DisplayName = "My Carrier",
                },
            ]
        );
        model.Name = "Nexus";
        Assert.Equal("Fenris", model.Commander);
        Assert.True(model.IsPrivate);
        Assert.Equal("My Carrier (ABC-123)", model.SelectedCarrier!.Label);
        Assert.True(model.AddCarrierCommand.CanExecute(null));
        model.AddCarrierCommand.Execute(null);
        Assert.False(model.AddCarrierCommand.CanExecute(null));
        model.AddCarrierCommand.Execute(null);
        Assert.Single(model.Carriers);
        model.RemoveCarrierCommand.Execute(model.Carriers[0]);
        Assert.Empty(model.Carriers);
        model.RemoveCarrierCommand.Execute("invalid");
        await model.FindCarriersAsync();
        Assert.Equal(0, api.Searches);
        model.CarrierQuery = " Another ";
        Assert.True(model.CanFindCarrier);
        await model.FindCarriersAsync();
        Assert.Equal("Another", api.Query);
        Assert.Equal(43, model.SelectedCarrier.MarketId);
        model.AddCarrierCommand.Execute(null);
        Assert.Contains("add", model.Status);
        model.Commanders.Draft = "Mistake";
        model.Commanders.CommitCommand.Execute(null);
        model.Commanders.Rows[0].RemoveCommand.Execute(null);
        Assert.Empty(model.Commanders.Rows);
        await model.SubmitAsync();
        Assert.Equal(43, Assert.Single(api.Draft!.CarrierIds));
    }

    [Fact]
    public async Task RejectsDuplicatesAndUncommittedNamesBeforeAnyMutation()
    {
        var api = new Writer();
        using ColonizationNexusEditorViewModel model = Create(api);
        await model.SubmitAsync();
        Assert.Equal(0, api.Creates);
        model.Name = "Nexus";
        model.Commanders.Draft = "Other";
        await model.SubmitAsync();
        Assert.Equal(0, api.Creates);
        model.Commanders.CommitCommand.Execute(null);
        model.Commanders.Draft = "other";
        model.Commanders.CommitCommand.Execute(null);
        await model.SubmitAsync();
        Assert.Contains("duplicate", model.Status);
        Assert.Equal(0, api.Creates);
        model.Commanders.Rows[1].RemoveCommand.Execute(null);
        model.Systems.Draft = "Sol";
        model.Systems.CommitCommand.Execute(null);
        model.Systems.Draft = "sol";
        model.Systems.CommitCommand.Execute(null);
        await model.SubmitAsync();
        Assert.Contains("duplicate", model.Status);
        Assert.Equal(0, api.Creates);
    }

    [Fact]
    public async Task ConfigurationFailureRetainsIdAndRetriesTheSameNexus()
    {
        var api = new Writer { ConfigureFailure = new IOException("offline") };
        using ColonizationNexusEditorViewModel model = Create(api);
        model.Name = "Nexus";
        await model.SubmitAsync();
        Assert.Equal(1, api.Creates);
        Assert.Contains("was created", model.Status);
        Assert.Equal("Finish Nexus setup", model.SubmitLabel);
        Assert.False(model.CanEditName);
        Assert.Equal("Close", model.CloseLabel);
        Assert.True(model.CanSubmit);
        api.ConfigureFailure = null;
        model.Systems.Draft = "Beta";
        model.Systems.CommitCommand.Execute(null);
        await model.SubmitAsync();
        Assert.Equal(1, api.Creates);
        Assert.Equal(2, api.Configures);
        Assert.Equal("new", api.ConfiguredId);
    }

    [Fact]
    public async Task UnknownCreateOutcomeCannotBeRetriedBlindly()
    {
        var api = new Writer { CreateFailure = new HttpRequestException("lost reply") };
        using ColonizationNexusEditorViewModel model = Create(api);
        model.Name = "Nexus";
        await model.SubmitAsync();
        Assert.Contains("may have created", model.Status);
        Assert.False(model.CanSubmit);
        Assert.False(model.CanEdit);
        await model.SubmitAsync();
        Assert.Equal(1, api.Creates);
        Assert.Equal(0, api.Configures);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task DefinitiveCreateRejectionAllowsCorrectionAndRetry(HttpStatusCode status)
    {
        var api = new Writer
        {
            CreateFailure = new RavenColonialServiceException(status, "create", "secret must not appear"),
        };
        using ColonizationNexusEditorViewModel model = Create(api);
        model.Name = "Nexus";
        await model.SubmitAsync();
        Assert.True(model.CanSubmit);
        Assert.Contains("rejected", model.Status);
        Assert.DoesNotContain("secret", model.Status);
        api.CreateFailure = null;
        await model.SubmitAsync();
        Assert.Equal(2, api.Creates);
    }

    [Fact]
    public async Task BlocksConcurrentSubmissionAndDiscardsResultsAfterContextOrWindowCloses()
    {
        var pending = new TaskCompletionSource<ColonizationNexus>();
        var api = new Writer { PendingCreate = pending.Task };
        bool current = true;
        bool published = false;
        using var model = new ColonizationNexusEditorViewModel(
            api,
            "Fenris",
            "key",
            () => current,
            _ =>
            {
                published = true;
                return Task.CompletedTask;
            },
            []
        );
        model.Name = "Nexus";
        model.Systems.Load("Sol");
        Task request = model.SubmitAsync();
        Assert.True(model.IsBusy);
        Assert.False(model.CanSubmit);
        await model.SubmitAsync();
        current = false;
        pending.SetResult(Writer.Snapshot());
        await request;
        Assert.False(published);
        Assert.Equal(0, api.Configures);
        Assert.Equal(1, api.Creates);
        model.Dispose();
        model.Dispose();
        Assert.False(model.CanEdit);
    }

    [Fact]
    public async Task HandlesEmptyFailedAndSupersededCarrierSearches()
    {
        var api = new Writer { Matches = [] };
        using ColonizationNexusEditorViewModel model = Create(api);
        model.CarrierQuery = "ABC";
        await model.FindCarriersAsync();
        Assert.Null(model.SelectedCarrier);
        Assert.Contains("No matching", model.Status);
        api.SearchFailure = new IOException("offline");
        await model.FindCarriersAsync();
        Assert.Contains("search failed", model.Status);
        Assert.False(model.IsBusy);
        var pending = new TaskCompletionSource<IReadOnlyList<ColonizationNexusCarrierMatch>>();
        api.SearchFailure = null;
        api.PendingSearch = pending.Task;
        Task searching = model.FindCarriersAsync();
        model.Dispose();
        pending.SetResult([new() { MarketId = 99, Name = "Stale" }]);
        await searching;
        Assert.Empty(model.CarrierOptions);
    }

    private static ColonizationNexusEditorViewModel Create(
        Writer api,
        IReadOnlyList<ColonizationFleetCarrier>? carriers = null
    )
    {
        var model = new ColonizationNexusEditorViewModel(
            api,
            "Fenris",
            "key",
            () => true,
            _ => Task.CompletedTask,
            carriers ?? []
        );
        model.Systems.Load("Sol");
        return model;
    }

    internal sealed class Writer : IRavenColonialNexusWriter, IRavenColonialNexusReader
    {
        public int SystemChanges { get; private set; }
        public IReadOnlyList<string>? SavedSystems { get; private set; }
        public Exception? SystemsFailure { get; set; }
        public Task<ColonizationNexus?>? PendingNexusRead { get; set; }
        public Task<ColonizationNexus>? PendingSystems { get; set; }

        public Task<ColonizationNexus> SetNexusSystemsAsync(
            string id,
            IReadOnlyList<string> systems,
            string commander,
            string apiKey,
            CancellationToken cancellationToken
        )
        {
            SystemChanges++;
            SavedSystems = systems;
            Commander = commander;
            Key = apiKey;
            if (SystemsFailure is { } failure)
            {
                throw failure;
            }
            Existing = (Existing ?? Snapshot()) with
            {
                Systems = (Existing ?? Snapshot()).Systems.Where(system => systems.Contains(system.Name)).ToArray(),
            };
            return PendingSystems ?? Task.FromResult(Existing);
        }

        public int PrivacyChanges { get; private set; }
        public Exception? PrivacyFailure { get; set; }
        public Task<ColonizationNexus>? PendingPrivacy { get; set; }

        public Task<ColonizationNexus> SetNexusPrivacyAsync(
            string id,
            bool isPrivate,
            string commander,
            string apiKey,
            CancellationToken cancellationToken
        )
        {
            PrivacyChanges++;
            Commander = commander;
            Key = apiKey;
            if (PrivacyFailure is { } failure)
            {
                throw failure;
            }
            Existing = (Existing ?? Snapshot()) with { Open = !isPrivate };
            return PendingPrivacy ?? Task.FromResult(Existing);
        }

        public int Renames { get; private set; }
        public int Deletes { get; private set; }
        public string? RenamedTo { get; private set; }
        public Exception? DeleteFailure { get; set; }
        public ColonizationNexus? Existing { get; set; }

        public Task<ColonizationNexus> RenameNexusAsync(
            string id,
            string name,
            string commander,
            string apiKey,
            CancellationToken cancellationToken
        )
        {
            Renames++;
            RenamedTo = name;
            return Task.FromResult((Existing ?? Snapshot()) with { Name = name });
        }

        public Task DeleteNexusAsync(string id, string commander, string apiKey, CancellationToken cancellationToken)
        {
            Deletes++;
            if (DeleteFailure is not null)
            {
                throw DeleteFailure;
            }
            return Task.CompletedTask;
        }

        public int Creates { get; private set; }
        public int Configures { get; private set; }
        public int IndexReads { get; private set; }
        public int Searches { get; private set; }
        public string? Commander { get; private set; }
        public string? Key { get; private set; }
        public string? ConfiguredId { get; private set; }
        public string? Query { get; private set; }
        public ColonizationNexusDraft? Draft { get; private set; }
        public Exception? CreateFailure { get; set; }
        public Exception? ConfigureFailure { get; set; }
        public Exception? SearchFailure { get; set; }
        public Task<ColonizationNexus>? PendingCreate { get; set; }
        public Task<IReadOnlyList<ColonizationNexusCarrierMatch>>? PendingSearch { get; set; }
        public IReadOnlyList<ColonizationNexusCarrierMatch> Matches { get; set; } =
        [
            new()
            {
                MarketId = 43,
                Name = "XYZ-456",
                CarrierName = "Another",
            },
        ];

        public Task<ColonizationNexus> CreateNexusAsync(
            string name,
            string commander,
            string apiKey,
            CancellationToken cancellationToken
        )
        {
            Creates++;
            Commander = commander;
            Key = apiKey;
            if (CreateFailure is { } exception)
            {
                throw exception;
            }
            return PendingCreate ?? Task.FromResult(Snapshot());
        }

        public Task<ColonizationNexus> ConfigureNexusAsync(
            string id,
            ColonizationNexusDraft draft,
            string commander,
            string apiKey,
            CancellationToken cancellationToken
        )
        {
            Configures++;
            ConfiguredId = id;
            Draft = draft;
            if (ConfigureFailure is { } exception)
            {
                throw exception;
            }
            Existing = Existing is null ? null : Existing with { Name = RenamedTo ?? Existing.Name };
            return Task.FromResult(Existing ?? Snapshot());
        }

        public Task<IReadOnlyList<ColonizationNexusCarrierMatch>> FindNexusCarriersAsync(
            string name,
            string commander,
            string apiKey,
            CancellationToken cancellationToken
        )
        {
            Searches++;
            Query = name;
            if (SearchFailure is { } exception)
            {
                throw exception;
            }
            return PendingSearch ?? Task.FromResult(Matches);
        }

        public Task<IReadOnlyList<ColonizationNexusSummary>> GetCommanderNexusesAsync(
            string commander,
            string apiKey,
            CancellationToken cancellationToken = default
        )
        {
            IndexReads++;
            return Task.FromResult<IReadOnlyList<ColonizationNexusSummary>>(
                Deletes > 0 ? [] : [new() { Id = Existing?.Id ?? "new", Name = Existing?.Name ?? "New Nexus" }]
            );
        }

        public Task<ColonizationNexus?> GetNexusAsync(
            string id,
            string commander,
            string apiKey,
            CancellationToken cancellationToken = default
        ) => PendingNexusRead ?? Task.FromResult<ColonizationNexus?>(Existing ?? Snapshot());

        internal static ColonizationNexus Snapshot() =>
            new()
            {
                Id = "new",
                Name = "New Nexus",
                Owner = "Fenris",
            };
    }
}
