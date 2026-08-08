using System.Text.Json;
using SrvSurvey.Core.Updates;

namespace SrvSurvey.Core.Tests.Updates;

public sealed class ReleaseInstallationPlanStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(
        2026,
        7,
        25,
        12,
        0,
        0,
        TimeSpan.Zero);
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-install-plan-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task CreateAndLoadRoundTripsBoundedHandoffPlan()
    {
        var store = new ReleaseInstallationPlanStore(
            new FixedTimeProvider(Now));
        var preparation = CreatePreparation();

        var created = await store.CreateAsync(
            temporaryDirectory,
            preparation,
            1_234,
            Now.AddMinutes(-1));
        var loaded = await store.LoadAsync(
            temporaryDirectory,
            created.PlanPath);

        Assert.Equal(created.PlanPath, loaded.PlanPath);
        Assert.Equal(created.HelperReadyMarkerPath, loaded.HelperReadyMarkerPath);
        Assert.Equal(created.HealthMarkerPath, loaded.HealthMarkerPath);
        Assert.Equal(created.OutcomePath, loaded.OutcomePath);
        Assert.Equal(created.CreatedAtUtc, loaded.CreatedAtUtc);
        Assert.Equal(created.ParentProcessId, loaded.ParentProcessId);
        Assert.Equal(
            created.ParentProcessStartTimeUtcTicks,
            loaded.ParentProcessStartTimeUtcTicks);
        Assert.Equal(created.HealthToken, loaded.HealthToken);
        IReadOnlyList<string> noArguments = Array.Empty<string>();
        Assert.Equal(
            created.Preparation with { StartupArguments = noArguments },
            loaded.Preparation with { StartupArguments = noArguments });
        Assert.Equal(
            created.Preparation.StartupArguments,
            loaded.Preparation.StartupArguments);
        Assert.Equal(64, created.HealthToken.Length);
        Assert.All(created.HealthToken, character => Assert.True(Uri.IsHexDigit(character)));
        Assert.True(File.Exists(created.PlanPath));
        Assert.Empty(Directory.GetFiles(
            Path.GetDirectoryName(created.PlanPath)!,
            "*.tmp"));
    }

    [Fact]
    public async Task LoadRejectsValidPlanCopiedOutsideRequestDirectory()
    {
        var store = new ReleaseInstallationPlanStore(
            new FixedTimeProvider(Now));
        var created = await store.CreateAsync(
            temporaryDirectory,
            CreatePreparation(),
            1_234,
            Now.AddMinutes(-1));
        var copiedPath = Path.Combine(temporaryDirectory, "copied-plan.json");
        File.Copy(created.PlanPath, copiedPath);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.LoadAsync(temporaryDirectory, copiedPath));
    }

    [Fact]
    public async Task LoadRejectsExpiredPlanWithoutChangingIt()
    {
        var created = await new ReleaseInstallationPlanStore(
                new FixedTimeProvider(Now))
            .CreateAsync(
                temporaryDirectory,
                CreatePreparation(),
                1_234,
                Now.AddMinutes(-1));
        var original = await File.ReadAllBytesAsync(created.PlanPath);
        var expiredStore = new ReleaseInstallationPlanStore(
            new FixedTimeProvider(Now.AddHours(3)));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            expiredStore.LoadAsync(temporaryDirectory, created.PlanPath));

        Assert.Equal(original, await File.ReadAllBytesAsync(created.PlanPath));
    }

    [Fact]
    public async Task HealthMarkerRequiresMatchingRandomToken()
    {
        var store = new ReleaseInstallationPlanStore(
            new FixedTimeProvider(Now));
        var plan = await store.CreateAsync(
            temporaryDirectory,
            CreatePreparation(),
            1_234,
            Now.AddMinutes(-1));
        Assert.False(await store.IsHealthConfirmedAsync(plan));
        await File.WriteAllTextAsync(
            plan.HealthMarkerPath,
            JsonSerializer.Serialize(new
            {
                schemaVersion = 2,
                requestId = plan.Preparation.RequestId,
                version = plan.Preparation.Version.ToString(),
                healthToken = new string('0', 64),
            }));
        Assert.False(await store.IsHealthConfirmedAsync(plan));
        File.Delete(plan.HealthMarkerPath);

        await store.WriteHealthMarkerAsync(plan);

        Assert.True(await store.IsHealthConfirmedAsync(plan));
    }

    [Fact]
    public async Task HelperReadyMarkerRequiresMatchingRandomToken()
    {
        var store = new ReleaseInstallationPlanStore(
            new FixedTimeProvider(Now));
        var plan = await store.CreateAsync(
            temporaryDirectory,
            CreatePreparation() with { RequiresElevation = true },
            1_234,
            Now.AddMinutes(-1));

        Assert.False(await store.IsHelperReadyAsync(plan));
        await store.WriteHelperReadyMarkerAsync(plan);
        Assert.True(await store.IsHelperReadyAsync(plan));
    }

    [Fact]
    public async Task OutcomeMustMatchPlanAndIsAtomicallyReplaced()
    {
        var store = new ReleaseInstallationPlanStore(
            new FixedTimeProvider(Now));
        var plan = await store.CreateAsync(
            temporaryDirectory,
            CreatePreparation(),
            1_234,
            Now.AddMinutes(-1));
        var wrong = new ReleaseInstallationOutcome(
            ReleaseInstallationOutcomeStatus.Aborted,
            Guid.NewGuid(),
            plan.Preparation.Version,
            Now,
            null,
            null,
            "wrong request");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.WriteOutcomeAsync(plan, wrong));
        var installed = wrong with
        {
            Status = ReleaseInstallationOutcomeStatus.Installed,
            RequestId = plan.Preparation.RequestId,
            BackupDirectory = plan.Preparation.BackupDirectory,
            Error = null,
        };
        await store.WriteOutcomeAsync(plan, installed);
        var rolledBack = installed with
        {
            Status = ReleaseInstallationOutcomeStatus.RolledBack,
            BackupDirectory = null,
            FailedDirectory = plan.Preparation.FailedDirectory,
            Error = "health timeout",
        };
        await store.WriteOutcomeAsync(plan, rolledBack);
        var loaded = await store.ReadOutcomeAsync(plan);

        Assert.Equal(rolledBack, loaded);
        using var document = JsonDocument.Parse(
            await File.ReadAllBytesAsync(plan.OutcomePath));
        Assert.Equal(
            "RolledBack",
            document.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            "health timeout",
            document.RootElement.GetProperty("Error").GetString());
        Assert.Empty(Directory.GetFiles(
            Path.GetDirectoryName(plan.OutcomePath)!,
            "*.tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private ReleaseInstallationPreparation CreatePreparation()
    {
        var requestId = Guid.NewGuid();
        var parent = Path.Combine(temporaryDirectory, "install-parent");
        var installation = Path.Combine(parent, "SrvSurvey");
        return new ReleaseInstallationPreparation(
            requestId,
            new Version(2, 0, 95, 23),
            "win-x64",
            installation,
            Path.Combine(temporaryDirectory, "ready"),
            Path.Combine(parent, $".SrvSurvey-update-{requestId:N}"),
            Path.Combine(parent, $".SrvSurvey-backup-{requestId:N}"),
            Path.Combine(parent, $".SrvSurvey-failed-{requestId:N}"),
            "SrvSurvey.Desktop.exe",
            new string('a', 64),
            new string('b', 64),
            false,
            ["--frontier-id", "F123"]);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
