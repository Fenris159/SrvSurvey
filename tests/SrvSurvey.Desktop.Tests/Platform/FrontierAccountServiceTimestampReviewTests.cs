using System.Net;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Desktop.Platform.Frontier;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed partial class FrontierAccountServiceTests
{
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task ProfileAuthorizationRetryUsesTheSuccessfulRequestsStartTime(HttpStatusCode challenge)
    {
        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-profile-retry-{Guid.NewGuid():N}");
        DateTimeOffset at = DateTimeOffset.UnixEpoch;
        var store = new MemoryCredentialStore { Document = LinkedCredential(at) };
        var attempts = new List<DateTimeOffset>();
        try
        {
            using FrontierAccountService service = CreateService(
                store,
                request =>
                {
                    string path = request.RequestUri!.AbsolutePath;
                    DateTimeOffset started = at;
                    at = at.AddSeconds(1);
                    if (path == "/profile")
                    {
                        attempts.Add(started);
                        return attempts.Count == 1
                            ? Json(challenge, "")
                            : Json(HttpStatusCode.OK, """{"commander":{"name":"Fenris"}}""");
                    }
                    return path == "/token"
                        ? Json(
                            HttpStatusCode.OK,
                            """{"access_token":"retry-access","refresh_token":"retry-refresh","token_type":"Bearer","expires_in":14400}"""
                        )
                        : Json(HttpStatusCode.NoContent, "");
                },
                root,
                () => at
            );
            FrontierAccountSnapshot snapshot = await service.RefreshAsync();
            Assert.Equal(2, attempts.Count);
            Assert.True(attempts[1] > attempts[0]);
            Assert.Equal(attempts[1], snapshot.FetchedAt);
            Assert.Equal("retry-access", store.Document.Accounts["F123"].AccessToken);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
