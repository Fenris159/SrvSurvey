using System.ComponentModel;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class MiningDetectionViewModel(SurfaceMiningSettingsStore? store) : INotifyPropertyChanged
{
    private MiningDetectionSettings saved = store?.LoadDetection() ?? new();
    private MiningDetectionSettings? draft;
    private MiningBarConfirmation confirmation = new();
    public event PropertyChangedEventHandler? PropertyChanged;
    public MiningDetectionSettings Settings => draft ?? saved;
    public bool IsCalibrating { get; set; }
    public bool IsCalibrationTesting { get; set; }
    public bool ReferenceRequested { get; private set; }
    public bool HasCalibrationChanges => draft is not null && !draft.HasSameCalibration(saved);
    public bool Enabled
    {
        get => saved.Enabled;
        set
        {
            if (value == saved.Enabled) return;
            try
            {
                var next = saved with { Enabled = value };
                store?.SaveDetection(next);
                saved = next;
                if (draft is not null) draft = draft with { Enabled = value };
                Reset();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                StatusText = "Detection settings could not be saved: " + e.Message;
            }
            Notify();
        }
    }

    public string StatusText { get; private set; } = "Detection only — rig locations are never changed.";
    public string SlotsText { get; private set; } = "1 ?   2 ?   3 ?   4 ?   5 ?   6 ?";
    public string LastAppearance { get; private set; } = "No new bar appearance observed.";
    public MiningBarAnalysis Latest { get; private set; } = MiningBarAnalysis.Unknown();
    public void BeginEdit() { draft = saved; Reset(); }
    public void UpdateCalibration(MiningDetectionSettings value)
    {
        value = value.Normalize();
        if (value.HasSameCalibration(Settings)) return;
        if ((value with { MotionMargin = Settings.MotionMargin, BarGap = Settings.BarGap }).HasSameCalibration(Settings))
        {
            draft = value;
            Reset();
            return;
        }
        draft = value with { LabelTemplates = null };
        IsCalibrationTesting = false;
        ReferenceRequested = false;
        Reset();
    }
    public void RequestReference()
    {
        if (!IsCalibrating) return;
        ReferenceRequested = true;
        IsCalibrationTesting = true;
        Pause("Learning the six HUD labels. Keep the Rhino stationary and look forward.");
    }
    public void StopCalibrationTest()
    {
        if (!IsCalibrationTesting && !ReferenceRequested) return;
        IsCalibrationTesting = false;
        ReferenceRequested = false;
        Reset();
    }
    public void ApplyReference(byte[][] labels)
    {
        if (!IsCalibrating || !IsCalibrationTesting || !ReferenceRequested) return;
        draft = (Settings with { LabelTemplates = labels }).Normalize();
        ReferenceRequested = false;
        Reset();
    }
    public void SaveEdit()
    {
        if (draft is null || draft.HasSameCalibration(saved)) return;
        store?.SaveDetection(draft);
        saved = draft;
    }
    public void EndEdit()
    {
        draft = null;
        IsCalibrating = false;
        IsCalibrationTesting = false;
        ReferenceRequested = false;
        Reset();
    }
    public void Pause(string reason)
    {
        confirmation = new();
        Latest = MiningBarAnalysis.Unknown();
        SlotsText = "1 ?   2 ?   3 ?   4 ?   5 ?   6 ?";
        StatusText = reason;
        Notify();
    }
    public void Apply(MiningBarAnalysis analysis)
    {
        Latest = analysis;
        var appeared = confirmation.Apply(analysis);
        SlotsText = string.Join("   ", analysis.Slots.Select((state, i) =>
            $"{i + 1} {(state == MiningBarState.Unknown ? "?" : state != confirmation.States[i] ? "…" : confirmation.States[i] switch
            { MiningBarState.Present => "BAR", MiningBarState.Absent => "empty", _ => "…" })}"));
        StatusText = analysis.Slots.All(s => s == MiningBarState.Unknown)
            ? "HUD not located — adjust alignment or return to the cockpit view."
            : "Detection only — rig locations are never changed.";
        if (appeared.Length > 0)
            LastAppearance = $"Bar appeared: {string.Join(", ", appeared)} at {DateTime.Now:HH:mm:ss}";
        if (confirmation.Disappeared.Count > 0)
            LastAppearance = $"Bar disappeared: {string.Join(", ", confirmation.Disappeared)} at {DateTime.Now:HH:mm:ss}";
        Notify();
    }
    private void Reset() => Pause(Enabled || IsCalibrating
        ? "Waiting for a clear view of the six HUD circles." : "Rig bar detection is off.");
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
