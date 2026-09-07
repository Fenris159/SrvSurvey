using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SrvSurvey.Desktop.Platform;

/// <summary>Optional local Windows SAPI output. One background STA owns the speaker and a bounded speech queue.</summary>
public sealed class MiningSpeechOutput : IDisposable
{
    private readonly BlockingCollection<Action<object>> queue = new(10);
    private readonly Lock sync = new();
    private Thread? worker;
    private volatile bool disposed;
    public bool IsSupported => OperatingSystem.IsWindows();
    public Task<IReadOnlyList<string>> GetVoicesAsync()
    {
        var result = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!Enqueue(speaker =>
        {
            try
            {
                dynamic voices = ((dynamic)speaker).GetVoices();
                var names = new List<string>();
                for (var index = 0; index < voices.Count; index++) names.Add((string)voices.Item(index).GetDescription());
                result.TrySetResult(names);
            }
            catch (Exception ex) { result.TrySetException(ex); }
        })) result.TrySetResult([]);
        return result.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
    public void Speak(string text, string voice, int volume, int rate) => Enqueue(speaker =>
    {
        dynamic sapi = speaker;
        sapi.Volume = Math.Clamp(volume, 0, 100);
        sapi.Rate = Math.Clamp(rate, -10, 10);
        if (!string.IsNullOrEmpty(voice))
        {
            dynamic voices = sapi.GetVoices();
            for (var index = 0; index < voices.Count; index++)
                if ((string)voices.Item(index).GetDescription() == voice) { sapi.Voice = voices.Item(index); break; }
        }
        sapi.Speak(text, 0);
    });
    private bool Enqueue(Action<object> action)
    {
        lock (sync)
        {
            if (disposed || !OperatingSystem.IsWindows()) return false;
            if (worker is null)
            {
                worker = new Thread(() => { if (OperatingSystem.IsWindows()) Run(); }) { IsBackground = true, Name = "Mining announcements" };
                worker.SetApartmentState(ApartmentState.STA);
                worker.Start();
            }
            return queue.TryAdd(action);
        }
    }
    [SupportedOSPlatform("windows")]
    private void Run()
    {
        object? speaker = null;
        try
        {
            var type = Type.GetTypeFromProgID("SAPI.SpVoice");
            if (type is null) return;
            speaker = Activator.CreateInstance(type);
            if (speaker is null) return;
            foreach (var action in queue.GetConsumingEnumerable())
            {
                if (disposed) break;
                try { action(speaker); }
                catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { /* A failed voice must not interrupt journal processing. */ }
            }
        }
        catch (Exception ex) when (ex is COMException or System.Reflection.TargetInvocationException) { /* Voice enumeration reports unavailability through its bounded timeout. */ }
        finally { if (speaker is not null && Marshal.IsComObject(speaker)) Marshal.FinalReleaseComObject(speaker); }
    }
    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            queue.CompleteAdding();
        }
    }
}
