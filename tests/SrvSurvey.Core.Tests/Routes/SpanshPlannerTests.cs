using System.Net;
using System.Text;
using System.Text.Json;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Routes;

public sealed class SpanshPlannerTests
{
    private const string Queued = """{"status":"queued","job":"74FA2952-2048-11F1-8302-B948FF6DF5C1"}""";
    private const string Slef = """
        [{"header":{"appName":"SrvSurvey"},"data":{"Ship":"sidewinder","UnladenMass":25,"FuelCapacity":{"Main":4,"Reserve":0.3},"Modules":[
        {"Slot":"FrameShiftDrive","Item":"Int_Hyperdrive_Size2_Class5","Engineering":{"Modifiers":[{"Label":"FSDOptimalMass","Value":150},{"Label":"MaxFuelPerJump","Value":1.2}]}},
        {"Slot":"Slot01_Size1","Item":"Int_GuardianFSDBooster_Size1"}]}}]
        """;

    [Fact]
    public void CatalogIncludesOnlyRequestedPlannersAndKeepsCarrierSeparate()
    {
        Assert.Equal(
            ["plotter", "riches", "tourist", "exact-plotter", "exobiology", "colonisation"],
            SpanshPlannerCatalog.ShipPlanners.Select(planner => planner.Page)
        );
        Assert.Equal("fleet-carrier", Assert.Single(SpanshPlannerCatalog.CarrierPlanners).Page);
        Assert.Equal("Galaxy Plotter", SpanshPlannerCatalog.ShipPlanners[3].ToString());
        SpanshPlannerDefinition colonisation = SpanshPlannerCatalog.ShipPlanners[5];
        SpanshPlannerRequest request = colonisation.CreateRequest(
            new Dictionary<string, string> { ["destination_system"] = "Sol" }
        );
        Assert.Equal("destination_system", Assert.Single(request.Parameters).Key);
        Assert.Equal(
            "0",
            SpanshPlannerCatalog.ShipPlanners[1].Fields.Single(field => field.Key == "use_mapping_value").Default
        );
        Assert.DoesNotContain(SpanshPlannerCatalog.ShipPlanners[4].Fields, field => field.Key == "use_mapping_value");
    }

    [Theory]
    [InlineData(0, "route")]
    [InlineData(1, "riches/route")]
    [InlineData(2, "tourist/route")]
    [InlineData(3, "generic/route")]
    [InlineData(4, "exobiology/route")]
    [InlineData(5, "colonisation/route")]
    [InlineData(6, "fleetcarrier/route")]
    public async Task EachPlannerSubmitsVerifiedEndpointAndRepeatedUnbracketedKeys(int index, string endpoint)
    {
        SpanshPlannerDefinition planner = AllPlanners()[index];
        var handler = new Handler(Queued);
        using var http = new HttpClient(handler);
        var resolver = new Resolver();
        var client = new SpanshPlannerClient(http, new Uri("https://spansh.test/api/"), resolver);
        Dictionary<string, string> values = ValidValues(planner);
        SpanshRouteReference result = await client.SubmitAsync(planner.CreateRequest(values));
        Assert.Equal(planner.Kind, result.Kind);
        Assert.Equal("/api/" + endpoint, handler.Path);
        Assert.Equal("application/x-www-form-urlencoded", handler.ContentType);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.DoesNotContain("%5B%5D", handler.Body);
        if (planner.Kind == SpanshRouteKind.Galaxy)
        {
            Assert.Equal(Slef, ReadForm(handler.Body, "ship_build").Single());
            Assert.Equal("150", ReadForm(handler.Body, "optimal_mass").Single());
            Assert.Equal("1.2", ReadForm(handler.Body, "max_fuel_per_jump").Single());
            Assert.Equal("25.3", ReadForm(handler.Body, "base_mass").Single());
            Assert.Equal("4", ReadForm(handler.Body, "range_boost").Single());
            Assert.Equal("60", ReadForm(handler.Body, "max_time").Single());
            Assert.Equal("100", ReadForm(handler.Body, "source").Single());
            Assert.Equal("200", ReadForm(handler.Body, "destination").Single());
        }
        else if (planner.Kind == SpanshRouteKind.FleetCarrier)
        {
            Assert.Equal(["100", "200"], ReadForm(handler.Body, "destinations"));
            Assert.Equal(["200"], ReadForm(handler.Body, "refuel_destinations"));
            Assert.Equal(["Sol", "Achenar"], resolver.Queries);
            Assert.Empty(ReadForm(handler.Body, "fuel_loaded"));
            Assert.Empty(ReadForm(handler.Body, "carrier_type"));
        }
        else
        {
            Assert.Empty(resolver.Queries);
            string? multi = planner.Fields.FirstOrDefault(field => field.Type == SpanshPlannerFieldType.Systems)?.Key;
            if (multi is not null)
            {
                Assert.Equal(["Sol", "Achenar"], ReadForm(handler.Body, multi));
            }
        }
    }

    [Theory]
    [InlineData("Fleet Carrier", "25000", "25000")]
    [InlineData("Squadron Carrier", "60000", "15000")]
    public void CarrierUsesSelectedCapacityAndManualFuelOnlyWhenRequested(string type, string capacity, string mass)
    {
        SpanshPlannerDefinition planner = SpanshPlannerCatalog.CarrierPlanners[0];
        Dictionary<string, string> values = ValidValues(planner);
        values["carrier_type"] = type;
        values["calculate_starting_fuel"] = "0";
        values["fuel_loaded"] = "750";
        values["tritium_stored"] = "5000";
        SpanshPlannerRequest request = planner.CreateRequest(values);
        Assert.Equal(capacity, request.Parameters.Single(pair => pair.Key == "capacity").Value);
        Assert.Equal(mass, request.Parameters.Single(pair => pair.Key == "mass").Value);
        Assert.Equal("750", request.Parameters.Single(pair => pair.Key == "fuel_loaded").Value);
        Assert.Equal("5000", request.Parameters.Single(pair => pair.Key == "tritium_stored").Value);
        Assert.DoesNotContain(request.Parameters, pair => pair.Key == "refuel_destinations");
    }

    [Theory]
    [InlineData("capacity_used", "30000")]
    [InlineData("tritium_stored", "30000")]
    [InlineData("refuel_destinations", "Sirius")]
    public void CarrierRejectsOverCapacityAndRefuelStopsOutsideItsRoute(string key, string value)
    {
        SpanshPlannerDefinition planner = SpanshPlannerCatalog.CarrierPlanners[0];
        Dictionary<string, string> values = ValidValues(planner);
        values[key] = value;
        if (key == "tritium_stored")
        {
            values["calculate_starting_fuel"] = "0";
        }
        Assert.Throws<ArgumentException>(() => planner.CreateRequest(values));
    }

    [Theory]
    [InlineData("range", "")]
    [InlineData("range", "NaN")]
    [InlineData("range", "invalid")]
    [InlineData("range", "9")]
    [InlineData("range", "101")]
    [InlineData("supercharge_multiplier", "5")]
    public void RequiredAndBoundedInputsAreValidatedBeforeSubmission(string key, string value)
    {
        SpanshPlannerDefinition planner = SpanshPlannerCatalog.ShipPlanners[0];
        Dictionary<string, string> values = ValidValues(planner);
        values[key] = value;
        Assert.Throws<ArgumentException>(() => planner.CreateRequest(values));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    public void ShipBuildRequiresJsonContainer(string value)
    {
        SpanshPlannerDefinition planner = SpanshPlannerCatalog.ShipPlanners[3];
        Dictionary<string, string> values = ValidValues(planner);
        values["ship_build"] = value;
        Assert.Throws<ArgumentException>(() => planner.CreateRequest(values));
    }

    [Theory]
    [InlineData("int_hyperdrive_size2_class5", "2", "0.012", "90", "0.9", "4")]
    [InlineData("Int_Hyperdrive_Overcharge_Size2_Class5", "2", "0.013", "100", "1", "4")]
    [InlineData("Int_Hyperdrive_Overcharge_Size8_Class5_Overchargebooster_MkII", "2.5025", "0.011", "4670", "6.8", "6")]
    public void UnengineeredAndScoDrivesUseEdsyFacts(
        string item,
        string power,
        string multiplier,
        string mass,
        string fuel,
        string supercharge
    )
    {
        string json =
            """{"UnladenMass":25,"FuelCapacity":{"Main":4,"Reserve":0.3},"Modules":[{"Slot":"frameshiftdrive","Item":"ITEM"}]}""".Replace(
                "ITEM",
                item,
                StringComparison.Ordinal
            );
        IReadOnlyDictionary<string, string> parameters = SpanshShipBuild.GetJumpParameters(json);
        Assert.Equal(power, parameters["fuel_power"]);
        Assert.Equal(multiplier, parameters["fuel_multiplier"]);
        Assert.Equal(mass, parameters["optimal_mass"]);
        Assert.Equal(fuel, parameters["max_fuel_per_jump"]);
        Assert.Equal(supercharge, parameters["supercharge_multiplier"]);
        Assert.Equal("0", parameters["range_boost"]);
        Assert.Equal("2", parameters["injection_multiplier"]);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"Modules\":[]}")]
    [InlineData("{\"Modules\":[{\"Slot\":\"FrameShiftDrive\",\"Item\":\"Int_Hyperdrive_Size2_Class5\"}]}")]
    public void IncompleteBuildsNeverInventJumpInputs(string json) =>
        Assert.ThrowsAny<ArgumentException>(() => SpanshShipBuild.GetJumpParameters(json));

    [Fact]
    public void EngineeringFallbackAndInvalidJournalNumbersAreHandled()
    {
        string json = Slef.Replace("FSDOptimalMass", "AnotherLabel", StringComparison.Ordinal);
        Assert.Equal("90", SpanshShipBuild.GetJumpParameters(json)["optimal_mass"]);
        Assert.Throws<ArgumentException>(() =>
            SpanshShipBuild.GetJumpParameters(Slef.Replace("\"Value\":150", "\"Value\":-1", StringComparison.Ordinal))
        );
        Assert.Throws<ArgumentException>(() =>
            SpanshShipBuild.GetJumpParameters(
                Slef.Replace("\"Reserve\":0.3", "\"Reserve\":-1", StringComparison.Ordinal)
            )
        );
    }

    [Theory]
    [InlineData("{\"status\":\"error\",\"error\":\"No route found\"}", typeof(InvalidDataException))]
    [InlineData("{\"job\":\"bad\"}", typeof(InvalidDataException))]
    [InlineData("{}", typeof(InvalidDataException))]
    [InlineData("[]", typeof(InvalidDataException))]
    [InlineData("broken", typeof(JsonException))]
    public async Task InvalidProviderResponsesAreReported(string json, Type exceptionType)
    {
        using var http = new HttpClient(new Handler(json));
        var client = new SpanshPlannerClient(http);
        Exception? failure = await Record.ExceptionAsync(() =>
            client.SubmitAsync(
                SpanshPlannerCatalog.ShipPlanners[0].CreateRequest(ValidValues(SpanshPlannerCatalog.ShipPlanners[0]))
            )
        );
        Assert.IsAssignableFrom(exceptionType, failure);
    }

    [Fact]
    public async Task ExactPlannerRejectsUnresolvedSystemInsteadOfChoosingASimilarName()
    {
        var handler = new Handler(Queued);
        using var http = new HttpClient(handler);
        var client = new SpanshPlannerClient(http, resolver: new Resolver());
        SpanshPlannerDefinition planner = SpanshPlannerCatalog.ShipPlanners[3];
        Dictionary<string, string> values = ValidValues(planner);
        values["destination"] = "Sol typo";
        await Assert.ThrowsAsync<ArgumentException>(() => client.SubmitAsync(planner.CreateRequest(values)));
        Assert.Null(handler.Method);
    }

    [Fact]
    public async Task PlannerResponsesAreBoundedAndCancellationIsPropagated()
    {
        using var http = new HttpClient(new Handler(new string('x', 1024 * 1024 + 1)));
        var client = new SpanshPlannerClient(http);
        SpanshPlannerDefinition planner = SpanshPlannerCatalog.ShipPlanners[0];
        SpanshPlannerRequest request = planner.CreateRequest(ValidValues(planner));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.SubmitAsync(request));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SubmitAsync(request, cancelled.Token));
    }

    private static SpanshPlannerDefinition[] AllPlanners() =>
        SpanshPlannerCatalog.ShipPlanners.Concat(SpanshPlannerCatalog.CarrierPlanners).ToArray();

    private static Dictionary<string, string> ValidValues(SpanshPlannerDefinition planner)
    {
        var values = planner.Fields.ToDictionary(field => field.Key, field => field.Default);
        foreach (SpanshPlannerField field in planner.Fields.Where(field => field.Type == SpanshPlannerFieldType.System))
        {
            values[field.Key] = field.Key is "to" or "destination" or "final_destination" or "destination_system"
                ? "Achenar"
                : "Sol";
        }
        foreach (
            SpanshPlannerField field in planner.Fields.Where(field => field.Type == SpanshPlannerFieldType.Systems)
        )
        {
            values[field.Key] = field.Key == "refuel_destinations" ? "Achenar" : " Sol \nAchenar\n";
        }
        if (values.ContainsKey("range"))
        {
            values["range"] = "50.5";
        }
        if (values.ContainsKey("ship_build"))
        {
            values["ship_build"] = Slef;
        }
        return values;
    }

    private static string[] ReadForm(string body, string key) =>
        body.Split('&')
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair[0] == key)
            .Select(pair => Uri.UnescapeDataString(pair[1].Replace('+', ' ')))
            .ToArray();

    private sealed class Resolver : IStarSystemResolver
    {
        public List<string> Queries { get; } = [];

        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default
        )
        {
            Queries.Add(query);
            return Task.FromResult<IReadOnlyList<StarSystemReference>>([
                new("Sol", 100, new(0, 0, 0)),
                new("Achenar", 200, new(1, 2, 3)),
            ]);
        }
    }

    private sealed class Handler(string json) : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string Body { get; private set; } = "";
        public string? ContentType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Path = request.RequestUri!.AbsolutePath;
            Method = request.Method;
            ContentType = request.Content!.Headers.ContentType!.MediaType;
            Body = await request.Content.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
