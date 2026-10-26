using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>Owns the cancellable destination lookup so stale replies cannot replace another jump or commander.</summary>
internal sealed class CarrierDestinationLookup(CarrierNextJumpDependencies dependencies) : IDisposable
{
    private CancellationTokenSource? cancellation;

    public Task Pending { get; private set; } = Task.CompletedTask;

    public void Load(
        (string Name, long Address) target,
        string? originSystem,
        Action<SystemSummary, GalacticCoordinate?> apply
    )
    {
        Reset();
        cancellation = new();
        Pending = LoadAsync(target, originSystem, apply, cancellation.Token);
    }

    public void Reset()
    {
        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = null;
    }

    public void Dispose() => Reset();

    private async Task LoadAsync(
        (string Name, long Address) target,
        string? originSystem,
        Action<SystemSummary, GalacticCoordinate?> apply,
        CancellationToken token
    )
    {
        try
        {
            long address = target.Address;
            if (address <= 0)
            {
                address = (await ResolveAsync(target.Name, token))?.SystemAddress ?? 0;
            }
            token.ThrowIfCancellationRequested();
            SystemSummaryLoadResult result = await dependencies.SummaryClient.GetAsync(target.Name, address, token);
            token.ThrowIfCancellationRequested();
            GalacticCoordinate? origin = string.IsNullOrEmpty(originSystem)
                ? null
                : (await ResolveAsync(originSystem, token))?.Position;
            token.ThrowIfCancellationRequested();
            apply(result.Summary, origin);
        }
        catch (OperationCanceledException)
        {
            // Superseded or timed-out lookups cannot publish results into the next context.
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or HttpRequestException
                        or InvalidOperationException
                        or ArgumentException
                        or TimeoutException
            )
        {
            // Public lookup failures leave optional values hidden; journal tracking continues.
        }
    }

    private async Task<StarSystemReference?> ResolveAsync(string name, CancellationToken token) =>
        dependencies.Resolver is { } resolver
            ? (await resolver.SearchAsync(name, token)).FirstOrDefault(system =>
                system.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            )
            : null;
}
