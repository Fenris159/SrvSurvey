using System.IO.Compression;
using System.Text;
using System.Text.Json;
using NetMQ;
using NetMQ.Sockets;
using SrvSurvey.Core.Mining;
using SrvSurvey.Desktop.Runtime;

namespace SrvSurvey.Desktop.Platform;

/// <summary>An opt-in receive-only EDDN subscriber. Its socket and cache writes stay on one background worker.</summary>
public sealed class MiningCommunityListener : IDisposable
{
    private readonly string path;
    private readonly CancellationTokenSource stop = new();
    private Task? worker;
    private volatile bool enabled;
    private bool disposed;
    private volatile string status = "EDDN reception is off.";
    public MiningCommunityCache Cache { get; } = new();
    public string Status => status;
    public MiningCommunityListener(string directory)
    {
        path = Path.Combine(directory, "mining", "community-cache.json");
        try { if (File.Exists(path) && new FileInfo(path).Length <= 64 * 1024 * 1024) Cache.Restore(File.ReadAllText(path), DateTimeOffset.UtcNow); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { status = "Community cache could not be loaded: " + ex.Message; }
    }
    public void SetEnabled(bool value)
    {
        if (disposed) return;
        enabled = value && DesktopExternalEffectPolicy.IsAllowed;
        if (enabled) worker ??= Task.Run(Receive);
        else status = "EDDN reception is off.";
    }
    private void Receive()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                if (!enabled) { stop.Token.WaitHandle.WaitOne(250); continue; }
                try
                {
                    using var socket = new SubscriberSocket();
                    socket.Options.Linger = TimeSpan.Zero;
                    socket.Options.ReceiveHighWatermark = 100;
                    socket.Options.MaxMsgSize = 2 * 1024 * 1024;
                    socket.Connect("tcp://eddn.edcd.io:9500");
                    socket.SubscribeToAnyTopic();
                    var lastSave = DateTimeOffset.UtcNow;
                    status = "Listening for community observations…";
                    while (enabled && !stop.IsCancellationRequested)
                    {
                        if (!socket.TryReceiveFrameBytes(TimeSpan.FromMilliseconds(250), out var bytes)) continue;
                        try
                        {
                            Cache.Apply(Decode(bytes), DateTimeOffset.UtcNow);
                            status = $"EDDN · {Cache.Count:N0} cached commodity observations";
                        }
                        catch (Exception ex) when (ex is InvalidDataException or JsonException or ArgumentException or InvalidOperationException or OverflowException) { /* Ignore malformed broadcasts; never interrupt live mining. */ }
                        if (DateTimeOffset.UtcNow - lastSave > TimeSpan.FromMinutes(1)) { Save(); lastSave = DateTimeOffset.UtcNow; }
                    }
                    Save();
                }
                catch (Exception ex) when (ex is NetMQException or IOException or UnauthorizedAccessException)
                {
                    status = "EDDN unavailable: " + ex.Message;
                    stop.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(30));
                }
            }
        }
        finally { enabled = false; }
    }
    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, Cache.Export());
        File.Move(temporary, path, true);
    }
    internal static string Decode(byte[] bytes)
    {
        if (bytes.Length > 2 * 1024 * 1024) throw new InvalidDataException("Oversized EDDN frame.");
        using var input = new MemoryStream(bytes, false);
        using var compressed = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = compressed.Read(buffer)) > 0)
        {
            if (output.Length + count > 4 * 1024 * 1024) throw new InvalidDataException("Oversized EDDN message.");
            output.Write(buffer, 0, count);
        }
        return Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        enabled = false;
        stop.Cancel();
        if (worker is null) stop.Dispose();
        else _ = worker.ContinueWith(_ => stop.Dispose(), TaskScheduler.Default);
    }
}
