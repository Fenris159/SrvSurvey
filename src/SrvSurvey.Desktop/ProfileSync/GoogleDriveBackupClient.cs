using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Network;
using SrvSurvey.Core.ProfileSync;

namespace SrvSurvey.Desktop.ProfileSync;

/// <summary>Metadata for an immutable Google Drive backup.</summary>
internal sealed record DriveBackup(
    string Id,
    string Name,
    string DeviceId,
    DateTimeOffset CreatedUtc,
    string DeviceName = "",
    long? SizeBytes = null
);

/// <summary>Cloud operations needed by synchronization, independent of desktop presentation.</summary>
internal interface IGoogleDriveBackupClient : IDisposable
{
    /// <summary>Indicates whether the publisher Desktop OAuth client is available.</summary>
    bool IsConfigured { get; }

    /// <summary>Indicates whether this machine has renewable user authorization.</summary>
    bool IsLinked { get; }

    /// <summary>Links this machine through an explicit browser authorization.</summary>
    Task LinkAsync(CancellationToken cancellationToken);

    /// <summary>Removes local authorization while retaining cloud backups.</summary>
    void Disconnect();

    /// <summary>Lists SrvSurvey application-data backups across all machines.</summary>
    Task<IReadOnlyList<DriveBackup>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Downloads and validates a selected backup.</summary>
    Task<ProfileSnapshot> DownloadAsync(string id, CancellationToken cancellationToken);

    /// <summary>Downloads validated backup bytes without changing their original representation.</summary>
    Task<byte[]> DownloadBytesAsync(string id, CancellationToken cancellationToken);

    /// <summary>Permanently deletes a backup, treating an already removed file as success.</summary>
    Task DeleteAsync(string id, CancellationToken cancellationToken);

    /// <summary>Stores an immutable backup and retains bounded machine history.</summary>
    Task UploadAsync(ProfileSnapshot snapshot, CancellationToken cancellationToken);
}

/// <summary>Uses the user's browser and the restricted Drive app-data scope to store immutable backups.</summary>
internal sealed class GoogleDriveBackupClient : IGoogleDriveBackupClient
{
    private const int MaximumMetadataResponseBytes = 4 * 1024 * 1024;
    private const int MaximumTokenResponseBytes = 64 * 1024;
    private static readonly string Scope = WellKnownUris.GoogleDriveAppDataScope.AbsoluteUri;
    private static readonly string TokenEndpoint = WellKnownUris.GoogleOAuthToken.AbsoluteUri;
    private static readonly string FilesEndpoint = WellKnownUris.GoogleDriveFiles.AbsoluteUri;
    private static readonly string[] AppDataParents = ["appDataFolder"];
    private readonly ProfileSyncStore store;
    private readonly HttpClient client;
    private readonly Action<Uri> openBrowser;
    private GoogleDriveToken? token;
    private readonly string tokenPath;
    private readonly SemaphoreSlim tokenGate = new(1, 1);

    /// <summary>Creates the Drive client using local configuration or publisher assembly metadata.</summary>
    internal GoogleDriveBackupClient(ProfileSyncStore store, HttpClient? client = null, Action<Uri>? openBrowser = null)
    {
        this.store = store;
        this.client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        this.openBrowser =
            openBrowser ?? (uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }));
        tokenPath = Path.Combine(store.StateDirectory, "google-authorization.dat");
        token = ReadToken();
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientSettings().ClientId);
    public bool IsLinked => token is { RefreshToken.Length: > 0 };

    /// <summary>Completes OAuth with PKCE, a random state, and a loopback-only callback.</summary>
    public async Task LinkAsync(CancellationToken cancellationToken)
    {
        ProfileSyncPreferences settings = ClientSettings();
        if (string.IsNullOrWhiteSpace(settings.ClientId))
        {
            throw new InvalidOperationException(
                "Google Drive linking needs the publisher's Desktop OAuth client setup."
            );
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        string verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        string state = Base64Url(RandomNumberGenerator.GetBytes(32));
        using var receiver = new LoopbackOAuthReceiver();
        string redirect = receiver.RedirectUri.AbsoluteUri;
        string query = FormQuery(
            new Dictionary<string, string>
            {
                ["client_id"] = settings.ClientId,
                ["redirect_uri"] = redirect,
                ["response_type"] = "code",
                ["scope"] = Scope,
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
                ["state"] = state,
                ["access_type"] = "offline",
                ["prompt"] = "consent",
            }
        );
        openBrowser(new Uri(WellKnownUris.GoogleOAuthAuthorize.AbsoluteUri + "?" + query));
        string code = await receiver.ReceiveAsync(state, deadline.Token).ConfigureAwait(false);
        token = await ExchangeAsync(
                new Dictionary<string, string>
                {
                    ["client_id"] = settings.ClientId,
                    ["client_secret"] = settings.ClientSecret,
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["code_verifier"] = verifier,
                    ["redirect_uri"] = redirect,
                },
                deadline.Token
            )
            .ConfigureAwait(false);
        SaveToken();
    }

    /// <summary>Disconnects this installation without revoking other machines' authorizations.</summary>
    public void Disconnect()
    {
        if (File.Exists(tokenPath))
        {
            File.Delete(tokenPath);
        }

        token = null;
    }

    /// <summary>Lists only SrvSurvey's app-data backups, including all machines and retained history.</summary>
    public async Task<IReadOnlyList<DriveBackup>> ListAsync(CancellationToken cancellationToken)
    {
        var result = new List<DriveBackup>();
        string? page = null;
        do
        {
            string query = FormQuery(
                new Dictionary<string, string>
                {
                    ["spaces"] = "appDataFolder",
                    ["q"] = "trashed = false and name contains 'srvsurvey-profile-v1-'",
                    ["fields"] = "nextPageToken,files(id,name,createdTime,appProperties,size)",
                    ["pageSize"] = "1000",
                    ["orderBy"] = "createdTime desc",
                    ["pageToken"] = page ?? "",
                }
            );
            using HttpResponseMessage response = await SendAsync(
                    HttpMethod.Get,
                    FilesEndpoint + "?" + query,
                    null,
                    cancellationToken
                )
                .ConfigureAwait(false);
            JsonObject json =
                JsonNode.Parse(
                    await ReadBodyAsync(response.Content, MaximumMetadataResponseBytes, cancellationToken)
                        .ConfigureAwait(false)
                ) as JsonObject
                ?? throw new InvalidDataException("Google Drive returned invalid backup metadata.");
            foreach (JsonObject file in (json["files"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (
                    file["id"]?.GetValue<string>() is { Length: > 0 } id
                    && file["appProperties"]?["deviceId"]?.GetValue<string>() is { Length: > 0 } device
                    && DateTimeOffset.TryParse(
                        file["createdTime"]?.GetValue<string>(),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out DateTimeOffset created
                    )
                )
                {
                    result.Add(
                        new(
                            id,
                            file["name"]?.GetValue<string>() ?? "Backup",
                            device,
                            created,
                            file["appProperties"]?["deviceName"]?.GetValue<string>() ?? "",
                            ReadSize(file["size"])
                        )
                    );
                }
            }
            page = json["nextPageToken"]?.GetValue<string>();
        } while (!string.IsNullOrWhiteSpace(page));
        return result;
    }

    /// <summary>Reads the string-encoded Drive file size; unavailable or invalid sizes remain unknown.</summary>
    private static long? ReadSize(JsonNode? value) =>
        value is JsonValue json
        && json.TryGetValue(out string? text)
        && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long size)
            ? size
            : null;

    /// <summary>Downloads and bounds-checks a backup before parsing it.</summary>
    public async Task<ProfileSnapshot> DownloadAsync(string id, CancellationToken cancellationToken) =>
        ProfileSnapshot.Parse(await DownloadBytesAsync(id, cancellationToken).ConfigureAwait(false));

    /// <summary>Preserves the exact cloud file after size and format validation.</summary>
    public async Task<byte[]> DownloadBytesAsync(string id, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(
                HttpMethod.Get,
                new Uri(WellKnownUris.GoogleDriveFilesDirectory, Uri.EscapeDataString(id)).AbsoluteUri + "?alt=media",
                null,
                cancellationToken
            )
            .ConfigureAwait(false);
        byte[] bytes = await ReadBodyAsync(response.Content, ProfileSnapshot.MaximumBytes, cancellationToken)
            .ConfigureAwait(false);
        _ = ProfileSnapshot.Parse(bytes);
        return bytes;
    }

    /// <summary>Deletes only through the app-data grant; concurrent deletion is harmless.</summary>
    public async Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await SendAsync(
                    HttpMethod.Delete,
                    new Uri(WellKnownUris.GoogleDriveFilesDirectory, Uri.EscapeDataString(id)).AbsoluteUri,
                    null,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // Another linked machine may already have removed this immutable backup.
        }
    }

    /// <summary>Bounds all response bodies while streaming, including unknown content lengths.</summary>
    private static async Task<byte[]> ReadBodyAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken
    )
    {
        if (content.Headers.ContentLength > maximumBytes)
        {
            throw new InvalidDataException("The Google response exceeds the supported size.");
        }
        using Stream input = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        byte[] buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (bytes.Length + count > maximumBytes)
            {
                throw new InvalidDataException("The Google response exceeds the supported size.");
            }
            await bytes.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
        }
        return bytes.ToArray();
    }

    /// <summary>Appends an immutable snapshot and retains ten successful backups per machine.</summary>
    public async Task UploadAsync(ProfileSnapshot snapshot, CancellationToken cancellationToken)
    {
        byte[] bytes = snapshot.Serialize();
        _ = ProfileSnapshot.Parse(bytes);
        string name = $"srvsurvey-profile-v1-{snapshot.DeviceId}-{Guid.NewGuid():N}.json";
        using var metadata = new StringContent(
            JsonSerializer.Serialize(
                new
                {
                    name,
                    parents = AppDataParents,
                    appProperties = new { deviceId = snapshot.DeviceId, deviceName = snapshot.DeviceName },
                }
            ),
            Encoding.UTF8,
            "application/json"
        );
        metadata.Headers.Add("X-Upload-Content-Type", "application/json");
        metadata.Headers.Add("X-Upload-Content-Length", bytes.Length.ToString(CultureInfo.InvariantCulture));
        using HttpResponseMessage session = await SendAsync(
                HttpMethod.Post,
                WellKnownUris.GoogleDriveUpload.AbsoluteUri + "?uploadType=resumable&fields=id",
                metadata,
                cancellationToken
            )
            .ConfigureAwait(false);
        Uri location =
            session.Headers.Location
            ?? throw new InvalidDataException("Google Drive did not return an upload session.");
        if (
            !location.IsAbsoluteUri
            || location.Scheme != Uri.UriSchemeHttps
            || location.Host != WellKnownUris.GoogleDriveUpload.Host
        )
        {
            throw new InvalidDataException("Google Drive returned an invalid upload session.");
        }
        using var payload = new ByteArrayContent(bytes);
        payload.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using HttpResponseMessage response = await SendAsync(
                HttpMethod.Put,
                location.AbsoluteUri,
                payload,
                cancellationToken
            )
            .ConfigureAwait(false);
        IReadOnlyList<DriveBackup> backups = await ListAsync(cancellationToken).ConfigureAwait(false);
        foreach (
            DriveBackup old in backups
                .Where(backup => backup.DeviceId == snapshot.DeviceId)
                .OrderByDescending(backup => backup.CreatedUtc)
                .Skip(10)
        )
        {
            await DeleteAsync(old.Id, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Authorizes each operation without placing tokens in URLs or diagnostic messages.</summary>
    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string url,
        HttpContent? content,
        CancellationToken cancellationToken
    )
    {
        await tokenGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (token is null)
            {
                throw new InvalidOperationException("Link Google Drive before synchronizing.");
            }

            if (token.ExpiresUtc <= DateTimeOffset.UtcNow.AddMinutes(1))
            {
                ProfileSyncPreferences settings = ClientSettings();
                token = await ExchangeAsync(
                        new Dictionary<string, string>
                        {
                            ["client_id"] = settings.ClientId,
                            ["client_secret"] = settings.ClientSecret,
                            ["grant_type"] = "refresh_token",
                            ["refresh_token"] = token.RefreshToken,
                        },
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                SaveToken();
            }
        }
        finally
        {
            tokenGate.Release();
        }
        using var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        HttpResponseMessage response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        HttpStatusCode status = response.StatusCode;
        response.Dispose();
        throw new HttpRequestException($"Google Drive request failed (HTTP {(int)status}).", null, status);
    }

    /// <summary>Exchanges authorization codes or refresh tokens without exposing Google error payloads.</summary>
    private async Task<GoogleDriveToken> ExchangeAsync(
        Dictionary<string, string> values,
        CancellationToken cancellationToken
    )
    {
        using var form = new FormUrlEncodedContent(values.Where(pair => !string.IsNullOrWhiteSpace(pair.Value)));
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint) { Content = form };
        using HttpResponseMessage response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Google authorization failed (HTTP {(int)response.StatusCode}); link Google Drive again if access was revoked."
            );
        }

        JsonObject json =
            JsonNode.Parse(
                await ReadBodyAsync(response.Content, MaximumTokenResponseBytes, cancellationToken)
                    .ConfigureAwait(false)
            ) as JsonObject
            ?? throw new InvalidDataException("Google returned an invalid authorization response.");
        string access = json["access_token"]?.GetValue<string>() ?? "";
        string refresh = json["refresh_token"]?.GetValue<string>() ?? token?.RefreshToken ?? "";
        if (access.Length == 0 || refresh.Length == 0)
        {
            throw new InvalidDataException("Google did not grant renewable access.");
        }

        return new(access, refresh, DateTimeOffset.UtcNow.AddSeconds(json["expires_in"]?.GetValue<int>() ?? 3600));
    }

    /// <summary>Loads Windows-protected tokens or a Linux user-only authorization file.</summary>
    private GoogleDriveToken? ReadToken()
    {
        if (!File.Exists(tokenPath))
        {
            return null;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(tokenPath);
            if (OperatingSystem.IsWindows())
            {
                bytes = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            }

            return JsonSerializer.Deserialize<GoogleDriveToken>(bytes);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Persists tokens on this machine only, using DPAPI or restrictive Unix permissions.</summary>
    private void SaveToken()
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(token);
        if (OperatingSystem.IsWindows())
        {
            bytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        }

        string temporary = tokenPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
            };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (var output = new FileStream(temporary, options))
            {
                output.Write(bytes);
            }

            File.Move(temporary, tokenPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    /// <summary>Uses imported local setup or the publisher's configured OAuth Desktop client.</summary>
    private ProfileSyncPreferences ClientSettings()
    {
        ProfileSyncPreferences preferences = store.LoadPreferences();
        if (!string.IsNullOrWhiteSpace(preferences.ClientId))
        {
            return preferences;
        }

        var metadata = typeof(GoogleDriveBackupClient)
            .Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>()
            .ToDictionary(item => item.Key, item => item.Value);
        return preferences with
        {
            ClientId = metadata.GetValueOrDefault("SrvSurvey.GoogleDrive.ClientId") ?? "",
            ClientSecret = metadata.GetValueOrDefault("SrvSurvey.GoogleDrive.ClientSecret") ?? "",
        };
    }

    /// <summary>Encodes OAuth and Drive query arguments without shell interpolation.</summary>
    private static string FormQuery(Dictionary<string, string> values) =>
        string.Join(
            "&",
            values
                .Where(pair => pair.Value.Length > 0)
                .Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value))
        );

    /// <summary>Encodes random OAuth material using the PKCE URL-safe alphabet.</summary>
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Releases network and token synchronization resources.</summary>
    public void Dispose()
    {
        client.Dispose();
        tokenGate.Dispose();
    }
}

/// <summary>Google authorization is local to this installation and excluded from all backups.</summary>
internal sealed record GoogleDriveToken(string AccessToken, string RefreshToken, DateTimeOffset ExpiresUtc);
