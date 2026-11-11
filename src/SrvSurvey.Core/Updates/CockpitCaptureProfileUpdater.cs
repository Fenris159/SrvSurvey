using System.Security.Cryptography;
using System.Text;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Updates;

public enum CockpitCaptureProfileRefreshState
{
    Current,
    Updated,
    Unavailable,
    Invalid,
    Incompatible,
    VersionConflict,
    Busy,
}

public sealed record CockpitCaptureProfileRefreshResult(int ProfileVersion, CockpitCaptureProfileRefreshState State);

/// <summary>Refreshes one machine-local declarative profile without changing the bridge installation or player settings.</summary>
public sealed class CockpitCaptureProfileUpdater
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly Func<Uri, CancellationToken, Task<byte[]>> download;
    private readonly Uri source;

    public CockpitCaptureProfileUpdater(
        string cacheDirectory,
        Func<Uri, CancellationToken, Task<byte[]>>? download = null,
        Uri? source = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        CachePath = Path.Combine(Path.GetFullPath(cacheDirectory), "cockpit-camera", "profile.ini");
        this.download =
            download ?? ((uri, token) => PublishedReferenceUpdateService.DownloadCaptureProfileAsync(uri, token));
        this.source = source ?? WellKnownUris.PublishedCockpitCaptureProfile;
        if (!this.source.IsAbsoluteUri || this.source.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Capture profiles require an absolute HTTPS source.", nameof(source));
        }
    }

    public string CachePath { get; }

    /// <summary>Retains the latest compatible cached profile when a download fails, is incompatible, or would downgrade it.</summary>
    public async Task<CockpitCaptureProfileRefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            byte[] bytes = await download(source, deadline.Token).ConfigureAwait(false);
            if (bytes.Length > CockpitCaptureProfile.MaximumBytes)
            {
                return Result(CockpitCaptureProfileRefreshState.Invalid);
            }
            var candidate = CockpitCaptureProfile.Parse(Utf8.GetString(bytes));
            if (candidate is null || !candidate.Validated)
            {
                return Result(CockpitCaptureProfileRefreshState.Invalid);
            }
            return candidate.IsCompatible
                ? Activate(candidate)
                : Result(CockpitCaptureProfileRefreshState.Incompatible);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException or DecoderFallbackException)
        {
            return Result(CockpitCaptureProfileRefreshState.Invalid);
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or OperationCanceledException
                        or IOException
                        or UnauthorizedAccessException
            )
        {
            return Result(CockpitCaptureProfileRefreshState.Unavailable);
        }
    }

    private CockpitCaptureProfileRefreshResult Activate(CockpitCaptureProfile candidate)
    {
        string identity = OperatingSystem.IsWindows() ? CachePath.ToUpperInvariant() : CachePath;
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        using var mutex = new Mutex(false, "SrvSurvey-CockpitProfile-" + hash);
        bool held = false;
        try
        {
            try
            {
                held = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                held = true;
            }
            if (!held)
            {
                return Result(CockpitCaptureProfileRefreshState.Busy);
            }
            // Recheck under the cross-instance lock after downloading: an older response cannot overwrite a newer activation.
            CockpitCaptureProfile current = LoadCurrent();
            if (candidate.ProfileVersion < current.ProfileVersion || candidate.Serialize() == current.Serialize())
            {
                return new(current.ProfileVersion, CockpitCaptureProfileRefreshState.Current);
            }
            if (candidate.ProfileVersion == current.ProfileVersion)
            {
                return new(current.ProfileVersion, CockpitCaptureProfileRefreshState.VersionConflict);
            }
            string directory = Path.GetDirectoryName(CachePath)!;
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, $".profile-{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(temporary, candidate.Serialize(), Utf8);
                // Preserve one predecessor with a compatible packet protocol without moving the active file away from readers.
                File.WriteAllText(CachePath + ".previous", current.Serialize(), Utf8);
                File.Move(temporary, CachePath, overwrite: true);
            }
            finally
            {
                File.Delete(temporary);
            }
            return new(candidate.ProfileVersion, CockpitCaptureProfileRefreshState.Updated);
        }
        finally
        {
            if (held)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    private CockpitCaptureProfileRefreshResult Result(CockpitCaptureProfileRefreshState state) =>
        new(LoadCurrent().ProfileVersion, state);

    private CockpitCaptureProfile LoadCurrent()
    {
        CockpitCaptureProfile bundled = CockpitCaptureProfile.Embedded;
        try
        {
            if (
                new FileInfo(CachePath) is { Exists: true, Length: <= CockpitCaptureProfile.MaximumBytes }
                && CockpitCaptureProfile.Parse(File.ReadAllText(CachePath, Utf8)) is { IsCompatible: true } cached
                && cached.ProfileVersion > bundled.ProfileVersion
            )
            {
                return cached;
            }
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            // The bridge also falls back to its bundled profile when a machine-local cache cannot be read.
        }
        return bundled;
    }
}
