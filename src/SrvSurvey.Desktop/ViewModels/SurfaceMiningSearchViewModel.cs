using System.Globalization;
using System.Windows.Input;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Desktop.ViewModels;

public sealed record SurfaceBodyLine(string System, string Details, double DistanceLy, string Connector);

public sealed record SurfaceSellRowViewModel(
    string Target,
    string Distance,
    IReadOnlyList<AcquireStationViewModel> Stations,
    IReadOnlyList<SurfaceBodyLine> Bodies
);

public sealed class SurfaceMiningSearchViewModel : WorkspaceObservable, IDisposable
{
    private const string RequestFailed = "Request failed. Try again.";
    private readonly MiningSearchClient client;
    private CancellationTokenSource? pending;
    private string reference = "";
    private double radius = 100;
    private string reserve = "All";
    private string status = "Choose a reference system and a surface material.";
    private bool busy;
    private bool nearestFirst = true;
    private IReadOnlyList<SurfaceSellRowViewModel> rows = [];

    public SurfaceMiningSearchViewModel(MiningSearchClient client)
    {
        this.client = client;
        SearchCommand = new WorkspaceCommand(() => _ = SearchAsync());
        CancelCommand = new WorkspaceCommand(Cancel);
        DistanceSortCommand = new WorkspaceCommand(ToggleDistanceSort);
    }

    public MiningChipBoxViewModel Materials { get; } =
        new(
            "Mineral / metal",
            new[] { MiningMaterialSelection.Default, MiningMaterialSelection.Any }
                .Concat(PlanetaryMiningPlan.Materials)
                .ToArray(),
            MiningMaterialSelection.Default,
            [MiningMaterialSelection.Default, MiningMaterialSelection.Any]
        );

    public static IReadOnlyList<string> Reserves { get; } =
    ["All", "Pristine", "Major", "Common", "Low", "Depleted", "Unknown"];

    public ICommand SearchCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand DistanceSortCommand { get; }
    public string DistanceSortLabel => nearestFirst ? "Nearest first" : "Farthest first";

    public string Reference
    {
        get => reference;
        set => Set(ref reference, value ?? "");
    }

    public double Radius
    {
        get => radius;
        set => Set(ref radius, value);
    }

    public string Reserve
    {
        get => reserve;
        set => Set(ref reserve, string.IsNullOrWhiteSpace(value) ? "All" : value);
    }

    public string Status
    {
        get => status;
        private set => Set(ref status, value);
    }

    public bool IsBusy
    {
        get => busy;
        private set => Set(ref busy, value);
    }

    public IReadOnlyList<SurfaceSellRowViewModel> Rows
    {
        get => rows;
        private set => Set(ref rows, value);
    }

    public bool HasRows => Rows.Count > 0;

    public void UseDiagnosticLog(Action<string>? log) => client.DiagnosticLog = log;

    private async Task<string> FindSurfaceSalesAsync(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(Reference))
        {
            Rows = [];
            Changed(nameof(HasRows));
            return "Choose a reference system and a surface material.";
        }

        IReadOnlyList<string> materials = SurfaceMiningSearchPlan.MaterialsFor(Materials.Selected);
        PlanetaryBodyCriteria? criteria = PlanetaryMiningPlan.For(materials);
        if (criteria is null)
        {
            Rows = [];
            Changed(nameof(HasRows));
            return "Choose a surface material.";
        }

        IReadOnlyList<MiningPlanetaryBody> bodies = await client.FindPlanetaryBodiesAsync(
            new MiningPlanetaryQuery(
                Reference.Trim(),
                criteria.BodySubtypes,
                criteria.LandmarkSubtypes,
                SurfaceMiningSearchPlan.SpanshReserve(Reserve),
                Radius
            ),
            token
        );
        List<MiningMarketResult> quotes = [];
        string source = "Ardent";
        foreach (string material in materials)
        {
            token.ThrowIfCancellationRequested();
            (IReadOnlyList<MiningMarketResult> found, source) = await client.FindMarketsPreferringArdentAsync(
                new MiningMarketQuery(Reference.Trim(), material, false, Radius),
                token
            );
            quotes.AddRange(found);
        }

        IReadOnlyDictionary<string, long> averages = await client.AverageSellPricesAsync(token);
        MiningMarketResult? best = SurfaceMiningSearchPlan.BestSell(quotes);
        AcquireStationViewModel[] stations = await StationsForAsync(best, materials, averages, token);
        MiningPlanetaryBody[] orderedBodies = bodies
            .OrderBy(body => body.DistanceLy ?? double.MaxValue)
            .Take(30)
            .ToArray();
        Rows = orderedBodies.Length == 0 ? [] : [Describe(orderedBodies, best, stations)];
        Changed(nameof(HasRows));
        return orderedBodies.Length
            + " landable bodies for "
            + string.Join(", ", materials)
            + ". Best sell from "
            + source
            + "."
            + (client.PriceMarksUnavailable ? " " + RequestFailed : "");
    }

    public async Task SearchAsync()
    {
        CancellationTokenSource? previous = pending;
        using var current = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        pending = current;
        IsBusy = true;
        Status = "Searching…";
        client.ResetDiagnostics();
        CancellationToken token = current.Token;
        try
        {
            if (previous is not null)
            {
                await previous.CancelAsync();
            }

            Status = await FindSurfaceSalesAsync(token);
        }
        catch (OperationCanceledException)
        {
            if (pending == current)
            {
                Status = "Search canceled or timed out.";
            }
        }
        catch (Exception ex)
            when (ex is HttpRequestException or System.Text.Json.JsonException or IOException or InvalidDataException)
        {
            if (pending == current)
            {
                Rows = [];
                Changed(nameof(HasRows));
                Status = RequestFailed;
            }
        }
        catch (ArgumentException ex)
        {
            if (pending == current)
            {
                Status = ex.Message;
            }
        }
        finally
        {
            client.FlushDiagnostics();
            if (pending == current)
            {
                pending = null;
                IsBusy = false;
            }
        }
    }

    public void Cancel() => pending?.Cancel();

    public void Dispose()
    {
        pending?.Cancel();
        pending?.Dispose();
        pending = null;
    }

    private async Task<AcquireStationViewModel[]> StationsForAsync(
        MiningMarketResult? best,
        IReadOnlyList<string> materials,
        IReadOnlyDictionary<string, long> averages,
        CancellationToken token
    )
    {
        if (best is null)
        {
            return [];
        }

        List<MiningMarketResult> stationQuotes = [best];
        try
        {
            IReadOnlyList<MiningMarketResult> imports = await client.FindSystemImportsAsync(
                best.System,
                TimeSpan.FromDays(30),
                token
            );
            stationQuotes.AddRange(
                imports.Where(market =>
                    market.Station.Equals(best.Station, StringComparison.OrdinalIgnoreCase)
                    && market.Price > 0
                    && market.Demand > 0
                    && materials.Contains(market.Commodity, StringComparer.OrdinalIgnoreCase)
                )
            );
        }
        catch (Exception ex)
            when (ex is HttpRequestException or System.Text.Json.JsonException or IOException or InvalidDataException)
        {
            // The best quote still stands when the station list cannot be loaded.
        }

        MiningMarketResult[] top = stationQuotes
            .GroupBy(market => market.Commodity, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(market => market.Price).First())
            .OrderByDescending(market => market.Price)
            .Take(7)
            .ToArray();
        return
        [
            new AcquireStationViewModel(
                best.Station,
                best.PadDescription,
                best.ArrivalLs is { } arrival
                    ? "Distance: " + arrival.ToString("0", CultureInfo.CurrentCulture) + " ls"
                    : "",
                "",
                top.Select(
                        (quote, index) =>
                            new AcquireQuoteViewModel(
                                MiningCommodityCode.Abbreviate(quote.Commodity),
                                quote.Price.ToString("N0", CultureInfo.CurrentCulture)
                                    + " CR "
                                    + MiningPriceMarks.For(
                                        quote.Price,
                                        averages.TryGetValue(quote.Commodity, out long average) ? average : 0
                                    ),
                                quote.Demand.ToString("N0", CultureInfo.CurrentCulture) + " Demand",
                                index == 0
                            )
                    )
                    .ToArray()
            ),
        ];
    }

    private SurfaceSellRowViewModel Describe(
        IReadOnlyList<MiningPlanetaryBody> bodies,
        MiningMarketResult? best,
        IReadOnlyList<AcquireStationViewModel> stations
    )
    {
        MiningPlanetaryBody[] ordered = (
            nearestFirst
                ? bodies.OrderBy(body => body.DistanceLy ?? double.MaxValue)
                : bodies.OrderByDescending(body => body.DistanceLy ?? double.MaxValue)
        ).ToArray();
        SurfaceBodyLine[] lines = ordered
            .Select(
                (body, index) =>
                    new SurfaceBodyLine(
                        body.System,
                        BodyDetails(body),
                        body.DistanceLy ?? double.MaxValue,
                        AcquireConnector.ForIndex(index, ordered.Length)
                    )
            )
            .ToArray();
        return new SurfaceSellRowViewModel(
            best?.System ?? "",
            best?.Distance is { } distance ? distance.ToString("0", CultureInfo.CurrentCulture) + " ly" : "",
            best is null ? [] : stations,
            lines
        );
    }

    private static string BodyDetails(MiningPlanetaryBody body)
    {
        string details =
            body.Body
            + ": "
            + body.Subtype
            + (body.Reserve.Length == 0 ? "" : ", " + body.Reserve + " reserve")
            + ", "
            + body.Gravity.ToString("0.00", CultureInfo.CurrentCulture)
            + " g, "
            + body.ArrivalLs.ToString("0", CultureInfo.CurrentCulture)
            + " ls";
        return body.DistanceLy is { } bodyDistance
            ? details + " · " + bodyDistance.ToString("0.0", CultureInfo.CurrentCulture) + " ly"
            : details;
    }

    private void ToggleDistanceSort()
    {
        nearestFirst = !nearestFirst;
        Rows = Rows.Select(row => row with { Bodies = OrderBodies(row.Bodies) }).ToArray();
        Changed(nameof(DistanceSortLabel));
    }

    private SurfaceBodyLine[] OrderBodies(IReadOnlyList<SurfaceBodyLine> bodies)
    {
        SurfaceBodyLine[] ordered = (
            nearestFirst ? bodies.OrderBy(body => body.DistanceLy) : bodies.OrderByDescending(body => body.DistanceLy)
        ).ToArray();
        return ordered
            .Select((body, index) => body with { Connector = AcquireConnector.ForIndex(index, ordered.Length) })
            .ToArray();
    }
}
