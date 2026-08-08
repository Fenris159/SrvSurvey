using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace SrvSurvey.Core.Updates;

public sealed record ReleaseInstallationHandoffPlan(
    string PlanPath,
    string HelperReadyMarkerPath,
    string HealthMarkerPath,
    string OutcomePath,
    DateTimeOffset CreatedAtUtc,
    int ParentProcessId,
    long ParentProcessStartTimeUtcTicks,
    string HealthToken,
    ReleaseInstallationPreparation Preparation);

public enum ReleaseInstallationOutcomeStatus
{
    Installed,
    RolledBack,
    Aborted,
}

public sealed record ReleaseInstallationOutcome(
    ReleaseInstallationOutcomeStatus Status,
    Guid RequestId,
    ReleaseVersion Version,
    DateTimeOffset CompletedAtUtc,
    string? BackupDirectory,
    string? FailedDirectory,
    string? Error);

public sealed class ReleaseInstallationPlanStore
{
    private const int SchemaVersion = 2;
    private const int MaximumPlanBytes = 256 * 1024;
    private const int MaximumArgumentCount = 128;
    private const int MaximumArgumentLength = 4_096;
    private static readonly TimeSpan MaximumPlanAge = TimeSpan.FromHours(2);
    private readonly TimeProvider timeProvider;

    public ReleaseInstallationPlanStore(TimeProvider? timeProvider = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ReleaseInstallationHandoffPlan> CreateAsync(
        string dataDirectory,
        ReleaseInstallationPreparation preparation,
        int parentProcessId,
        DateTimeOffset parentProcessStartTimeUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(preparation);
        if (parentProcessId <= 0
            || parentProcessStartTimeUtc == default
            || preparation.StartupArguments.Count > MaximumArgumentCount
            || preparation.StartupArguments.Any(argument =>
                argument is null || argument.Length > MaximumArgumentLength))
        {
            throw new InvalidDataException(
                "The update handoff process metadata is invalid.");
        }

        var paths = ResolvePaths(dataDirectory, preparation.RequestId);
        if (Directory.Exists(paths.PlanDirectory)
            || File.Exists(paths.PlanDirectory))
        {
            throw new IOException(
                "The update handoff directory already exists.");
        }

        Directory.CreateDirectory(paths.PlanDirectory);
        var healthToken = Convert.ToHexString(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .ToLowerInvariant();
        var createdAt = timeProvider.GetUtcNow();
        var document = new
        {
            schemaVersion = SchemaVersion,
            requestId = preparation.RequestId,
            createdAtUtc = createdAt,
            parentProcessId,
            parentProcessStartTimeUtcTicks = parentProcessStartTimeUtc.UtcTicks,
            healthToken,
            preparation = new
            {
                version = preparation.Version.ToString(),
                preparation.RuntimeIdentifier,
                preparation.InstallationDirectory,
                preparation.ReadyDirectory,
                preparation.CandidateDirectory,
                preparation.BackupDirectory,
                preparation.FailedDirectory,
                preparation.EntryPoint,
                preparation.ManifestSha256,
                preparation.InstallationFingerprint,
                preparation.RequiresElevation,
                startupArguments = preparation.StartupArguments,
            },
        };
        try
        {
            await WriteJsonAtomicallyAsync(
                    paths.PlanPath,
                    document,
                    overwrite: false,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            TryDeleteDirectory(paths.PlanDirectory);
            throw;
        }

        return new ReleaseInstallationHandoffPlan(
            paths.PlanPath,
            paths.HelperReadyMarkerPath,
            paths.HealthMarkerPath,
            paths.OutcomePath,
            createdAt,
            parentProcessId,
            parentProcessStartTimeUtc.UtcTicks,
            healthToken,
            preparation);
    }

    public async Task<ReleaseInstallationHandoffPlan> LoadAsync(
        string dataDirectory,
        string planPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(planPath);
        var fullPlanPath = Path.GetFullPath(planPath);
        var info = new FileInfo(fullPlanPath);
        if (!info.Exists || info.Length is <= 0 or > MaximumPlanBytes)
        {
            throw new InvalidDataException(
                "The update handoff plan is missing or outside the supported size.");
        }

        var bytes = await File.ReadAllBytesAsync(fullPlanPath, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || ReadInt32(root, "schemaVersion") != SchemaVersion)
            {
                throw new InvalidDataException(
                    "The update handoff plan schema is incompatible.");
            }

            var requestId = ReadGuid(root, "requestId");
            var paths = ResolvePaths(dataDirectory, requestId);
            if (!PathsEqual(fullPlanPath, paths.PlanPath))
            {
                throw new InvalidDataException(
                    "The update handoff plan escaped its app-data request directory.");
            }

            var createdAt = ReadDateTimeOffset(root, "createdAtUtc");
            var age = timeProvider.GetUtcNow() - createdAt;
            if (age < TimeSpan.FromMinutes(-5) || age > MaximumPlanAge)
            {
                throw new InvalidDataException(
                    "The update handoff plan is expired or has a future timestamp.");
            }

            var parentProcessId = ReadInt32(root, "parentProcessId");
            var parentStartTicks = ReadInt64(
                root,
                "parentProcessStartTimeUtcTicks");
            var healthToken = ReadHex(root, "healthToken");
            if (parentProcessId <= 0 || parentStartTicks <= 0)
            {
                throw new InvalidDataException(
                    "The update handoff parent process is invalid.");
            }

            if (!root.TryGetProperty("preparation", out var preparationElement)
                || preparationElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    "The update handoff plan has no installation preparation.");
            }

            var versionText = ReadString(preparationElement, "version");
            if (!ReleaseVersion.TryParse(versionText, out var version)
                || version.Build < 0)
            {
                throw new InvalidDataException(
                    "The update handoff version is invalid.");
            }

            var arguments = ReadArguments(preparationElement);
            var preparation = new ReleaseInstallationPreparation(
                requestId,
                version,
                ReadString(preparationElement, "RuntimeIdentifier"),
                ReadString(preparationElement, "InstallationDirectory"),
                ReadString(preparationElement, "ReadyDirectory"),
                ReadString(preparationElement, "CandidateDirectory"),
                ReadString(preparationElement, "BackupDirectory"),
                ReadString(preparationElement, "FailedDirectory"),
                ReadString(preparationElement, "EntryPoint"),
                ReadHex(preparationElement, "ManifestSha256"),
                ReadHex(preparationElement, "InstallationFingerprint"),
                ReadBoolean(preparationElement, "RequiresElevation"),
                arguments);
            return new ReleaseInstallationHandoffPlan(
                paths.PlanPath,
                paths.HelperReadyMarkerPath,
                paths.HealthMarkerPath,
                paths.OutcomePath,
                createdAt,
                parentProcessId,
                parentStartTicks,
                healthToken,
                preparation);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The update handoff plan is not valid JSON.",
                exception);
        }
    }

    public async Task WriteHealthMarkerAsync(
        ReleaseInstallationHandoffPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        await WriteJsonAtomicallyAsync(
                plan.HealthMarkerPath,
                new
                {
                    schemaVersion = SchemaVersion,
                    requestId = plan.Preparation.RequestId,
                    version = plan.Preparation.Version.ToString(),
                    healthToken = plan.HealthToken,
                    confirmedAtUtc = timeProvider.GetUtcNow(),
                },
                overwrite: false,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task WriteHelperReadyMarkerAsync(
        ReleaseInstallationHandoffPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        await WriteJsonAtomicallyAsync(
                plan.HelperReadyMarkerPath,
                new
                {
                    schemaVersion = SchemaVersion,
                    requestId = plan.Preparation.RequestId,
                    healthToken = plan.HealthToken,
                },
                overwrite: false,
                cancellationToken)
            .ConfigureAwait(false);
    }

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The instance API represents one installation-plan store contract.")]
    [SuppressMessage(
        "Maintainability",
        "S2325:Make methods and properties static",
        Justification = "The instance API represents one installation-plan store contract.")]
    public Task<bool> IsHelperReadyAsync(
        ReleaseInstallationHandoffPlan plan,
        CancellationToken cancellationToken = default) =>
        IsMatchingMarkerAsync(
            plan.HelperReadyMarkerPath,
            plan,
            requireVersion: false,
            cancellationToken);

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The instance API represents one installation-plan store contract.")]
    [SuppressMessage(
        "Maintainability",
        "S2325:Make methods and properties static",
        Justification = "The instance API represents one installation-plan store contract.")]
    public async Task<bool> IsHealthConfirmedAsync(
        ReleaseInstallationHandoffPlan plan,
        CancellationToken cancellationToken = default) =>
        await IsMatchingMarkerAsync(
                plan.HealthMarkerPath,
                plan,
                requireVersion: true,
                cancellationToken)
            .ConfigureAwait(false);

    private static async Task<bool> IsMatchingMarkerAsync(
        string markerPath,
        ReleaseInstallationHandoffPlan plan,
        bool requireVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var info = new FileInfo(markerPath);
        if (!info.Exists || info.Length is <= 0 or > MaximumPlanBytes)
        {
            return false;
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(markerPath, cancellationToken)
                .ConfigureAwait(false);
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && ReadInt32(root, "schemaVersion") == SchemaVersion
                && ReadGuid(root, "requestId") == plan.Preparation.RequestId
                && (!requireVersion || string.Equals(
                    ReadString(root, "version"),
                    plan.Preparation.Version.ToString(),
                    StringComparison.Ordinal))
                && string.Equals(
                    ReadHex(root, "healthToken"),
                    plan.HealthToken,
                    StringComparison.Ordinal);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or JsonException)
        {
            return false;
        }
    }

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The instance API represents one installation-plan store contract.")]
    public async Task WriteOutcomeAsync(
        ReleaseInstallationHandoffPlan plan,
        ReleaseInstallationOutcome outcome,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.RequestId != plan.Preparation.RequestId
            || outcome.Version != plan.Preparation.Version)
        {
            throw new InvalidDataException(
                "The update outcome does not match its handoff plan.");
        }

        await WriteJsonAtomicallyAsync(
                plan.OutcomePath,
                new
                {
                    schemaVersion = SchemaVersion,
                    status = outcome.Status.ToString(),
                    outcome.RequestId,
                    version = outcome.Version.ToString(),
                    outcome.CompletedAtUtc,
                    outcome.BackupDirectory,
                    outcome.FailedDirectory,
                    outcome.Error,
                },
                overwrite: true,
                cancellationToken)
            .ConfigureAwait(false);
    }

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The instance API represents one installation-plan store contract.")]
    [SuppressMessage(
        "Maintainability",
        "S2325:Make methods and properties static",
        Justification = "The instance API represents one installation-plan store contract.")]
    public async Task<ReleaseInstallationOutcome> ReadOutcomeAsync(
        ReleaseInstallationHandoffPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var info = new FileInfo(plan.OutcomePath);
        if (!info.Exists || info.Length is <= 0 or > MaximumPlanBytes)
        {
            throw new InvalidDataException(
                "The update outcome is missing or outside the supported size.");
        }

        var bytes = await File.ReadAllBytesAsync(
                plan.OutcomePath,
                cancellationToken)
            .ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || ReadInt32(root, "schemaVersion") != SchemaVersion
                || !Enum.TryParse<ReleaseInstallationOutcomeStatus>(
                    ReadString(root, "status"),
                    ignoreCase: false,
                    out var status))
            {
                throw new InvalidDataException(
                    "The update outcome schema or status is invalid.");
            }

            var requestId = ReadGuid(root, "RequestId");
            var versionText = ReadString(root, "version");
            if (!ReleaseVersion.TryParse(versionText, out var version)
                || requestId != plan.Preparation.RequestId
                || version != plan.Preparation.Version)
            {
                throw new InvalidDataException(
                    "The update outcome does not match its handoff plan.");
            }

            var backupDirectory = ReadOptionalString(root, "BackupDirectory");
            var failedDirectory = ReadOptionalString(root, "FailedDirectory");
            if ((backupDirectory is not null
                    && !PathsEqual(
                        backupDirectory,
                        plan.Preparation.BackupDirectory))
                || (failedDirectory is not null
                    && !PathsEqual(
                        failedDirectory,
                        plan.Preparation.FailedDirectory)))
            {
                throw new InvalidDataException(
                    "The update outcome contains an unexpected recovery path.");
            }

            return new ReleaseInstallationOutcome(
                status,
                requestId,
                version,
                ReadDateTimeOffset(root, "CompletedAtUtc"),
                backupDirectory,
                failedDirectory,
                ReadOptionalString(root, "Error"));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The update outcome is not valid JSON.",
                exception);
        }
    }

    private static async Task WriteJsonAtomicallyAsync<T>(
        string path,
        T value,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidDataException(
                "The update metadata path has no directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        value,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static PlanPaths ResolvePaths(string dataDirectory, Guid requestId)
    {
        if (requestId == Guid.Empty)
        {
            throw new InvalidDataException(
                "The update request identifier is empty.");
        }

        var dataRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(dataDirectory));
        var planDirectory = Path.GetFullPath(Path.Combine(
            dataRoot,
            "updates",
            "install-plans",
            requestId.ToString("N")));
        var prefix = dataRoot + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!planDirectory.StartsWith(prefix, comparison))
        {
            throw new InvalidDataException(
                "The update plan directory escaped application data.");
        }

        return new PlanPaths(
            planDirectory,
            Path.Combine(planDirectory, "plan.json"),
            Path.Combine(planDirectory, "helper-ready.json"),
            Path.Combine(planDirectory, "health.json"),
            Path.Combine(planDirectory, "outcome.json"));
    }

    private static List<string> ReadArguments(JsonElement preparation)
    {
        if (!preparation.TryGetProperty("startupArguments", out var arguments)
            || arguments.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "The update handoff startup arguments are invalid.");
        }

        var values = new List<string>();
        foreach (var element in arguments.EnumerateArray())
        {
            if (values.Count >= MaximumArgumentCount
                || element.ValueKind != JsonValueKind.String
                || element.GetString() is not { } value
                || value.Length > MaximumArgumentLength)
            {
                throw new InvalidDataException(
                    "The update handoff startup arguments exceed their bounds.");
            }

            values.Add(value);
        }

        return values;
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }

    private static string ReadHex(JsonElement element, string propertyName)
    {
        var value = ReadString(element, propertyName);
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException(
                $"The update handoff '{propertyName}' value is not a SHA-256 token.");
        }

        return value.ToLowerInvariant();
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || property.GetString() is not { } value
            || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException(
                $"The update handoff '{propertyName}' value is invalid.");
        }

        return value;
    }

    private static string? ReadOptionalString(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                $"The update handoff '{propertyName}' value is invalid.");
        }

        return property.GetString();
    }

    private static int ReadInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || !property.TryGetInt32(out var value))
        {
            throw new InvalidDataException(
                $"The update handoff '{propertyName}' value is invalid.");
        }

        return value;
    }

    private static long ReadInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || !property.TryGetInt64(out var value))
        {
            throw new InvalidDataException(
                $"The update handoff '{propertyName}' value is invalid.");
        }

        return value;
    }

    private static bool ReadBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException(
                $"The update handoff '{propertyName}' value is invalid.");
        }

        return property.GetBoolean();
    }

    private static Guid ReadGuid(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || !Guid.TryParse(property.GetString(), out var value)
            || value == Guid.Empty)
        {
            throw new InvalidDataException(
                $"The update handoff '{propertyName}' value is invalid.");
        }

        return value;
    }

    private static DateTimeOffset ReadDateTimeOffset(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || !property.TryGetDateTimeOffset(out var value))
        {
            throw new InvalidDataException(
                $"The update handoff '{propertyName}' value is invalid.");
        }

        return value;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Cleanup is best effort; a later run can remove the stale plan.
        }
    }

    private sealed record PlanPaths(
        string PlanDirectory,
        string PlanPath,
        string HelperReadyMarkerPath,
        string HealthMarkerPath,
        string OutcomePath);
}
