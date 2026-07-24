using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests;

public sealed class StatusFileReaderTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-status-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task ReadAsyncPortsFlagsLocationAndUnknownFields()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, StatusFileReader.FileName);
        var flags = (uint)(StatusFlags.InSrv
            | StatusFlags.HasLatLong
            | StatusFlags.SrvHighBeam);
        var flags2 = (uint)(StatusFlags2.OnFoot | StatusFlags2.OnFootExterior);
        var json = $"{{\"timestamp\":\"2026-07-24T12:00:00Z\","
            + $"\"event\":\"Status\",\"Flags\":{flags},\"Flags2\":{flags2},"
            + "\"Pips\":[4,2,0],\"FireGroup\":1,\"GuiFocus\":0,"
            + "\"Latitude\":12.5,\"Longitude\":-44.25,\"Heading\":-1,"
            + "\"FutureStatusValue\":{\"Enabled\":true}}";
        await File.WriteAllTextAsync(path, json);

        var result = await StatusFileReader.ReadAsync(path);

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(result.Status);
        Assert.True(result.Status.InSrv);
        Assert.True(result.Status.HasLatitudeLongitude);
        Assert.True(result.Status.Flags.HasFlag(StatusFlags.SrvHighBeam));
        Assert.True(result.Status.OnFootExterior);
        Assert.Equal(359, result.Status.NormalizedHeading);
        Assert.Equal(12.5, result.Status.Latitude);
        Assert.NotNull(result.Status.AdditionalProperties);
        Assert.True(result.Status.AdditionalProperties["FutureStatusValue"]
            .GetProperty("Enabled").GetBoolean());
    }

    [Fact]
    public async Task ReadAsyncRetriesMalformedPartialWrite()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, StatusFileReader.FileName);
        await File.WriteAllTextAsync(path, "{\"event\":\"Status\"");

        var result = await StatusFileReader.ReadAsync(
            path,
            maximumAttempts: 2,
            retryDelay: TimeSpan.Zero);

        Assert.False(result.IsSuccess);
        Assert.Equal(2, result.Attempts);
        Assert.Contains("after 2 attempts", result.Error);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
