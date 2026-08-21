using SrvSurvey.Core.Updates;

namespace SrvSurvey.Core.Tests.Updates;

public sealed class ReleaseInstallationPlanCleanerTests : IDisposable
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-plan-cleaner-tests-{Guid.NewGuid():N}");

    [Fact]
    public void CleanDeletesOnlyOldDirectGuidPlanDirectories()
    {
        var oldPlan = CreatePlan(Guid.NewGuid(), Now.AddDays(-2));
        var recentPlan = CreatePlan(Guid.NewGuid(), Now.AddHours(-4));
        var planRoot = Path.GetDirectoryName(oldPlan)!;
        var unrelated = Path.Combine(planRoot, "notes");
        var emptyRequest = Path.Combine(planRoot, Guid.Empty.ToString("N"));
        var nestedPlan = Path.Combine(unrelated, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyRequest);
        Directory.CreateDirectory(nestedPlan);
        Directory.SetLastWriteTimeUtc(
            unrelated,
            Now.AddDays(-8).UtcDateTime);

        var result = new ReleaseInstallationPlanCleaner(
            new FixedTimeProvider(Now)).Clean(root);

        Assert.Equal(1, result.DeletedPlans);
        Assert.Equal(1, result.RetainedPlans);
        Assert.Empty(result.Failures);
        Assert.False(Directory.Exists(oldPlan));
        Assert.True(Directory.Exists(recentPlan));
        Assert.True(Directory.Exists(unrelated));
        Assert.True(Directory.Exists(emptyRequest));
        Assert.True(Directory.Exists(nestedPlan));
    }

    [Fact]
    public void CleanReturnsAnEmptyResultWhenThePlanRootDoesNotExist()
    {
        var result = new ReleaseInstallationPlanCleaner(
            new FixedTimeProvider(Now)).Clean(root);

        Assert.Equal(0, result.DeletedPlans);
        Assert.Equal(0, result.RetainedPlans);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void CleanRetainsAndReportsAPlanThatCannotBeDeleted()
    {
        var plan = CreatePlan(Guid.NewGuid(), Now.AddDays(-2));
        var cleaner = new ReleaseInstallationPlanCleaner(
            new FixedTimeProvider(Now),
            minimumAge: TimeSpan.FromHours(24),
            _ => throw new UnauthorizedAccessException("locked"));

        var result = cleaner.Clean(root);

        Assert.Equal(0, result.DeletedPlans);
        Assert.Equal(1, result.RetainedPlans);
        var failure = Assert.Single(result.Failures);
        Assert.Contains(plan, failure);
        Assert.Contains("locked", failure);
        Assert.True(Directory.Exists(plan));
    }

    [Fact]
    public async Task CleanAsyncHonorsCancellationWithoutDeletingPlans()
    {
        var plan = CreatePlan(Guid.NewGuid(), Now.AddDays(-2));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ReleaseInstallationPlanCleaner(
                new FixedTimeProvider(Now),
                minimumAge: TimeSpan.Zero)
                .CleanAsync(root, cancellation.Token));

        Assert.True(Directory.Exists(plan));
    }

    [Fact]
    public async Task CoordinatorRunsPlanCleanupThroughTheSharedGate()
    {
        var plan = CreatePlan(Guid.NewGuid(), Now.AddDays(-2));
        var coordinator = new ReleaseUpdateHistoryCleanupCoordinator();

        var result = await coordinator.CleanPlansAsync(
            new ReleaseInstallationPlanCleaner(new FixedTimeProvider(Now)),
            root);

        Assert.Equal(1, result.DeletedPlans);
        Assert.False(Directory.Exists(plan));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private string CreatePlan(Guid requestId, DateTimeOffset lastWriteTime)
    {
        var path = Path.Combine(
            root,
            "updates",
            "install-plans",
            requestId.ToString("N"));
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "plan.json"), "{}");
        Directory.SetLastWriteTimeUtc(path, lastWriteTime.UtcDateTime);
        return path;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
