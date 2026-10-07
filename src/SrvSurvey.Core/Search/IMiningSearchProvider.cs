namespace SrvSurvey.Core.Search;

/// <summary>The provider requests shared by ring, Powerplay, and surface mining searches.</summary>
public interface IMiningSearchProvider
{
    /// <summary>
    /// Owns this asynchronous search's diagnostics. Disposing the scope logs only its failures and
    /// restores any enclosing search's diagnostics.
    /// </summary>
    IDisposable BeginDiagnostics();

    Task<IReadOnlyDictionary<string, MiningCommodityPriceSummary>> CommodityPriceReportAsync(
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyDictionary<string, long>> AverageSellPricesAsync(CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<MiningMarketResult> Markets, string Source)> FindMarketsPreferringArdentAsync(
        MiningMarketQuery query,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<MiningMarketResult>> FindSystemImportsAsync(
        string system,
        MiningMarketQuery query,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<MiningMarketResult>> FindSpanshSystemCommoditiesAsync(
        string reference,
        string system,
        CancellationToken cancellationToken = default
    );

    Task<MiningPlanetaryBodyPage> FindPlanetaryBodyPageAsync(
        MiningPlanetaryQuery query,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlySet<string>> FindWhiteDwarfHostedBodiesAsync(
        string reference,
        IReadOnlyList<MiningPlanetaryBody> bodies,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<MiningSystemResult>> FindSystemsByNameAsync(
        string reference,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<MiningSystemResult>> FindAcquireSupportersAsync(
        string reference,
        string power,
        string state,
        CancellationToken cancellationToken = default
    );

    Task<MiningSystemPage> FindSystemPageAsync(MiningSystemQuery query, CancellationToken cancellationToken = default);

    Task<MiningSystemPage> FindAcquireCandidatePageAsync(
        MiningSystemQuery query,
        CancellationToken cancellationToken = default
    );
}
