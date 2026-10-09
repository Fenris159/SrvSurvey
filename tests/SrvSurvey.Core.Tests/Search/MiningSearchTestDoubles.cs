using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

internal sealed class StubMiningSearchProvider : IMiningSearchProvider
{
    public List<string> Calls { get; } = [];
    public int DiagnosticResets { get; private set; }
    public int DiagnosticFlushes { get; private set; }
    public List<MiningMarketQuery> MarketQueries { get; } = [];
    public List<MiningPlanetaryQuery> BodyQueries { get; } = [];
    public List<MiningSystemQuery> SystemPageQueries { get; } = [];
    public List<MiningSystemQuery> CandidatePageQueries { get; } = [];

    public Func<MiningMarketQuery, (IReadOnlyList<MiningMarketResult> Markets, string Source)> Markets { get; set; } =
        _ => ([], "Ardent");

    public Func<string, MiningMarketQuery, IReadOnlyList<MiningMarketResult>> Imports { get; set; } = (_, _) => [];

    public Func<string, string, IReadOnlyList<MiningMarketResult>> SpanshCommodities { get; set; } = (_, _) => [];

    public Func<MiningPlanetaryQuery, MiningPlanetaryBodyPage> BodyPages { get; set; } = _ => new([], false);

    public Func<string, IReadOnlyList<MiningPlanetaryBody>, IReadOnlySet<string>> WhiteDwarfHosted { get; set; } =
        (_, _) => new HashSet<string>();

    public Func<string, IReadOnlyList<string>, IReadOnlyList<MiningSystemResult>> SystemsByName { get; set; } =
        (_, _) => [];

    public Func<string, string, string, IReadOnlyList<MiningSystemResult>> Supporters { get; set; } = (_, _, _) => [];

    public Func<MiningSystemQuery, MiningSystemPage> SystemPages { get; set; } = _ => new([], false);

    public Func<MiningSystemQuery, MiningSystemPage> CandidatePages { get; set; } = _ => new([], false);

    public IReadOnlyDictionary<string, long> AverageSellPrices { get; set; } =
        new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, MiningCommodityPriceSummary> PriceReport { get; set; } =
        new Dictionary<string, MiningCommodityPriceSummary>(StringComparer.OrdinalIgnoreCase);

    public IDisposable BeginDiagnostics()
    {
        DiagnosticResets++;
        return new DiagnosticScope(this);
    }

    private sealed class DiagnosticScope(StubMiningSearchProvider owner) : IDisposable
    {
        public void Dispose() => owner.DiagnosticFlushes++;
    }

    public Task<IReadOnlyDictionary<string, MiningCommodityPriceSummary>> CommodityPriceReportAsync(
        CancellationToken cancellationToken = default
    )
    {
        Calls.Add("report");
        return Task.FromResult(PriceReport);
    }

    public Task<IReadOnlyDictionary<string, long>> AverageSellPricesAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("averages");
        return Task.FromResult(AverageSellPrices);
    }

    public Task<(IReadOnlyList<MiningMarketResult> Markets, string Source)> FindMarketsPreferringArdentAsync(
        MiningMarketQuery query,
        CancellationToken cancellationToken = default
    )
    {
        Calls.Add("markets " + query.Commodity + " " + query.Radius);
        MarketQueries.Add(query);
        return Task.FromResult(Markets(query));
    }

    public Task<IReadOnlyList<MiningMarketResult>> FindSystemImportsAsync(
        string system,
        MiningMarketQuery query,
        CancellationToken cancellationToken = default
    )
    {
        Calls.Add("imports " + system);
        return Task.FromResult(Imports(system, query));
    }

    public Task<IReadOnlyList<MiningMarketResult>> FindSpanshSystemCommoditiesAsync(
        string reference,
        string system,
        CancellationToken cancellationToken = default
    )
    {
        Calls.Add("spansh " + reference + " " + system);
        return Task.FromResult(SpanshCommodities(reference, system));
    }

    public Task<MiningPlanetaryBodyPage> FindPlanetaryBodyPageAsync(
        MiningPlanetaryQuery query,
        CancellationToken cancellationToken = default
    )
    {
        Calls.Add("bodies " + query.ReferenceSystem);
        BodyQueries.Add(query);
        return Task.FromResult(BodyPages(query));
    }

    public Task<IReadOnlySet<string>> FindWhiteDwarfHostedBodiesAsync(
        string reference,
        IReadOnlyList<MiningPlanetaryBody> bodies,
        CancellationToken cancellationToken = default
    )
    {
        Calls.Add("white-dwarf " + reference);
        return Task.FromResult(WhiteDwarfHosted(reference, bodies));
    }

    public Task<IReadOnlyList<MiningSystemResult>> FindSystemsByNameAsync(
        string reference,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken = default
    )
    {
        Calls.Add("names " + string.Join(",", names));
        return Task.FromResult(SystemsByName(reference, names));
    }

    public Task<IReadOnlyList<MiningSystemResult>> FindAcquireSupportersAsync(
        string reference,
        string power,
        string state,
        CancellationToken cancellationToken = default
    )
    {
        Calls.Add("supporters " + state);
        return Task.FromResult(Supporters(reference, power, state));
    }

    public Task<MiningSystemPage> FindSystemPageAsync(
        MiningSystemQuery query,
        CancellationToken cancellationToken = default
    )
    {
        Calls.Add("systems " + query.ReferenceSystem + " " + query.Page);
        SystemPageQueries.Add(query);
        return Task.FromResult(SystemPages(query));
    }

    public Task<MiningSystemPage> FindAcquireCandidatePageAsync(
        MiningSystemQuery query,
        CancellationToken cancellationToken = default
    )
    {
        Calls.Add("candidates " + query.ReferenceSystem + " " + query.Page);
        CandidatePageQueries.Add(query);
        return Task.FromResult(CandidatePages(query));
    }

    public static MiningMarketResult Quote(
        string system,
        string station,
        string commodity,
        long price,
        double? distance = 10,
        long demand = 1_000
    ) =>
        new(system, station, "Coriolis", distance, 100, price, demand, 0, DateTimeOffset.UtcNow, 1, true)
        {
            Commodity = commodity,
        };

    public static MiningSystemResult System(
        string name,
        double? distance = 10,
        string power = "",
        string powerState = "",
        GalacticCoordinate? position = null
    ) => new(name, distance, "", "", "", "", "", power, powerState, 0, position);

    public static MiningPlanetaryBody Body(
        string system,
        string name,
        double distance,
        string subtype = "Rocky body"
    ) => new(system, system + " " + name, subtype, "", 0.4, 100, distance);
}

internal sealed class InMemoryMiningSearchResultStore : IMiningSearchResultStore
{
    private readonly Dictionary<string, object?> entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> lastKeys = new(StringComparer.Ordinal);
    private bool hideIrrelevantMaterialTags;

    public event Action<bool>? HideIrrelevantMaterialTagsChanged;

    public bool HideIrrelevantMaterialTags
    {
        get => hideIrrelevantMaterialTags;
        set
        {
            hideIrrelevantMaterialTags = value;
            HideIrrelevantMaterialTagsChanged?.Invoke(value);
        }
    }

    public int Loads { get; private set; }

    public T? Load<T>(string workspace, string key)
    {
        Loads++;
        return entries.TryGetValue(workspace + "\u001f" + key, out object? value) && value is T typed ? typed : default;
    }

    public T? LoadLast<T>(string workspace) =>
        lastKeys.TryGetValue(workspace, out string? key) ? Load<T>(workspace, key) : default;

    public void Save<T>(string workspace, string key, T snapshot)
    {
        entries[workspace + "\u001f" + key] = snapshot;
        lastKeys[workspace] = key;
    }
}

internal sealed class RecordingProgress<T> : IProgress<T>
{
    public List<T> Reports { get; } = [];

    public void Report(T value) => Reports.Add(value);
}
