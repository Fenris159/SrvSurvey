using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SrvSurvey.Core.Quests;

public sealed class QuestDevelopmentFolderLoader
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The loader is exposed as an injectable service instance.")]
    public async Task<QuestDevelopmentFolderLoadResult> LoadAsync(
        string sourceDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        var root = Path.GetFullPath(sourceDirectory);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(
                $"Quest development folder was not found: {root}");
        }

        var questPath = Path.Combine(root, "quest.json");
        if (!File.Exists(questPath))
        {
            throw new FileNotFoundException(
                "The quest development folder does not contain quest.json.",
                questPath);
        }

        var paths = EnumerateSourcePaths(root, questPath);
        var loaded = new Dictionary<string, LoadedSourceFile>(
            PathComparer);
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectReparsePoint(path);
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken)
                .ConfigureAwait(false);
            loaded.Add(path, new LoadedSourceFile(
                Path.GetRelativePath(root, path),
                bytes,
                Convert.ToHexString(SHA256.HashData(bytes))));
        }

        await VerifySourcesUnchangedAsync(
                root,
                questPath,
                loaded,
                cancellationToken)
            .ConfigureAwait(false);

        var definition = Deserialize<RavenQuestDefinition>(
                loaded[questPath],
                "quest.json")
            ?? throw new InvalidDataException("quest.json contains JSON null.");
        definition = Normalize(definition);
        ValidateDefinitionIdentity(definition);

        var stringsPath = Path.Combine(root, "strings.json");
        if (loaded.TryGetValue(stringsPath, out var stringsFile))
        {
            definition = definition with
            {
                Strings = Deserialize<Dictionary<string, string>>(
                        stringsFile,
                        "strings.json")
                    ?? throw new InvalidDataException(
                        "strings.json contains JSON null."),
            };
        }

        var messages = definition.Messages.ToList();
        foreach (var file in loaded.Values
                     .Where(file => string.Equals(
                         Path.GetExtension(file.RelativePath),
                         ".md",
                         StringComparison.OrdinalIgnoreCase))
                     .OrderBy(file => file.RelativePath, PathComparer))
        {
            messages.Add(ParseMessage(file));
        }

        var chapters = definition.Chapters.ToDictionary(
            StringComparer.Ordinal);
        foreach (var file in loaded.Values
                     .Where(file => string.Equals(
                         Path.GetExtension(file.RelativePath),
                         ".lua",
                         StringComparison.OrdinalIgnoreCase))
                     .OrderBy(file => file.RelativePath, PathComparer))
        {
            var chapterId = Path.GetFileNameWithoutExtension(file.RelativePath);
            chapters[chapterId] = Decode(file, file.RelativePath);
        }

        if (!chapters.ContainsKey(definition.FirstChapter))
        {
            throw new InvalidDataException(
                $"First chapter script not found: {definition.FirstChapter}.lua");
        }

        definition = definition with
        {
            Messages = messages,
            Chapters = chapters,
        };
        var warnings = messages
            .GroupBy(message => message.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group =>
                $"Quest message ID '{group.Key}' is defined more than once.")
            .ToArray();
        var inventory = loaded.Values
            .OrderBy(file => file.RelativePath, PathComparer)
            .Select(file => new QuestDevelopmentSourceFile(
                file.RelativePath,
                file.Bytes.LongLength,
                file.Sha256))
            .ToArray();
        return new QuestDevelopmentFolderLoadResult(
            root,
            definition,
            inventory,
            warnings);
    }

    private static string[] EnumerateSourcePaths(
        string root,
        string questPath)
    {
        var paths = new HashSet<string>(PathComparer)
        {
            questPath,
        };
        var stringsPath = Path.Combine(root, "strings.json");
        if (File.Exists(stringsPath))
        {
            paths.Add(stringsPath);
        }

        foreach (var pattern in new[] { "*.md", "*.lua" })
        {
            foreach (var path in Directory.EnumerateFiles(
                         root,
                         pattern,
                         SearchOption.TopDirectoryOnly))
            {
                var fullPath = Path.GetFullPath(path);
                if (!string.Equals(
                        Path.GetDirectoryName(fullPath),
                        root,
                        PathComparison))
                {
                    throw new InvalidDataException(
                        $"Quest source path escapes its selected folder: {path}");
                }

                paths.Add(fullPath);
            }
        }

        return paths.OrderBy(path => path, PathComparer).ToArray();
    }

    private static async Task VerifySourcesUnchangedAsync(
        string root,
        string questPath,
        IReadOnlyDictionary<string, LoadedSourceFile> loaded,
        CancellationToken cancellationToken)
    {
        var currentPaths = EnumerateSourcePaths(root, questPath);
        if (currentPaths.Length != loaded.Count
            || currentPaths.Any(path => !loaded.ContainsKey(path)))
        {
            throw new IOException(
                "Quest source files changed while the folder was being read.");
        }

        foreach (var pair in loaded)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(pair.Key))
            {
                throw new IOException(
                    $"Quest source changed while it was being read: {pair.Value.RelativePath}");
            }

            var current = await File.ReadAllBytesAsync(pair.Key, cancellationToken)
                .ConfigureAwait(false);
            if (current.LongLength != pair.Value.Bytes.LongLength
                || !CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(current),
                    Convert.FromHexString(pair.Value.Sha256)))
            {
                throw new IOException(
                    $"Quest source changed while it was being read: {pair.Value.RelativePath}");
            }
        }
    }

    private static T? Deserialize<T>(LoadedSourceFile file, string displayName)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(
                Decode(file, displayName),
                JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"{displayName} is not valid quest JSON.",
                exception);
        }
    }

    private static RavenQuestDefinition Normalize(
        RavenQuestDefinition definition)
    {
        return definition with
        {
            Tags = definition.Tags ?? [],
            OnlySquadrons = definition.OnlySquadrons ?? [],
            OnlyCommanders = definition.OnlyCommanders ?? [],
            Objectives = definition.Objectives ?? [],
            Strings = definition.Strings ?? [],
            Messages = definition.Messages ?? [],
            Chapters = definition.Chapters ?? [],
            ExtensionData = definition.ExtensionData ?? [],
        };
    }

    private static void ValidateDefinitionIdentity(
        RavenQuestDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Publisher);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.FirstChapter);
        if (definition.Publisher.Contains('|', StringComparison.Ordinal)
            || definition.Id.Contains('|', StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Quest publisher or ID cannot contain '|' characters.");
        }

        if (definition.Id is "." or ".."
            || definition.Id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || definition.Id.Contains(Path.DirectorySeparatorChar)
            || definition.Id.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidDataException(
                "Quest ID must be safe to use as a local definition filename.");
        }
    }

    private static RavenQuestMessageDefinition ParseMessage(
        LoadedSourceFile file)
    {
        var parsed = ParseMessageFields(file);
        return new RavenQuestMessageDefinition
        {
            Id = Path.GetFileNameWithoutExtension(file.RelativePath),
            From = parsed.From,
            Subject = parsed.Subject,
            Body = parsed.Body.Count == 0
                ? string.Empty
                : string.Join('\n', parsed.Body) + "\n",
            Actions = parsed.Actions.Count == 0 ? null : parsed.Actions,
            Tags = parsed.Tags,
        };
    }

    private sealed record MessageFields(
        string From,
        string? Subject,
        Dictionary<string, string> Actions,
        HashSet<string>? Tags,
        List<string> Body);

    private static MessageFields ParseMessageFields(LoadedSourceFile file)
    {
        var state = new MessageParseState();
        foreach (var line in SplitLines(Decode(file, file.RelativePath)))
        {
            if (TryParseMessageHeader(file, line, state))
            {
                continue;
            }

            if (line.Length == 0 && state.FirstBlankLine)
            {
                state.FirstBlankLine = false;
                continue;
            }

            state.Body.Add(line);
        }

        return new MessageFields(
            state.From,
            state.Subject,
            state.Actions,
            state.Tags,
            state.Body);
    }

    private sealed class MessageParseState
    {
        public string From { get; set; } = string.Empty;

        public string? Subject { get; set; }

        public Dictionary<string, string> Actions { get; } = [];

        public HashSet<string>? Tags { get; set; }

        public List<string> Body { get; } = [];

        public bool FirstBlankLine { get; set; } = true;
    }

    private static bool TryParseMessageHeader(
        LoadedSourceFile file,
        string line,
        MessageParseState state)
    {
        if (line.StartsWith("from:", StringComparison.OrdinalIgnoreCase))
        {
            state.From = line["from:".Length..].Trim();
            return true;
        }

        if (line.StartsWith("subject:", StringComparison.OrdinalIgnoreCase))
        {
            state.Subject = line["subject:".Length..].Trim();
            return true;
        }

        if (line.StartsWith("action:", StringComparison.OrdinalIgnoreCase))
        {
            ParseMessageAction(file, line, state.Actions);
            return true;
        }

        if (line.StartsWith("tags:", StringComparison.OrdinalIgnoreCase))
        {
            state.Tags = ParseMessageTags(file, line);
            return true;
        }

        return false;
    }

    private static HashSet<string>? ParseMessageTags(
        LoadedSourceFile file,
        string line)
    {
        try
        {
            return JsonSerializer.Deserialize<HashSet<string>>(
                line["tags:".Length..],
                JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"{file.RelativePath} contains invalid message tags.",
                exception);
        }
    }

    private static void ParseMessageAction(
        LoadedSourceFile file,
        string line,
        Dictionary<string, string> actions)
    {
        var value = line["action:".Length..];
        var separator = value.IndexOf(':', StringComparison.Ordinal);
        if (separator < 0)
        {
            throw new InvalidDataException(
                $"{file.RelativePath} contains an action without an ID and label.");
        }

        var id = value[..separator].Trim();
        var label = value[(separator + 1)..].Trim();
        if (id.Length == 0 || label.Length == 0)
        {
            throw new InvalidDataException(
                $"{file.RelativePath} contains an action without an ID and label.");
        }

        if (!actions.TryAdd(id, label))
        {
            throw new InvalidDataException(
                $"{file.RelativePath} defines action '{id}' more than once.");
        }
    }

    private static IEnumerable<string> SplitLines(string value)
    {
        using var reader = new StringReader(value);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    private static string Decode(LoadedSourceFile file, string displayName)
    {
        var bytes = file.Bytes.AsSpan();
        var preamble = StrictUtf8.Preamble;
        if (bytes.StartsWith(preamble))
        {
            bytes = bytes[preamble.Length..];
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException(
                $"{displayName} is not valid UTF-8 and was not imported.",
                exception);
        }
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                $"Quest source links are not imported: {Path.GetFileName(path)}");
        }
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private sealed record LoadedSourceFile(
        string RelativePath,
        byte[] Bytes,
        string Sha256);
}

public sealed record QuestDevelopmentFolderLoadResult(
    string SourceDirectory,
    RavenQuestDefinition Definition,
    IReadOnlyList<QuestDevelopmentSourceFile> SourceFiles,
    IReadOnlyList<string> Warnings);

public sealed record QuestDevelopmentSourceFile(
    string RelativePath,
    long Length,
    string Sha256);
