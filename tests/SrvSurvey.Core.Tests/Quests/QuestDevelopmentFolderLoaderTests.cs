using System.Text;
using SrvSurvey.Core.Quests;

namespace SrvSurvey.Core.Tests.Quests;

public sealed class QuestDevelopmentFolderLoaderTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "srv-survey-quest-folder-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LoadsLegacyFolderWithoutChangingSourceBytes()
    {
        Directory.CreateDirectory(temporaryDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "quest.json"),
            """
            {
              "publisher":"Raven",
              "id":"sample",
              "ver":2,
              "title":"Sample Quest",
              "firstChapter":"start",
              "objectives":{"scan":"Scan a body"},
              "strings":{"old":"replaced"},
              "msgs":[{"id":"built-in","from":"Raven","body":"Ready"}],
              "chapters":{"start":"return 'embedded'"},
              "future":{"retain":true}
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "strings.json"),
            """{"scan":"Scan the target"}""");
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "welcome.md"),
            """
            from: Raven Colonial
            subject: Welcome
            action: go: Proceed: now
            tags: ["Sol","Achenar"]

            First line.
            Second line.
            """);
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "start.lua"),
            "return 'file'");
        var originals = Directory.GetFiles(temporaryDirectory)
            .ToDictionary(path => path, File.ReadAllBytes);

        var result = await new QuestDevelopmentFolderLoader().LoadAsync(
            temporaryDirectory);

        Assert.Equal("Sample Quest", result.Definition.Title);
        Assert.Equal("Scan the target", result.Definition.Strings["scan"]);
        Assert.False(result.Definition.Strings.ContainsKey("old"));
        Assert.Equal("return 'file'", result.Definition.Chapters["start"]);
        Assert.True(result.Definition.ExtensionData.ContainsKey("future"));
        var message = result.Definition.Messages.Single(item =>
            item.Id == "welcome");
        Assert.Equal("Raven Colonial", message.From);
        Assert.Equal("Welcome", message.Subject);
        Assert.Equal("Proceed: now", message.Actions!["go"]);
        Assert.Equal(["Achenar", "Sol"], message.Tags!.Order());
        Assert.Equal("First line.\nSecond line.\n", message.Body);
        Assert.Equal(4, result.SourceFiles.Count);
        Assert.All(result.SourceFiles, file => Assert.Equal(64, file.Sha256.Length));
        Assert.Empty(result.Warnings);
        foreach (var pair in originals)
        {
            Assert.Equal(pair.Value, await File.ReadAllBytesAsync(pair.Key));
        }
    }

    [Fact]
    public async Task RejectsUnsafeQuestIdBeforeItCanBecomeAFileName()
    {
        Directory.CreateDirectory(temporaryDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "quest.json"),
            """
            {
              "publisher":"Raven",
              "id":"../escape",
              "ver":1,
              "title":"Unsafe",
              "firstChapter":"start",
              "chapters":{"start":"return true"}
            }
            """);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new QuestDevelopmentFolderLoader().LoadAsync(temporaryDirectory));

        Assert.Contains("safe", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsMissingFirstChapterAndInvalidUtf8WithoutWriting()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var questPath = Path.Combine(temporaryDirectory, "quest.json");
        await File.WriteAllTextAsync(
            questPath,
            """
            {
              "publisher":"Raven",
              "id":"sample",
              "ver":1,
              "title":"Missing",
              "firstChapter":"start",
              "chapters":{}
            }
            """);
        var original = await File.ReadAllBytesAsync(questPath);

        var missing = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new QuestDevelopmentFolderLoader().LoadAsync(temporaryDirectory));
        Assert.Contains("First chapter", missing.Message, StringComparison.Ordinal);
        Assert.Equal(original, await File.ReadAllBytesAsync(questPath));

        await File.WriteAllBytesAsync(questPath, [0xff, 0xfe, 0xfd]);
        var invalid = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new QuestDevelopmentFolderLoader().LoadAsync(temporaryDirectory));
        Assert.Contains("UTF-8", invalid.Message, StringComparison.Ordinal);
        Assert.Equal(
            new byte[] { 0xff, 0xfe, 0xfd },
            await File.ReadAllBytesAsync(questPath));
    }

    [Fact]
    public async Task ReportsDuplicateMessageIdsWithoutDiscardingEitherMessage()
    {
        Directory.CreateDirectory(temporaryDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "quest.json"),
            """
            {
              "publisher":"Raven",
              "id":"sample",
              "ver":1,
              "title":"Duplicate Messages",
              "firstChapter":"start",
              "msgs":[{"id":"welcome","from":"One","body":"One"}],
              "chapters":{"start":"return true"}
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "welcome.md"),
            "from: Two\n\nTwo");

        var result = await new QuestDevelopmentFolderLoader().LoadAsync(
            temporaryDirectory);

        Assert.Equal(2, result.Definition.Messages.Count(message =>
            message.Id == "welcome"));
        Assert.Single(result.Warnings);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
