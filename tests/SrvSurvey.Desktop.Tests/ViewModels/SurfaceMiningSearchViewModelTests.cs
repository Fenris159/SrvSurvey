using System.Net;
using System.Net.Http;
using System.Text;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class SurfaceMiningSearchViewModelTests
{
    private const string Material = "Diamond";

    [Fact]
    public async Task EmptyReferenceAndUnknownMaterialDoNotSearch()
    {
        using var handler = new SurfaceHandler();
        using SurfaceMiningSearchViewModel model = Create(handler);

        await model.SearchAsync();

        Assert.Equal("Choose a reference system and a surface material.", model.Status);
        Assert.False(model.HasRows);
        model.Reference = "Sol";
        model.Materials.Selected.Clear();
        model.Materials.Selected.Add("Nope");

        await model.SearchAsync();

        Assert.Equal("Choose a surface material.", model.Status);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task SearchGroupsBodiesUnderTheBestSellAndCanResort()
    {
        using var handler = new SurfaceHandler();
        using SurfaceMiningSearchViewModel model = Create(handler);
        model.Reference = "  Sol  ";
        model.Radius = 40;
        model.Reserve = " ";
        Assert.Equal("All", model.Reserve);
        model.Reserve = "Pristine";
        model.Materials.Add(Material);

        await model.SearchAsync();

        SurfaceSellRowViewModel row = Assert.Single(model.Rows);
        Assert.Equal("Sell System", row.Target);
        Assert.Equal("12 ly", row.Distance);
        Assert.Equal(["Alpha", "Beta", "Gamma"], row.Bodies.Select(body => body.System).ToArray());
        Assert.Contains("Pristine reserve", row.Bodies[0].Details);
        Assert.DoesNotContain("reserve", row.Bodies[1].Details);
        Assert.DoesNotContain(" ly", row.Bodies[2].Details);
        AcquireStationViewModel station = Assert.Single(row.Stations);
        Assert.Equal("Gold Port", station.Name);
        Assert.Equal("DIA", Assert.Single(station.Quotes).Code);
        Assert.Contains("3 landable bodies for Diamond", model.Status);
        Assert.Contains("Ardent", model.Status);
        Assert.True(model.HasRows);

        model.DistanceSortCommand.Execute(null);

        Assert.Equal("Farthest first", model.DistanceSortLabel);
        Assert.Equal(
            ["Gamma", "Beta", "Alpha"],
            Assert.Single(model.Rows).Bodies.Select(body => body.System).ToArray()
        );
    }

    [Fact]
    public async Task MissingImportsAndAveragesStillKeepTheBestQuote()
    {
        using var handler = new SurfaceHandler { Mode = "imports-fail" };
        using SurfaceMiningSearchViewModel model = Create(handler);
        model.Reference = "Sol";
        model.Materials.Add(Material);
        var notes = new List<string>();
        model.UseDiagnosticLog(notes.Add);

        await model.SearchAsync();

        Assert.Equal("DIA", Assert.Single(Assert.Single(Assert.Single(model.Rows).Stations).Quotes).Code);
        Assert.NotEmpty(notes);

        using SurfaceHandler averages = new() { Mode = "averages-bad" };
        using SurfaceMiningSearchViewModel priced = Create(averages);
        priced.Reference = "Sol";
        priced.Materials.Add(Material);
        await priced.SearchAsync();

        Assert.Contains("Request failed. Try again.", priced.Status);
        Assert.True(priced.HasRows);
    }

    [Fact]
    public async Task ProviderFailureAndCancellationUpdateStatus()
    {
        using var handler = new SurfaceHandler { Mode = "fail" };
        using SurfaceMiningSearchViewModel model = Create(handler);
        model.Reference = "Sol";
        model.Materials.Add(Material);

        await model.SearchAsync();

        Assert.Equal("Request failed. Try again.", model.Status);
        Assert.False(model.HasRows);

        handler.Mode = "delay";
        Task search = model.SearchAsync();
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        model.Cancel();
        await search;

        Assert.Equal("Search canceled or timed out.", model.Status);
        Assert.False(model.IsBusy);
        model.Dispose();
    }

    private static SurfaceMiningSearchViewModel Create(SurfaceHandler handler) =>
        new(new SrvSurvey.Core.Search.MiningSearchClient(new HttpClient(handler)));

    private sealed class SurfaceHandler : HttpMessageHandler
    {
        public string Mode { get; set; } = "ok";
        public int Requests { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests++;
            string path = request.RequestUri!.AbsolutePath;
            if (Mode == "delay")
            {
                Started.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (Mode == "fail" && path.Contains("bodies/search", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            if (path.Contains("bodies/search", StringComparison.Ordinal))
            {
                return Json(
                    """
                    {"results":[
                      {"name":"Alpha 1","system_name":"Alpha","subtype":"Rocky body","reserve_level":"Pristine","gravity":0.4,"distance_to_arrival":200,"distance":4.2},
                      {"name":"Beta 1","system_name":"Beta","subtype":"Icy body","reserve_level":"","gravity":0.1,"distance_to_arrival":50,"distance":9.5},
                      {"name":"Gamma 1","system_name":"Gamma","subtype":"Rocky body","gravity":1.2,"distance_to_arrival":10}
                    ]}
                    """
                );
            }

            if (path.Contains("/commodities/imports", StringComparison.Ordinal))
            {
                if (Mode == "imports-fail")
                {
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                }

                return Json(Markets(extra: true));
            }

            if (path.EndsWith("/commodities", StringComparison.Ordinal))
            {
                return Json(Mode == "averages-bad" ? "{}" : """[{"commodityName":"Diamond","avgSellPrice":100000}]""");
            }

            return Json(Markets(extra: false));
        }

        private static string Markets(bool extra)
        {
            string updated = DateTimeOffset.UtcNow.ToString("O");
            string quote =
                $$"""{"systemName":"Sell System","stationName":"Gold Port","stationType":"Coriolis","maxLandingPadSize":3,"sellPrice":200000,"demand":1000,"stock":10,"updatedAt":"{{updated}}","distance":12.4,"distanceToArrival":150,"marketId":9,"commodityName":"Diamond"}""";
            if (!extra)
            {
                return "[" + quote + "]";
            }

            return "["
                + quote
                + $$""",{"systemName":"Sell System","stationName":"Gold Port","stationType":"Coriolis","maxLandingPadSize":3,"sellPrice":150000,"demand":40,"stock":10,"updatedAt":"{{updated}}","distance":12.4,"distanceToArrival":150,"marketId":9,"commodityName":"Diamond"}"""
                + $$""",{"systemName":"Sell System","stationName":"Other Port","stationType":"Coriolis","maxLandingPadSize":3,"sellPrice":300000,"demand":40,"stock":10,"updatedAt":"{{updated}}","distance":12.4,"distanceToArrival":20,"marketId":8,"commodityName":"Diamond"}"""
                + $$""",{"systemName":"Sell System","stationName":"Gold Port","stationType":"Coriolis","maxLandingPadSize":3,"sellPrice":500000,"demand":40,"stock":10,"updatedAt":"{{updated}}","distance":12.4,"distanceToArrival":150,"marketId":9,"commodityName":"Gold"}"""
                + "]";
        }

        private static HttpResponseMessage Json(string payload) =>
            new(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
    }
}
