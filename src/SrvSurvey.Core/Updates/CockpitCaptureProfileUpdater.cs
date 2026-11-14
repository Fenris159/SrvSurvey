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
                MaterializeCurrent(current);
                return new(current.ProfileVersion, CockpitCaptureProfileRefreshState.Current);
            }
            if (candidate.ProfileVersion == current.ProfileVersion)
            {
                return new(current.ProfileVersion, CockpitCaptureProfileRefreshState.VersionConflict);
            }
            WriteProfile(candidate, current);
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

    /// <summary>Uses both on-disk candidates to prevent a corrupt active cache from lowering the revision floor.</summary>
    private CockpitCaptureProfile LoadCurrent()
    {
        CockpitCaptureProfile current = CockpitCaptureProfile.Embedded;
        foreach (string path in new[] { CachePath + ".previous", CachePath })
        {
            if (ReadCompatible(path) is { } candidate && candidate.ProfileVersion > current.ProfileVersion)
            {
                current = candidate;
            }
        }
        return current;
    }

    /// <summary>Makes the app's embedded or recovered revision readable by independently installed bridge DLLs.</summary>
    private void MaterializeCurrent(CockpitCaptureProfile current)
    {
        CockpitCaptureProfile? cached = ReadCompatible(CachePath);
        if (cached?.Serialize() == current.Serialize())
        {
            return;
        }
        CockpitCaptureProfile? previous = ReadCompatible(CachePath + ".previous");
        WriteProfile(
            current,
            cached is not null && cached.ProfileVersion > (previous?.ProfileVersion ?? 0) ? cached : null
        );
    }

    /// <summary>Preserves the predecessor before atomically replacing a complete bridge-readable active profile.</summary>
    private void WriteProfile(CockpitCaptureProfile candidate, CockpitCaptureProfile? previous)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
        if (previous is not null)
        {
            WriteAtomic(CachePath + ".previous", previous.Serialize());
        }
        WriteAtomic(CachePath, candidate.Serialize());
    }

    private static void WriteAtomic(string path, string text)
    {
        string temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, text, Utf8);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static CockpitCaptureProfile? ReadCompatible(string path)
    {
        try
        {
            return
                new FileInfo(path) is { Exists: true, Length: <= CockpitCaptureProfile.MaximumBytes }
                && CockpitCaptureProfile.Parse(File.ReadAllText(path, Utf8)) is { IsCompatible: true } cached
                ? cached
                : null;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return null;
        }
    }
}
