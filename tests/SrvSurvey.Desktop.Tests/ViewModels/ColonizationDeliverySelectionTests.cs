using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationViewModelTests
{
    /// <summary>Requires an explicit choice, updates button availability, and treats unchecked actions as no-ops.</summary>
    [Fact]
    public async Task DeliveryRecoveryRequiresSelection()
    {
        SeedUnconfirmedDeliveries();
        var client = new StubRavenColonialClient();
        using ColonizationViewModel vm = await CreateRecoveryAsync(client);
        Assert.All(vm.UnconfirmedContributions, row => Assert.False(row.IsSelected));
        Assert.False(vm.RetryUnconfirmedContributionsCommand.CanExecute(null));
        Assert.False(vm.DismissConfirmedContributionsCommand.CanExecute(null));
        await vm.RetryUnconfirmedContributionsAsync();
        vm.DismissConfirmedContributions();
        Assert.Empty(client.Contributions);
        Assert.Equal(3, vm.UnconfirmedContributions.Count);

        ColonizationPendingContributionRowViewModel row = vm.UnconfirmedContributions[0];
        var propertyChanges = new List<string?>();
        row.PropertyChanged += (_, args) => propertyChanges.Add(args.PropertyName);
        int commandChanges = 0;
        vm.RetryUnconfirmedContributionsCommand.CanExecuteChanged += (_, _) => commandChanges++;
        row.IsSelected = true;
        row.IsSelected = true;
        Assert.True(vm.RetryUnconfirmedContributionsCommand.CanExecute(null));
        Assert.True(vm.DismissConfirmedContributionsCommand.CanExecute(null));
        Assert.Equal(1, commandChanges);
        Assert.Equal([nameof(row.IsSelected)], propertyChanges);
        row.IsSelected = false;
        Assert.False(vm.RetryUnconfirmedContributionsCommand.CanExecute(null));
        Assert.False(vm.DismissConfirmedContributionsCommand.CanExecute(null));
        Assert.Equal(2, commandChanges);
    }

    /// <summary>Retries only checked journal deliveries and persists unchecked identical cargo independently.</summary>
    [Fact]
    public async Task RetriesOnlySelectedDeliveries()
    {
        SeedUnconfirmedDeliveries();
        var client = new StubRavenColonialClient();
        using ColonizationViewModel vm = await CreateRecoveryAsync(client);
        vm.UnconfirmedContributions[0].IsSelected = true;
        vm.UnconfirmedContributions[2].IsSelected = true;
        await vm.RetryUnconfirmedContributionsAsync();
        Assert.Equal(["build-a", "build-c"], client.Contributions.Select(call => call.BuildId));
        ColonizationPendingContributionRowViewModel remaining = Assert.Single(vm.UnconfirmedContributions);
        Assert.Equal("delivery-b", remaining.EventId);
        Assert.False(remaining.IsSelected);
        Assert.True(vm.HasUncertainContributions);
        Assert.Contains("5 units", remaining.Summary);
        using ColonizationViewModel restarted = await CreateRecoveryAsync(client);
        Assert.Equal("delivery-b", Assert.Single(restarted.UnconfirmedContributions).EventId);
        Assert.False(restarted.UnconfirmedContributions[0].IsSelected);
        Assert.False(restarted.RetryUnconfirmedContributionsCommand.CanExecute(null));
    }

    /// <summary>Dismisses selected records locally, leaving all other deliveries available after restart.</summary>
    [Fact]
    public async Task DismissesOnlySelectedDeliveries()
    {
        SeedUnconfirmedDeliveries();
        var client = new StubRavenColonialClient();
        using ColonizationViewModel vm = await CreateRecoveryAsync(client);
        vm.UnconfirmedContributions[1].IsSelected = true;
        vm.DismissConfirmedContributions();
        Assert.Empty(client.Contributions);
        Assert.Equal(["delivery-a", "delivery-c"], vm.UnconfirmedContributions.Select(row => row.EventId));
        using ColonizationViewModel restarted = await CreateRecoveryAsync(client);
        Assert.Equal(2, restarted.UnconfirmedContributions.Count);
        foreach (ColonizationPendingContributionRowViewModel row in restarted.UnconfirmedContributions)
        {
            row.IsSelected = true;
        }
        restarted.DismissConfirmedContributions();
        Assert.False(restarted.HasUncertainContributions);
        Assert.Empty(restarted.UnconfirmedContributions);
        Assert.Empty(client.Contributions);
    }

    /// <summary>A second lost response retains just the selected delivery and requires fresh verification before another attempt.</summary>
    [Fact]
    public async Task AmbiguousRetryRequiresNewSelection()
    {
        SeedUnconfirmedDeliveries();
        var client = new StubRavenColonialClient();
        client.ContributionFailures.Enqueue(new HttpRequestException("lost response again"));
        using ColonizationViewModel vm = await CreateRecoveryAsync(client);
        vm.UnconfirmedContributions[0].IsSelected = true;
        await vm.RetryUnconfirmedContributionsAsync();
        Assert.Single(client.Contributions);
        Assert.Equal(3, vm.UnconfirmedContributions.Count);
        Assert.All(vm.UnconfirmedContributions, row => Assert.False(row.IsSelected));
        Assert.Contains("remain pending", vm.StatusMessage);
        await vm.RetryUnconfirmedContributionsAsync();
        recoveryTime = recoveryTime.AddSeconds(6);
        await vm.SynchronizeLiveProjectsAsync([], true);
        Assert.Single(client.Contributions);
        using ColonizationViewModel restarted = await CreateRecoveryAsync(client);
        Assert.Equal(3, restarted.UnconfirmedContributions.Count);
    }

    /// <summary>Prevents a second retry or local dismissal while an earlier selected delivery is awaiting acknowledgement.</summary>
    [Fact]
    public async Task RecoveryCannotOverlapDeliveryUpload()
    {
        SeedUnconfirmedDeliveries();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new StubRavenColonialClient { ContributionResponseTask = gate.Task };
        using ColonizationViewModel vm = await CreateRecoveryAsync(client);
        vm.UnconfirmedContributions[0].IsSelected = true;
        Task retry = vm.RetryUnconfirmedContributionsAsync();
        try
        {
            Assert.False(vm.CanSelectUnconfirmedContributions);
            vm.UnconfirmedContributions[1].IsSelected = true;
            Assert.False(vm.RetryUnconfirmedContributionsCommand.CanExecute(null));
            Assert.False(vm.DismissConfirmedContributionsCommand.CanExecute(null));
            await vm.RetryUnconfirmedContributionsAsync();
            vm.DismissConfirmedContributions();
            await vm.SynchronizeLiveProjectsAsync([], true);
            Assert.Equal(3, vm.UnconfirmedContributions.Count);
            Assert.Single(client.Contributions);
        }
        finally
        {
            gate.TrySetResult();
            await retry;
        }
        Assert.True(vm.CanSelectUnconfirmedContributions);
        Assert.Equal(2, vm.UnconfirmedContributions.Count);
    }

    /// <summary>Never carries checkbox authority into another commander or a replacement Frontier profile.</summary>
    [Fact]
    public async Task DeliverySelectionIsScopedToActiveProfile()
    {
        SeedUnconfirmedDeliveries();
        var client = new StubRavenColonialClient();
        using ColonizationViewModel vm = await CreateRecoveryAsync(client);
        ColonizationPendingContributionRowViewModel oldRow = vm.UnconfirmedContributions[0];
        oldRow.IsSelected = true;
        await vm.SetCommanderAsync("Another Cmdr");
        Assert.Empty(vm.UnconfirmedContributions);
        oldRow.IsSelected = false;
        oldRow.IsSelected = true;
        vm.DismissConfirmedContributions();
        await vm.RetryUnconfirmedContributionsAsync();
        await vm.SetCommanderAsync("Test Cmdr");
        Assert.All(vm.UnconfirmedContributions, row => Assert.False(row.IsSelected));
        vm.UnconfirmedContributions[0].IsSelected = true;
        vm.SetCommanderProfile("F456", true, "key");
        Assert.Empty(vm.UnconfirmedContributions);
        vm.SetCommanderProfile("F123", true, "key");
        Assert.All(vm.UnconfirmedContributions, row => Assert.False(row.IsSelected));
        Assert.Empty(client.Contributions);
    }

    /// <summary>Stops a selected batch after a profile switch without marking its unsent deliveries safe for replay.</summary>
    [Fact]
    public async Task ProfileSwitchStopsRemainingSelectedRetries()
    {
        SeedUnconfirmedDeliveries();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new StubRavenColonialClient { ContributionResponseTask = gate.Task };
        using ColonizationViewModel vm = await CreateRecoveryAsync(client);
        foreach (ColonizationPendingContributionRowViewModel row in vm.UnconfirmedContributions)
        {
            row.IsSelected = true;
        }
        Task retry = vm.RetryUnconfirmedContributionsAsync();
        await vm.SetCommanderAsync("Another Cmdr");
        gate.SetResult();
        await retry;
        Assert.Single(client.Contributions);
        Assert.Empty(vm.UnconfirmedContributions);
        await vm.SetCommanderAsync("Test Cmdr");
        Assert.Equal(2, vm.UnconfirmedContributions.Count);
        Assert.All(vm.UnconfirmedContributions, row => Assert.False(row.IsSelected));
        await vm.SynchronizeLiveProjectsAsync([], true);
        Assert.Single(client.Contributions);
    }

    /// <summary>Honors consent changes: local dismissal remains available, while selected uploads are prohibited.</summary>
    [Fact]
    public async Task DisabledIntegrationBlocksSelectedRetry()
    {
        SeedUnconfirmedDeliveries();
        var client = new StubRavenColonialClient();
        using ColonizationViewModel vm = await CreateRecoveryAsync(client);
        vm.UnconfirmedContributions[0].IsSelected = true;
        vm.IsEnabled = false;
        Assert.False(vm.RetryUnconfirmedContributionsCommand.CanExecute(null));
        await vm.RetryUnconfirmedContributionsAsync();
        Assert.Empty(client.Contributions);
        Assert.True(vm.DismissConfirmedContributionsCommand.CanExecute(null));
        vm.DismissConfirmedContributions();
        Assert.Equal(2, vm.UnconfirmedContributions.Count);
    }

    /// <summary>Preserves an explicit checkbox choice across idle refreshes without automatically checking new deliveries.</summary>
    [Fact]
    public async Task IdleRecoveryPreservesDeliverySelection()
    {
        SeedUnconfirmedDeliveries();
        var client = new StubRavenColonialClient();
        using ColonizationViewModel vm = await CreateRecoveryAsync(client);
        vm.UnconfirmedContributions[1].IsSelected = true;
        await vm.SynchronizeLiveProjectsAsync([], true);
        Assert.True(vm.UnconfirmedContributions[1].IsSelected);
        Assert.False(vm.UnconfirmedContributions[0].IsSelected);
        Assert.False(vm.UnconfirmedContributions[2].IsSelected);
        Assert.Empty(client.Contributions);
    }

    /// <summary>Creates distinct journal identities, including identical cargo, in the normal persisted recovery store.</summary>
    private void SeedUnconfirmedDeliveries()
    {
        new ColonizationSettingsStore(Path.Combine(directory, "recovery.json")).SavePendingContributions([
            new("Test Cmdr|F123|True", "build-a", "Test Cmdr", new() { ["steel"] = 5 }, "delivery-a", true),
            new("Test Cmdr|F123|True", "build-a", "Test Cmdr", new() { ["steel"] = 5 }, "delivery-b", true),
            new("Test Cmdr|F123|True", "build-c", "Test Cmdr", new() { ["titanium"] = 10 }, "delivery-c", true),
        ]);
    }
}
