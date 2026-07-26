using System.IO.Compression;
using System.Net;
using System.Text;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Updates;

namespace SrvSurvey.Core.Tests.Updates;

public sealed class PublishedReferenceUpdateServiceTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey.Tests",
        Guid.NewGuid().ToString("N"));
    private readonly PublishedReferenceUris uris = new(
        new Uri("https://example.test/codex.json"),
        new Uri("https://example.test/regional-codex.csv"),
        new Uri("https://example.test/known-systems.txt"),
        new Uri("https://example.test/bio.zip"),
        new Uri("https://example.test/guardian-templates.json"),
        new Uri("https://example.test/ruins.json"),
        new Uri("https://example.test/structures.json"),
        new Uri("https://example.test/guardian.zip"),
        new Uri("https://example.test/settlements.zip"),
        new Uri("https://example.test/ggg.json"),
        new Uri("https://example.test/nicknames.json"));

    [Fact]
    public async Task RefreshAsyncActivatesAllValidatedCatalogsAndPreservesBackup()
    {
        WriteExistingReferences();
        var service = CreateService(CreatePayloads());

        var result = await service.RefreshAsync(root);

        Assert.Equal(9, result.UpdatedCatalogs.Count);
        Assert.True(result.RestartRequired);
        Assert.NotNull(result.BackupDirectory);
        Assert.Equal(
            "keep me",
            File.ReadAllText(Path.Combine(root, "pub", "keep.txt")));
        Assert.Equal(
            "keep me",
            File.ReadAllText(Path.Combine(
                result.BackupDirectory!,
                "pub",
                "keep.txt")));
        Assert.Equal(
            LegacyRegionalCatalog,
            File.ReadAllText(Path.Combine(
                result.BackupDirectory!,
                RegionalCodexCandidateCatalog.LegacyFileName)));
        Assert.Equal(
            LegacyKnownSystemsCatalog,
            File.ReadAllText(Path.Combine(
                result.BackupDirectory!,
                "pub",
                KnownSystemAddressCatalog.LegacyFileName)));
        var active = LegacyReferenceCatalogLoader.Load(root);
        Assert.Equal(7, active.LocalCatalogCount);
        Assert.Empty(active.Warnings);
        var versions = new PublishedReferenceVersionStore().Load(root);
        Assert.Equal(10, versions.CodexReference);
        Assert.Equal(7, versions.BiologyCriteria);
        Assert.Equal(4, versions.BiologyEngine);
        Assert.Equal(48, versions.SettlementTemplate);
        Assert.Equal(68, versions.Guardian);
        Assert.Equal(15, versions.Settlements);
        Assert.Equal(1, versions.GreenGasGiants);
        Assert.Equal(1, versions.Nicknames);
        Assert.Equal(
            "The Lantern",
            SrvSurvey.Core.Navigation.SystemNicknameCatalog.Load(root)
                .Resolve("Tir"));
        var regional = RegionalCodexCandidateCatalog.Load(root);
        Assert.Equal(2, regional.Count);
        Assert.True(regional.IsCandidate(1, 2310101));
        var resolved = active.Exobiology.FindByDisplayName(
            "Aleoida Coronamus - Lime");
        Assert.NotNull(resolved);
        Assert.True(regional.IsCandidate(18, resolved.EntryId));
        var knownSystems = KnownSystemAddressCatalog.Load(root);
        Assert.True(knownSystems.TryResolve("Sol", out var sol));
        Assert.Equal(10477373803, sol);
        Assert.Empty(FindOperationDirectories(".reference-update-"));
        Assert.Empty(FindOperationDirectories(".reference-rollback-"));
    }

    [Fact]
    public async Task RefreshAsyncRejectsMalformedArchiveBeforeTouchingLiveFiles()
    {
        WriteExistingReferences();
        var originalCodex = File.ReadAllBytes(Path.Combine(root, "codexRef.json"));
        var originalSentinel = File.ReadAllBytes(Path.Combine(root, "pub", "keep.txt"));
        var payloads = CreatePayloads();
        payloads[uris.BiologyCriteriaArchive] = new byte[] { 1, 2, 3, 4 };
        var service = CreateService(payloads);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.RefreshAsync(root));

        Assert.Equal(
            originalCodex,
            File.ReadAllBytes(Path.Combine(root, "codexRef.json")));
        Assert.Equal(
            originalSentinel,
            File.ReadAllBytes(Path.Combine(root, "pub", "keep.txt")));
        Assert.False(File.Exists(Path.Combine(
            root,
            "pub",
            PublishedReferenceVersionStore.ManifestFileName)));
        Assert.Empty(FindOperationDirectories(".reference-update-"));
        Assert.Empty(FindOperationDirectories(".reference-rollback-"));
    }

    [Fact]
    public async Task RefreshAsyncRejectsMalformedRegionalCsvBeforeTouchingLiveFiles()
    {
        WriteExistingReferences();
        var regionalPath = Path.Combine(
            root,
            RegionalCodexCandidateCatalog.LegacyFileName);
        var originalRegional = File.ReadAllBytes(regionalPath);
        var payloads = CreatePayloads();
        payloads[uris.RegionalCodexCandidatesCsv] = Encoding.UTF8.GetBytes(
            "\"RegionID\",\"RegionName\",\"EnglishName\",\"Found\",\"NotExpectedToBeFound\",\"EntryID\",\"Name\",\"Varient\"\r\n"
                + "\"99\",\"Unknown\",\"bad\",\"0\",\"0\",\"1\",\"name\",\"A\"");
        var service = CreateService(payloads);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.RefreshAsync(root));

        Assert.Equal(originalRegional, File.ReadAllBytes(regionalPath));
        Assert.Equal(
            "keep me",
            File.ReadAllText(Path.Combine(root, "pub", "keep.txt")));
        Assert.Empty(FindOperationDirectories(".reference-update-"));
        Assert.Empty(FindOperationDirectories(".reference-rollback-"));
    }

    [Fact]
    public async Task RefreshAsyncRejectsMalformedKnownSystemsBeforeTouchingLiveFiles()
    {
        WriteExistingReferences();
        var knownSystemsPath = Path.Combine(
            root,
            "pub",
            KnownSystemAddressCatalog.LegacyFileName);
        var originalKnownSystems = File.ReadAllBytes(knownSystemsPath);
        var payloads = CreatePayloads();
        payloads[uris.KnownSystemAddresses] = Encoding.UTF8.GetBytes(
            "known_systems = {\n  \"sol\": 10477373803,\n}\nknown_missing = [");
        var service = CreateService(payloads);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.RefreshAsync(root));

        Assert.Equal(
            originalKnownSystems,
            File.ReadAllBytes(knownSystemsPath));
        Assert.Equal(
            "keep me",
            File.ReadAllText(Path.Combine(root, "pub", "keep.txt")));
        Assert.Empty(FindOperationDirectories(".reference-update-"));
        Assert.Empty(FindOperationDirectories(".reference-rollback-"));
    }

    [Fact]
    public async Task RefreshAsyncRollsBackAfterPostActivationFailure()
    {
        WriteExistingReferences();
        var originalCodex = File.ReadAllBytes(Path.Combine(root, "codexRef.json"));
        var originalRegional = File.ReadAllBytes(Path.Combine(
            root,
            RegionalCodexCandidateCatalog.LegacyFileName));
        var originalSentinel = File.ReadAllBytes(Path.Combine(root, "pub", "keep.txt"));
        var service = CreateService(
            CreatePayloads(),
            checkpoint =>
            {
                if (checkpoint
                    == PublishedReferenceUpdateCheckpoint.CandidateActivated)
                {
                    throw new InjectedFailureException();
                }
            });

        await Assert.ThrowsAsync<InjectedFailureException>(
            () => service.RefreshAsync(root));

        Assert.Equal(
            originalCodex,
            File.ReadAllBytes(Path.Combine(root, "codexRef.json")));
        Assert.Equal(
            originalRegional,
            File.ReadAllBytes(Path.Combine(
                root,
                RegionalCodexCandidateCatalog.LegacyFileName)));
        Assert.Equal(
            originalSentinel,
            File.ReadAllBytes(Path.Combine(root, "pub", "keep.txt")));
        Assert.False(File.Exists(Path.Combine(
            root,
            "pub",
            PublishedReferenceVersionStore.ManifestFileName)));
        Assert.Empty(FindOperationDirectories(".reference-update-"));
        Assert.Empty(FindOperationDirectories(".reference-rollback-"));
        Assert.Single(Directory.GetDirectories(
            Path.Combine(root, "reference-backups")));
    }

    [Fact]
    public async Task RefreshAsyncRejectsZipSlipPaths()
    {
        WriteExistingReferences();
        var payloads = CreatePayloads();
        payloads[uris.BiologyCriteriaArchive] = CreateArchive(
            ("../escape.json", Encoding.UTF8.GetBytes("{}")));
        var service = CreateService(payloads);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => service.RefreshAsync(root));

        Assert.Contains("unsafe path", exception.Message);
        Assert.False(File.Exists(Path.Combine(root, "escape.json")));
    }

    [Fact]
    public async Task RefreshAsyncRejectsEmptyNicknameResponse()
    {
        WriteExistingReferences();
        var payloads = CreatePayloads();
        payloads[uris.RavenNicknames] = Encoding.UTF8.GetBytes("[]");
        var service = CreateService(payloads);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => service.RefreshAsync(root));

        Assert.Contains("no nicknames", exception.Message);
        Assert.False(File.Exists(Path.Combine(root, "pub", "nicknames.json")));
        Assert.Equal(
            "keep me",
            File.ReadAllText(Path.Combine(root, "pub", "keep.txt")));
    }

    [Fact]
    public async Task RefreshAsyncSkipsFreshRegionalCatalogWhenVersionsAreCurrent()
    {
        WriteExistingReferences();
        await CreateService(CreatePayloads()).RefreshAsync(root);

        var result = await CreateService(new Dictionary<Uri, byte[]>())
            .RefreshAsync(root);

        Assert.Empty(result.UpdatedCatalogs);
        Assert.False(result.RestartRequired);
        Assert.Null(result.BackupDirectory);
    }

    [Fact]
    public async Task RefreshAsyncRefreshesOnlyAStaleRegionalCatalog()
    {
        WriteExistingReferences();
        await CreateService(CreatePayloads()).RefreshAsync(root);
        var regionalPath = Path.Combine(
            root,
            RegionalCodexCandidateCatalog.LegacyFileName);
        File.SetLastWriteTimeUtc(
            regionalPath,
            new DateTime(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc));
        var payloads = new Dictionary<Uri, byte[]>
        {
            [uris.RegionalCodexCandidatesCsv] = CreateRegionalCodexCsv(),
        };

        var result = await CreateService(payloads).RefreshAsync(root);

        Assert.Equal(
            ["regional Codex candidates"],
            result.UpdatedCatalogs);
        Assert.True(result.RestartRequired);
        Assert.Equal(2, RegionalCodexCandidateCatalog.Load(root).Count);
    }

    [Fact]
    public async Task RefreshAsyncRestoresOnlyAMissingKnownSystemCatalog()
    {
        WriteExistingReferences();
        await CreateService(CreatePayloads()).RefreshAsync(root);
        File.Delete(Path.Combine(
            root,
            "pub",
            KnownSystemAddressCatalog.LegacyFileName));
        var payloads = new Dictionary<Uri, byte[]>
        {
            [uris.KnownSystemAddresses] = CreateKnownSystemsCatalog(),
        };

        var result = await CreateService(payloads).RefreshAsync(root);

        Assert.Equal(["known system addresses"], result.UpdatedCatalogs);
        Assert.True(result.RestartRequired);
        Assert.True(KnownSystemAddressCatalog.Load(root).TryResolve(
            "Sol",
            out _));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private PublishedReferenceUpdateService CreateService(
        IReadOnlyDictionary<Uri, byte[]> payloads,
        Action<PublishedReferenceUpdateCheckpoint>? checkpoint = null)
    {
        return new PublishedReferenceUpdateService(
            new StubIndexClient(CreateIndex()),
            new PublishedReferenceVersionStore(),
            new HttpClient(new PayloadHandler(payloads)),
            uris,
            new FixedTimeProvider(),
            checkpoint);
    }

    private void WriteExistingReferences()
    {
        Directory.CreateDirectory(Path.Combine(root, "pub"));
        File.WriteAllText(Path.Combine(root, "pub", "keep.txt"), "keep me");
        File.WriteAllText(
            Path.Combine(
                root,
                "pub",
                KnownSystemAddressCatalog.LegacyFileName),
            LegacyKnownSystemsCatalog);
        CopyResource(
            "SrvSurvey.Core.Resources.codexRef.json",
            Path.Combine(root, "codexRef.json"));
        File.WriteAllText(
            Path.Combine(
                root,
                RegionalCodexCandidateCatalog.LegacyFileName),
            LegacyRegionalCatalog);
    }

    private Dictionary<Uri, byte[]> CreatePayloads()
    {
        return new Dictionary<Uri, byte[]>
        {
            [uris.CodexReference] = ReadResource(
                "SrvSurvey.Core.Resources.codexRef.json"),
            [uris.RegionalCodexCandidatesCsv] = CreateRegionalCodexCsv(),
            [uris.KnownSystemAddresses] = CreateKnownSystemsCatalog(),
            [uris.BiologyCriteriaArchive] = CreateBiologyArchive(),
            [uris.GuardianTemplates] = ReadResource(
                "SrvSurvey.Core.Resources.guardianSiteTemplates.json"),
            [uris.GuardianRuins] = ReadResource(
                "SrvSurvey.Core.Resources.allRuins.json"),
            [uris.GuardianStructures] = ReadResource(
                "SrvSurvey.Core.Resources.allStructures.json"),
            [uris.GuardianSurveyArchive] = ReadResource(
                "SrvSurvey.Core.Resources.guardian.zip"),
            [uris.HumanSettlementsArchive] = CreateArchive((
                "humanSiteTemplates.json",
                ReadResource("SrvSurvey.Core.Resources.humanSiteTemplates.json"))),
            [uris.GreenGasGiants] = ReadResource(
                "SrvSurvey.Core.Resources.ggg.json"),
            [uris.RavenNicknames] = Encoding.UTF8.GetBytes(
                "[{\"name\":\"Tir\",\"nickname\":\"The Lantern\"}]"),
        };
    }

    private static PublishedDataIndex CreateIndex()
    {
        return new PublishedDataIndex(
            new Version(2, 0, 95, 23),
            new Version(2, 0, 95, 0),
            7,
            4,
            10,
            48,
            68,
            15,
            1,
            1);
    }

    private static byte[] CreateBiologyArchive()
    {
        var assembly = typeof(ExobiologyReferenceCatalog).Assembly;
        const string prefix = "SrvSurvey.Core.Resources.bio-criteria.";
        var entries = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
            .Where(name => name.EndsWith(".json", StringComparison.Ordinal))
            .Select(name => (name[prefix.Length..], ReadResource(name)))
            .ToArray();
        return CreateArchive(entries);
    }

    private static byte[] CreateRegionalCodexCsv()
    {
        var csv = string.Join(
            "\r\n",
            "\"RegionID\",\"RegionName\",\"EnglishName\",\"Found\",\"NotExpectedToBeFound\",\"EntryID\",\"Name\",\"Varient\"",
            "\"1\",\"Galactic Centre\",\"Aleoida Arcus - Yellow\",\"0\",\"0\",\"2310101\",\"$Codex_Ent_Aleoids_01_B_Name;\",\"B\"",
            "\"18\",\"Inner Orion Spur\",\"Aleoida Coronamus - Lime\",\"0\",\"0\",\"\",\"$Codex_Ent_Aleoids_02_C_Name;\",\"C\"",
            "\"18\",\"Inner Orion Spur\",\"Already found\",\"1\",\"0\",\"2310102\",\"ignored\",\"ignored\"");
        return Encoding.UTF8.GetBytes(csv);
    }

    private static byte[] CreateKnownSystemsCatalog()
    {
        return Encoding.UTF8.GetBytes(
            "known_systems = {\n  \"sol\": 10477373803,\n}\n"
                + "known_missing = [\n]\n");
    }

    private static byte[] CreateArchive(params (string Name, byte[] Bytes)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(
                   stream,
                   ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            foreach (var (name, bytes) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
                using var target = entry.Open();
                target.Write(bytes);
            }
        }

        return stream.ToArray();
    }

    private static byte[] ReadResource(string resourceName)
    {
        var assembly = typeof(ExobiologyReferenceCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Test resource {resourceName} was not found.");
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    private static void CopyResource(string resourceName, string destination)
    {
        File.WriteAllBytes(destination, ReadResource(resourceName));
    }

    private string[] FindOperationDirectories(string prefix)
    {
        return Directory.GetDirectories(root)
            .Where(path => Path.GetFileName(path).StartsWith(
                prefix,
                StringComparison.Ordinal))
            .ToArray();
    }

    private sealed class StubIndexClient(PublishedDataIndex index)
        : IPublishedDataIndexClient
    {
        public Task<PublishedDataIndex> GetAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(index);
        }
    }

    private sealed class PayloadHandler(
        IReadOnlyDictionary<Uri, byte[]> payloads) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri is null
                || !payloads.TryGetValue(request.RequestUri, out var bytes))
            {
                return Task.FromResult(new HttpResponseMessage(
                    HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes),
            });
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
        }
    }

    private sealed class InjectedFailureException : Exception;

    private const string LegacyRegionalCatalog =
        "{\"Inner Orion Spur\":[\"2310101_old\"]}";

    private const string LegacyKnownSystemsCatalog =
        "known_systems = {\n  \"old\": 123,\n";
}
