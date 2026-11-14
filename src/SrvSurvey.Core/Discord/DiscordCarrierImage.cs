namespace SrvSurvey.Core.Discord;

/// <summary>Reads a bounded PNG or JPEG for both the local preview and Discord attachment.</summary>
public sealed record DiscordCarrierImage(byte[] Bytes, string FileName, string MediaType)
{
    public const int MaximumBytes = 8 * 1024 * 1024;

    public static async Task<DiscordCarrierImage?> LoadAsync(
        string? path,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        if (stream.Length is < 8 or > MaximumBytes)
        {
            throw new InvalidDataException("Choose a PNG or JPEG image up to 8 MB.");
        }
        byte[] bytes = new byte[stream.Length];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        if (bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            return new(bytes, "carrier.png", "image/png");
        }
        if (bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255)
        {
            return new(bytes, "carrier.jpg", "image/jpeg");
        }
        throw new InvalidDataException("Choose a PNG or JPEG image up to 8 MB.");
    }
}
