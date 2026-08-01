using System.Text.Json.Nodes;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Routes;

public sealed class FollowRouteStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-route-store-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task MissingRouteReturnsLegacyDefaultsWithoutCreatingFile()
    {
        var store = new FollowRouteStore(temporaryDirectory);

        var result = await store.LoadAsync("F123");

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.Exists);
        Assert.True(result.Route!.IsActive);
        Assert.True(result.Route.AutoCopy);
        Assert.Equal(-1, result.Route.LastReachedIndex);
        Assert.Empty(result.Route.Hops);
        Assert.Equal(
            Path.Combine(temporaryDirectory, "Routes", "F123.json"),
            result.Path);
        Assert.False(File.Exists(result.Path));
    }

    [Fact]
    public async Task LoadReadsLegacyRouteShapeAndComputedState()
    {
        var path = CreateRoutePath();
        await File.WriteAllTextAsync(
            path,
            """
            {
              "active": true,
              "autoCopy": true,
              "last": 0,
              "hops": [
                { "name": "Sol", "id64": 1, "x": 0, "y": 0, "z": 0 },
                {
                  "name": "Skaudai CH-B d14-34",
                  "id64": 2,
                  "x": 10.5,
                  "y": -2,
                  "z": 7,
                  "notes": "Map planet 2",
                  "refuel": true,
                  "neutron": true
                }
              ]
            }
            """);
        var store = new FollowRouteStore(temporaryDirectory);

        var result = await store.LoadAsync("F123");

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Exists);
        Assert.True(result.Route!.IsStarted);
        Assert.False(result.Route.IsComplete);
        Assert.True(result.Route.UseNextHop);
        var next = result.Route.NextHop;
        Assert.NotNull(next);
        Assert.Equal("Skaudai CH-B d14-34", next.Name);
        Assert.Equal(2, next.SystemAddress);
        Assert.Equal(new GalacticCoordinate(10.5, -2, 7), next.Position);
        Assert.Equal("Map planet 2", next.Notes);
        Assert.True(next.Refuel);
        Assert.True(next.Neutron);
    }

    [Fact]
    public async Task SaveUsesLegacyFieldsAndPreservesMatchingUnknownData()
    {
        var path = CreateRoutePath();
        await File.WriteAllTextAsync(
            path,
            """
            {
              "active": true,
              "autoCopy": false,
              "last": -1,
              "futureRoot": { "enabled": true },
              "hops": [
                {
                  "name": "Sol",
                  "id64": 1,
                  "x": 0,
                  "y": 0,
                  "z": 0,
                  "refuel": true,
                  "futureHop": 7
                }
              ]
            }
            """);
        var store = new FollowRouteStore(temporaryDirectory);
        var loaded = await store.LoadAsync("F123");
        var route = loaded.Route! with
        {
            IsActive = false,
            AutoCopy = true,
            LastReachedIndex = 0,
            Hops =
            [
                new FollowRouteHop(
                    "Sol",
                    1,
                    new GalacticCoordinate(1, 2, 3),
                    null,
                    false,
                    true),
            ],
        };

        await store.SaveAsync(route);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.True(root["futureRoot"]!["enabled"]!.GetValue<bool>());
        Assert.False(root["active"]!.GetValue<bool>());
        Assert.True(root["autoCopy"]!.GetValue<bool>());
        Assert.Equal(0, root["last"]!.GetValue<int>());
        var hop = root["hops"]![0]!.AsObject();
        Assert.Equal(7, hop["futureHop"]!.GetValue<int>());
        Assert.Equal(1D, hop["x"]!.GetValue<double>());
        Assert.True(hop["neutron"]!.GetValue<bool>());
        Assert.False(hop.ContainsKey("refuel"));
        Assert.False(hop.ContainsKey("notes"));
    }

    [Fact]
    public async Task ReplacingHopsDoesNotTransferUnknownDataToAnotherSystem()
    {
        var path = CreateRoutePath();
        await File.WriteAllTextAsync(
            path,
            """
            {
              "hops": [
                { "name": "Sol", "id64": 1, "futureHop": "Sol only" }
              ]
            }
            """);
        var store = new FollowRouteStore(temporaryDirectory);
        var loaded = await store.LoadAsync("F123");
        var route = loaded.Route! with
        {
            Hops =
            [
                new FollowRouteHop(
                    "Achenar",
                    2,
                    null,
                    null,
                    false,
                    false),
            ],
        };

        await store.SaveAsync(route);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        var hop = root["hops"]![0]!.AsObject();
        Assert.Equal("Achenar", hop["name"]!.GetValue<string>());
        Assert.False(hop.ContainsKey("futureHop"));
    }

    [Fact]
    public async Task SaveRefusesToOverwriteMalformedRoute()
    {
        var path = CreateRoutePath();
        const string malformed = "{\"hops\":";
        await File.WriteAllTextAsync(path, malformed);
        var store = new FollowRouteStore(temporaryDirectory);
        var route = new FollowRouteDocument(
            "F123",
            path,
            true,
            true,
            -1,
            []);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.SaveAsync(route));

        Assert.Equal(malformed, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task InvalidRouteAndUnsafeFrontierIdAreReported()
    {
        var path = CreateRoutePath();
        await File.WriteAllTextAsync(path, "{\"hops\":[{\"id64\":1}]}");
        var store = new FollowRouteStore(temporaryDirectory);

        var result = await store.LoadAsync("F123");

        Assert.False(result.IsSuccess);
        Assert.Contains("name", result.Error, StringComparison.OrdinalIgnoreCase);
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.LoadAsync("../outside"));
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.LoadAsync("unsafe:name"));
    }

    [Fact]
    public async Task NamedRoutesUseProfileRoutesFolderAndRememberSelection()
    {
        var store = new FollowRouteStore(temporaryDirectory);
        var draft = (await store.CreateNewAsync("F123")) with
        {
            Hops =
            [
                new FollowRouteHop("Sol", 1, null, null, false, false),
            ],
            Notes = "Survey staging route",
        };

        var saved = await store.SaveAsAsync(draft, "Colonia Run");
        var reloaded = await store.LoadAsync("F123");
        var catalog = await store.ListAsync("F123");

        Assert.Equal(
            Path.Combine(
                temporaryDirectory,
                "Routes",
                "F123",
                "Colonia Run.json"),
            saved.FilePath);
        Assert.True(File.Exists(saved.FilePath));
        Assert.Equal(saved.FilePath, reloaded.Path);
        Assert.Equal("Colonia Run", reloaded.Route!.Name);
        Assert.Equal("Survey staging route", reloaded.Route.Notes);
        Assert.Equal(saved.FilePath, Assert.Single(catalog).FilePath);
    }

    [Fact]
    public async Task ProgressOnlySaveDoesNotRewriteRouteDefinitionOrNotes()
    {
        var store = new FollowRouteStore(temporaryDirectory);
        var saved = await store.SaveAsAsync(
            (await store.CreateNewAsync("F123")) with
            {
                Hops =
                [
                    new FollowRouteHop("Sol", 1, null, null, false, false),
                    new FollowRouteHop("Achenar", 2, null, null, false, false),
                ],
                Notes = "Keep this note",
            },
            "Protected Definition");

        await store.SaveProgressAsync(saved with
        {
            LastReachedIndex = 0,
            AutoCopy = false,
            Notes = "Must not replace",
            Hops = [new FollowRouteHop("Wrong", 99, null, null, false, false)],
        });

        var reloaded = await store.ReloadAsync(saved);
        Assert.Equal(0, reloaded.Route!.LastReachedIndex);
        Assert.False(reloaded.Route.AutoCopy);
        Assert.Equal("Keep this note", reloaded.Route.Notes);
        Assert.Equal(["Sol", "Achenar"], reloaded.Route.Hops.Select(hop => hop.Name));
    }

    [Fact]
    public async Task NewWorkspaceLeavesSavedRoutesAndDeleteMovesRouteToRecovery()
    {
        var store = new FollowRouteStore(temporaryDirectory);
        var saved = await store.SaveAsAsync(
            (await store.CreateNewAsync("F123")) with
            {
                Hops = [new FollowRouteHop("Sol", 1, null, null, false, false)],
            },
            "Disposable");

        var blank = await store.CreateNewAsync("F123");
        Assert.Empty(blank.Hops);
        Assert.True(File.Exists(saved.FilePath));
        Assert.False((await store.LoadAsync("F123")).Exists);

        var recoveryPath = await store.DeleteAsync(saved);
        Assert.False(File.Exists(saved.FilePath));
        Assert.True(File.Exists(recoveryPath));
        Assert.Contains(
            Path.Combine("Routes", "F123", ".trash"),
            recoveryPath,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CatalogIncludesNotesCreationTimeAndPersistentFavorite()
    {
        var store = new FollowRouteStore(temporaryDirectory);
        var saved = await store.SaveAsAsync(
            (await store.CreateNewAsync("F123")) with
            {
                Hops = [new FollowRouteHop("Sol", 1, null, null, false, false)],
                Notes = "Meet near the primary star.",
            },
            "Favorite Run");

        var favorite = await store.SetFavoriteAsync(
            "F123",
            Path.GetFileName(saved.FilePath),
            isLegacy: false,
            isFavorite: true);
        var catalog = await store.ListAsync("F123");

        Assert.True(favorite.IsFavorite);
        var entry = Assert.Single(catalog);
        Assert.Equal("Meet near the primary star.", entry.Notes);
        Assert.NotEqual(default, entry.CreatedAt);
        Assert.True(entry.IsFavorite);
        var reloaded = await store.ReloadAsync(favorite);
        Assert.True(reloaded.Route!.IsFavorite);
    }

    [Fact]
    public async Task ImportExportAndNamedDeleteDoNotChangeAnotherLoadedRoute()
    {
        var store = new FollowRouteStore(temporaryDirectory);
        var active = await store.SaveAsAsync(
            (await store.CreateNewAsync("F123")) with
            {
                Hops = [new FollowRouteHop("Sol", 1, null, null, false, false)],
            },
            "Active Route");
        var importPath = Path.Combine(temporaryDirectory, "source.json");
        await File.WriteAllTextAsync(
            importPath,
            """
            {
              "name": "Imported Route",
              "notes": "Imported notes",
              "hops": [
                { "name": "Achenar", "id64": 2 }
              ]
            }
            """);

        var firstImport = await store.ImportAsync("F123", importPath);
        var secondImport = await store.ImportAsync("F123", importPath);
        var loaded = await store.LoadAsync("F123");

        Assert.Equal("Imported Route", firstImport.Name);
        Assert.Equal("Imported Route (2)", secondImport.Name);
        Assert.Equal(active.FilePath, loaded.Path);

        var importedEntries = (await store.ListAsync("F123"))
            .Where(route => route.Name.StartsWith(
                "Imported Route",
                StringComparison.Ordinal))
            .ToArray();
        var exportDirectory = Path.Combine(temporaryDirectory, "exports");
        var exported = await store.ExportAsync(
            "F123",
            importedEntries,
            exportDirectory);

        Assert.Equal(2, exported.Count);
        Assert.All(exported, path => Assert.True(File.Exists(path)));

        var recoveryPath = await store.DeleteNamedAsync(
            "F123",
            importedEntries[0].FileName,
            importedEntries[0].IsLegacy);
        loaded = await store.LoadAsync("F123");

        Assert.True(File.Exists(recoveryPath));
        Assert.False(File.Exists(importedEntries[0].FilePath));
        Assert.Equal(active.FilePath, loaded.Path);
    }

    [Fact]
    public async Task StaleLegacyCatalogNameCannotDeleteCommanderRoute()
    {
        var path = Path.Combine(
            temporaryDirectory,
            "Routes",
            "F123.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            """
            {
              "active": true,
              "autoCopy": true,
              "last": -1,
              "hops": [{ "name": "Sol", "id64": 1 }]
            }
            """);
        var store = new FollowRouteStore(temporaryDirectory);

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => store.DeleteNamedAsync(
                "F123",
                "stale-catalog-entry.json",
                isLegacy: true));

        Assert.True(File.Exists(path));
        Assert.False(Directory.Exists(Path.Combine(
            temporaryDirectory,
            "Routes",
            "F123",
            ".trash")));
    }

    [Fact]
    public async Task SpanshAndCsvExportsPreservePortableRouteData()
    {
        var store = new FollowRouteStore(temporaryDirectory);
        var saved = await store.SaveAsAsync(
            (await store.CreateNewAsync("F123")) with
            {
                SourceSpanshKind = SpanshRouteKind.Exobiology,
                Hops =
                [
                    new FollowRouteHop(
                        "Test System",
                        42,
                        new GalacticCoordinate(1.25, -2.5, 3.75),
                        "Scan, then \"map\"",
                        true,
                        false,
                        [
                            new FollowRouteBioTarget(
                                "A 2",
                                7,
                                ["Stratum Tectonicas", "Bacterium Acies"],
                                Subtype: "High metal content world",
                                DistanceToArrivalLs: 1245.75,
                                EstimatedScanValue: 125000,
                                EstimatedMappingValue: 625000,
                                EstimatedBiologyValue: 27428800,
                                IsTerraformable: true,
                                IsBiological: true),
                        ]),
                ],
            },
            "Portable Route");
        var entry = Assert.Single(await store.ListAsync("F123"));
        var exportDirectory = Path.Combine(temporaryDirectory, "portable");

        var spanshPath = Assert.Single(await store.ExportSpanshAsync(
            "F123",
            [entry],
            exportDirectory));
        var csvPath = Assert.Single(await store.ExportCsvAsync(
            "F123",
            [entry],
            exportDirectory));

        var spansh = JsonNode.Parse(
            await File.ReadAllTextAsync(spanshPath))!.AsObject();
        var hop = spansh["result"]!.AsArray().Single()!.AsObject();
        Assert.Equal("ok", spansh["status"]!.GetValue<string>());
        Assert.Equal("Test System", hop["name"]!.GetValue<string>());
        Assert.True(hop["must_refuel"]!.GetValue<bool>());
        var body = hop["bodies"]!.AsArray().Single()!.AsObject();
        Assert.Equal("Test System A 2", body["name"]!.GetValue<string>());
        Assert.Equal(
            ["Stratum Tectonicas", "Bacterium Acies"],
            body["landmarks"]!.AsArray()
                .Select(node => node!["subtype"]!.GetValue<string>()));

        var csv = await File.ReadAllTextAsync(csvPath);
        Assert.EndsWith(".csv", csvPath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sequence,System,SystemAddress", csv, StringComparison.Ordinal);
        Assert.Contains("\"Scan, then \"\"map\"\"\"", csv, StringComparison.Ordinal);
        Assert.Contains("Stratum Tectonicas; Bacterium Acies", csv, StringComparison.Ordinal);

        var reloaded = await store.ReloadAsync(saved);
        Assert.Equal(SpanshRouteKind.Exobiology, reloaded.Route!.SourceSpanshKind);
        Assert.Equal("Scan, then \"map\"", reloaded.Route.Hops[0].Notes);
    }

    [Fact]
    public async Task RenameChangesFileEmbeddedNameAndCurrentSelection()
    {
        var store = new FollowRouteStore(temporaryDirectory);
        var saved = await store.SaveAsAsync(
            (await store.CreateNewAsync("F123")) with
            {
                Notes = "Keep this note",
                IsFavorite = true,
                SourceSpanshKind = SpanshRouteKind.Neutron,
                Hops = [new FollowRouteHop("Jackson's Lighthouse", 42, null, null, false, true)],
            },
            "Old Name");
        var oldPath = saved.FilePath;

        var renamed = await store.RenameAsync(
            "F123",
            Path.GetFileName(oldPath),
            isLegacy: false,
            "New Name");

        Assert.Equal(oldPath, renamed.PreviousPath);
        Assert.False(File.Exists(oldPath));
        Assert.True(File.Exists(renamed.Route.FilePath));
        Assert.Equal("New Name.json", Path.GetFileName(renamed.Route.FilePath));
        Assert.Equal("New Name", renamed.Route.Name);
        Assert.Equal("Keep this note", renamed.Route.Notes);
        Assert.True(renamed.Route.IsFavorite);
        Assert.Equal(SpanshRouteKind.Neutron, renamed.Route.SourceSpanshKind);
        var selected = await store.LoadAsync("F123");
        Assert.Equal(renamed.Route.FilePath, selected.Path);
        Assert.Equal("New Name", selected.Route!.Name);
    }

    [Fact]
    public async Task FleetCarrierLibraryIsSeparatedAndRejectsStandardRoutes()
    {
        var standardStore = new FollowRouteStore(temporaryDirectory);
        var carrierStore = new FollowRouteStore(
            temporaryDirectory,
            FollowRouteKind.FleetCarrier);
        var standard = await standardStore.SaveAsAsync(
            (await standardStore.CreateNewAsync("F123")) with
            {
                Hops = [new FollowRouteHop("Sol", 1, null, null, false, false)],
            },
            "Explorer Route");
        var carrier = await carrierStore.SaveAsAsync(
            (await carrierStore.CreateNewAsync("F123")) with
            {
                Hops =
                [
                    new FollowRouteHop(
                        "Colonia",
                        2,
                        null,
                        "Refuel 500 t Tritium",
                        false,
                        false),
                ],
            },
            "Carrier Run");

        Assert.Equal(FollowRouteKind.Standard, standard.Kind);
        Assert.Equal(FollowRouteKind.FleetCarrier, carrier.Kind);
        Assert.DoesNotContain(
            "FleetCarrier",
            standard.FilePath,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            Path.Combine("Routes", "FleetCarrier", "F123"),
            carrier.FilePath,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Explorer Route", Assert.Single(
            await standardStore.ListAsync("F123")).Name);
        Assert.Equal("Carrier Run", Assert.Single(
            await carrierStore.ListAsync("F123")).Name);

        var carrierJson = JsonNode.Parse(
            await File.ReadAllTextAsync(carrier.FilePath))!.AsObject();
        Assert.Equal(
            "fleetCarrier",
            carrierJson["routeType"]!.GetValue<string>());

        var carrierEntry = Assert.Single(await carrierStore.ListAsync("F123"));
        var carrierExport = Assert.Single(await carrierStore.ExportSpanshAsync(
            "F123",
            [carrierEntry],
            Path.Combine(temporaryDirectory, "carrier-export")));
        var carrierSpansh = JsonNode.Parse(
            await File.ReadAllTextAsync(carrierExport))!.AsObject();
        var carrierJump = carrierSpansh["result"]!["jumps"]!
            .AsArray()
            .Single()!
            .AsObject();
        Assert.Equal("Colonia", carrierJump["name"]!.GetValue<string>());

        await Assert.ThrowsAsync<InvalidDataException>(
            () => standardStore.ImportAsync("F456", carrier.FilePath));
        await Assert.ThrowsAsync<InvalidDataException>(
            () => carrierStore.ImportAsync("F456", standard.FilePath));
    }

    [Fact]
    public async Task BioTargetsRoundTripAndProgressOnlySavePreservesDefinition()
    {
        var store = new FollowRouteStore(temporaryDirectory);
        var route = await store.SaveAsAsync(
            (await store.CreateNewAsync("F123")) with
            {
                Hops =
                [
                    new FollowRouteHop(
                        "Test System",
                        42,
                        null,
                        "Meet near the primary star.",
                        false,
                        false,
                        [
                            new FollowRouteBioTarget(
                                "A 2",
                                2,
                                ["Stratum Tectonicas", "Bacterium Acies"],
                                Subtype: "High metal content world",
                                DistanceToArrivalLs: 1245.75,
                                EstimatedScanValue: 125000,
                                EstimatedMappingValue: 625000,
                                EstimatedBiologyValue: 27428800,
                                IsTerraformable: true,
                                IsBiological: true),
                        ]),
                ],
            },
            "Exobiology Run");
        var root = JsonNode.Parse(
            await File.ReadAllTextAsync(route.FilePath))!.AsObject();
        root["hops"]![0]!["bio"]![0]!["source"] = "spansh";
        await File.WriteAllTextAsync(route.FilePath, root.ToJsonString());

        var completed = route with
        {
            Hops =
            [
                route.Hops[0] with
                {
                    Bio =
                    [
                        route.Hops[0].BioTargets[0] with
                        {
                            IsCompleted = true,
                        },
                    ],
                },
            ],
        };
        await store.SaveProgressAsync(completed);
        var reloaded = await store.ReloadAsync(completed);

        var target = Assert.Single(Assert.Single(reloaded.Route!.Hops).BioTargets);
        Assert.Equal("A 2", target.BodyName);
        Assert.Equal(2, target.BodyId);
        Assert.Equal(
            ["Stratum Tectonicas", "Bacterium Acies"],
            target.Species);
        Assert.Equal("High metal content world", target.Subtype);
        Assert.Equal(1245.75, target.DistanceToArrivalLs);
        Assert.Equal(125000, target.EstimatedScanValue);
        Assert.Equal(625000, target.EstimatedMappingValue);
        Assert.Equal(27428800, target.EstimatedBiologyValue);
        Assert.True(target.IsTerraformable);
        Assert.True(target.IsBiological);
        Assert.True(target.IsCompleted);
        root = JsonNode.Parse(
            await File.ReadAllTextAsync(route.FilePath))!.AsObject();
        Assert.Equal(
            "spansh",
            root["hops"]![0]!["bio"]![0]!["source"]!.GetValue<string>());
        Assert.Equal(
            "Meet near the primary star.",
            root["hops"]![0]!["notes"]!.GetValue<string>());
    }

    [Fact]
    public async Task BodyTargetTypeRoundTripsAndLegacyBioDefaultsToBiological()
    {
        var store = new FollowRouteStore(temporaryDirectory);
        var route = await store.SaveAsAsync(
            (await store.CreateNewAsync("F123")) with
            {
                Hops =
                [
                    new FollowRouteHop(
                        "Valuable System",
                        42,
                        null,
                        null,
                        false,
                        false,
                        [
                            new FollowRouteBioTarget(
                                "A 2",
                                2,
                                [],
                                Subtype: "Earth-like world",
                                IsBiological: false),
                        ]),
                ],
            },
            "Valuable Worlds");

        var root = JsonNode.Parse(
            await File.ReadAllTextAsync(route.FilePath))!.AsObject();
        Assert.False(root["hops"]![0]!["bio"]![0]!["biological"]!.GetValue<bool>());
        var reloaded = await store.ReloadAsync(route);
        Assert.False(reloaded.Route!.Hops[0].BioTargets[0].IsBiological);

        root["hops"]![0]!["bio"]![0]!.AsObject().Remove("biological");
        await File.WriteAllTextAsync(route.FilePath, root.ToJsonString());

        reloaded = await store.ReloadAsync(route);
        Assert.True(reloaded.Route!.Hops[0].BioTargets[0].IsBiological);
    }

    private string CreateRoutePath()
    {
        var directory = Path.Combine(temporaryDirectory, "routes");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "F123.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
