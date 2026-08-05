using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using Avalonia;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Quests;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class JournalInspectorViewModel : INotifyPropertyChanged
{
    private const int MaximumEventCount = 120;
    private readonly AsyncCommand copyCodeCommand;
    private readonly AsyncCommand copyCoordinatesCommand;
    private readonly AsyncCommand replayCommand;
    private readonly Func<JournalEventEnvelope, Task<QuestRuntimeUpdateResult>>?
        replayEvent;
    private Func<string, Task>? clipboardWriter;
    private readonly ObservableCollection<JournalInspectorEventViewModel> events = [];
    private IReadOnlyList<JournalInspectorPropertyViewModel> properties = [];
    private JournalInspectorEventViewModel? selectedEvent;
    private EliteStatus? status;
    private string codeText = string.Empty;
    private string statusMessage = string.Empty;
    private bool replayConfirmed;

    public JournalInspectorViewModel(
        Func<JournalEventEnvelope, Task<QuestRuntimeUpdateResult>>?
            replayEvent = null)
    {
        this.replayEvent = replayEvent;
        copyCodeCommand = new AsyncCommand(CopyCodeAsync, CanCopyCode);
        copyCoordinatesCommand = new AsyncCommand(
            CopyCoordinatesAsync,
            CanCopyCoordinates);
        replayCommand = new AsyncCommand(ReplayAsync, CanReplay);
        CopyCodeCommand = copyCodeCommand;
        CopyCoordinatesCommand = copyCoordinatesCommand;
        ReplayCommand = replayCommand;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand CopyCodeCommand { get; }

    public ICommand CopyCoordinatesCommand { get; }

    public ICommand ReplayCommand { get; }

    public IReadOnlyList<JournalInspectorEventViewModel> Events => events;

    public JournalInspectorEventViewModel? SelectedEvent
    {
        get => selectedEvent;
        set
        {
            if (!SetField(ref selectedEvent, value))
            {
                return;
            }

            BuildProperties();
            ReplayConfirmed = false;
            replayCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(RawJson));
        }
    }

    public IReadOnlyList<JournalInspectorPropertyViewModel> Properties
    {
        get => properties;
        private set => SetField(ref properties, value);
    }

    public string RawJson => SelectedEvent?.JournalEvent.RawJson ?? string.Empty;

    public string CodeText
    {
        get => codeText;
        private set
        {
            if (SetField(ref codeText, value))
            {
                copyCodeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusText => CreateStatusText(status);

    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            if (SetField(ref statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public bool ReplayConfirmed
    {
        get => replayConfirmed;
        set
        {
            if (SetField(ref replayConfirmed, value))
            {
                replayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public void SetClipboardWriter(Func<string, Task>? writer)
    {
        clipboardWriter = writer;
        copyCodeCommand.RaiseCanExecuteChanged();
        copyCoordinatesCommand.RaiseCanExecuteChanged();
    }

    public void ApplyUpdate(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        EliteStatus? latestStatus)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        if (journalEvents.Count > 0)
        {
            var firstRetainedIndex = Math.Max(
                0,
                journalEvents.Count - MaximumEventCount);
            for (var index = firstRetainedIndex;
                 index < journalEvents.Count;
                 index++)
            {
                events.Insert(
                    0,
                    new JournalInspectorEventViewModel(journalEvents[index]));
            }

            while (events.Count > MaximumEventCount)
            {
                events.RemoveAt(events.Count - 1);
            }

            if (SelectedEvent is null || !events.Contains(SelectedEvent))
            {
                SelectedEvent = events.FirstOrDefault();
            }
        }

        if (latestStatus is not null)
        {
            status = latestStatus;
            OnPropertyChanged(nameof(StatusText));
            copyCoordinatesCommand.RaiseCanExecuteChanged();
        }
    }

    private void BuildProperties()
    {
        if (SelectedEvent is null)
        {
            Properties = [];
            CodeText = string.Empty;
            return;
        }

        var rows = new List<JournalInspectorPropertyViewModel>();
        foreach (var property in SelectedEvent.JournalEvent.Payload
                     .EnumerateObject())
        {
            AddPropertyRows(
                rows,
                property.Name,
                AppendLuaPropertyAccess("entry", property.Name),
                property.Value,
                0);
        }

        Properties = rows;
        RegenerateCode();
    }

    private void AddPropertyRows(
        ICollection<JournalInspectorPropertyViewModel> rows,
        string path,
        string luaAccess,
        JsonElement value,
        int depth)
    {
        var selectable = path != "event" && IsLuaScalar(value);
        rows.Add(new JournalInspectorPropertyViewModel(
            path,
            luaAccess,
            FormatJsonValue(value),
            depth,
            selectable,
            value.Clone(),
            RegenerateCode));
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                AddPropertyRows(
                    rows,
                    path + "." + property.Name,
                    AppendLuaPropertyAccess(luaAccess, property.Name),
                    property.Value,
                    depth + 1);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                AddPropertyRows(
                    rows,
                    $"{path}[{index}]",
                    $"{luaAccess}[{index + 1}]",
                    item,
                    depth + 1);
                index++;
            }
        }
    }

    private void RegenerateCode()
    {
        if (SelectedEvent is null)
        {
            CodeText = string.Empty;
            return;
        }

        var clauses = Properties
            .Where(property => property.IsIncluded && property.IsSelectable)
            .Select(property =>
                $"{property.LuaAccess} == {ToLuaLiteral(property.Value)}")
            .ToArray();
        var builder = new StringBuilder();
        builder.Append("function on_")
            .Append(SelectedEvent.EventName)
            .AppendLine("(entry)");
        if (clauses.Length == 0)
        {
            builder.AppendLine("  -- TODO: your code");
        }
        else
        {
            builder.Append("  if ")
                .Append(string.Join(" and ", clauses))
                .AppendLine(" then");
            builder.AppendLine("    -- TODO: your code");
            builder.AppendLine("  end");
        }

        builder.Append("end");
        CodeText = builder.ToString();
    }

    private bool CanCopyCode()
    {
        return clipboardWriter is not null && !string.IsNullOrWhiteSpace(CodeText);
    }

    public async Task CopyCodeAsync()
    {
        if (!CanCopyCode())
        {
            StatusMessage = "The generated handler cannot be copied because the clipboard is unavailable.";
            return;
        }

        await CopyAsync(CodeText, "The generated Lua handler was copied.");
    }

    private bool CanCopyCoordinates()
    {
        return clipboardWriter is not null && status?.HasLatitudeLongitude == true;
    }

    public async Task CopyCoordinatesAsync()
    {
        if (!CanCopyCoordinates() || status is null)
        {
            StatusMessage = "Status.json does not currently contain surface coordinates.";
            return;
        }

        await CopyAsync(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{status.Latitude}, {status.Longitude}"),
            "The current latitude and longitude were copied.");
    }

    private bool CanReplay()
    {
        return replayEvent is not null
            && SelectedEvent is not null
            && ReplayConfirmed;
    }

    public async Task ReplayAsync()
    {
        if (!CanReplay() || replayEvent is null || SelectedEvent is null)
        {
            StatusMessage =
                "Select an event and confirm that replay may change active quest progress.";
            return;
        }

        var selected = SelectedEvent;
        try
        {
            var result = await replayEvent(selected.JournalEvent);
            ReplayConfirmed = false;
            StatusMessage = result.Warnings.Count > 0
                ? string.Join(Environment.NewLine, result.Warnings)
                : $"Replayed {selected.EventName} into the active quest runtime.";
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException
                or ArgumentException
                or HttpRequestException)
        {
            ReplayConfirmed = false;
            StatusMessage = "The journal event was not replayed: "
                + exception.Message;
        }
    }

    private async Task CopyAsync(string text, string successMessage)
    {
        try
        {
            await clipboardWriter!(text);
            StatusMessage = successMessage;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or IOException
                or NotSupportedException
                or System.Runtime.InteropServices.ExternalException
                or UnauthorizedAccessException)
        {
            StatusMessage = "The clipboard could not be updated: "
                + exception.Message;
        }
    }

    private static bool IsLuaScalar(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(
                value.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out _))
        {
            return false;
        }

        return value.ValueKind is JsonValueKind.String or JsonValueKind.Number
            or JsonValueKind.True or JsonValueKind.False;
    }

    private static string FormatJsonValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Object => $"object ({value.EnumerateObject().Count()})",
            JsonValueKind.Array => $"array ({value.GetArrayLength()})",
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Null => "null",
            _ => value.GetRawText(),
        };
    }

    private static string ToLuaLiteral(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.String
            ? ToLuaStringLiteral(value.GetString() ?? string.Empty)
            : value.ValueKind switch
            {
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => value.GetRawText(),
            };
    }

    private static string AppendLuaPropertyAccess(
        string parent,
        string propertyName)
    {
        return IsLuaIdentifier(propertyName)
            ? parent + "." + propertyName
            : parent + "[" + ToLuaStringLiteral(propertyName) + "]";
    }

    private static string ToLuaStringLiteral(string value)
    {
        var builder = new StringBuilder(value.Length + 2).Append('"');
        foreach (var character in value)
        {
            var escaped = character switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => null,
            };
            if (escaped is not null)
            {
                builder.Append(escaped);
            }
            else if (char.IsControl(character))
            {
                builder.Append('\\').Append(
                    ((int)character).ToString("D3", CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.Append('"').ToString();
    }

    private static bool IsLuaIdentifier(string value)
    {
        return value.Length > 0
            && (char.IsLetter(value[0]) || value[0] == '_')
            && value.Skip(1).All(character =>
                char.IsLetterOrDigit(character) || character == '_')
            && !LuaKeywords.Contains(value);
    }

    private static readonly HashSet<string> LuaKeywords = new(
    [
        "and", "break", "do", "else", "elseif", "end", "false", "for",
        "function", "goto", "if", "in", "local", "nil", "not", "or",
        "repeat", "return", "then", "true", "until", "while",
    ],
        StringComparer.Ordinal);

    private static string CreateStatusText(EliteStatus? value)
    {
        if (value is null)
        {
            return "Waiting for Status.json.";
        }

        var lines = new List<string>();
        if (value.Destination is { } destination)
        {
            lines.Add(
                $"Destination: {destination.Name ?? destination.NameLocalised ?? "?"} body:{destination.Body} id64:{destination.System}");
        }

        if (value.Flags != StatusFlags.None)
        {
            lines.Add($"Flags: {value.Flags} ({(uint)value.Flags})");
        }

        if (value.Flags2 != StatusFlags2.None)
        {
            lines.Add($"Flags2: {value.Flags2} ({(uint)value.Flags2})");
        }

        lines.Add(
            $"GuiFocus: {value.GuiFocus}, Pips: {string.Join(", ", value.Pips)}, FireGroup: {value.FireGroup}");
        if (!string.IsNullOrWhiteSpace(value.BodyName))
        {
            lines.Add("BodyName: " + value.BodyName);
        }

        if (value.HasLatitudeLongitude)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"Lat/Long: {value.Latitude}, {value.Longitude}, Heading: {value.NormalizedHeading} deg, Altitude: {value.Altitude}, Temp: {value.Temperature}"));
        }

        if (!string.IsNullOrWhiteSpace(value.SelectedWeapon))
        {
            lines.Add(
                $"SelectedWeapon: {value.SelectedWeapon} / {value.SelectedWeaponLocalised}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class AsyncCommand(
        Func<Task> execute,
        Func<bool> canExecute) : ICommand
    {
        private bool running;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => !running && canExecute();

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter))
            {
                return;
            }

            running = true;
            RaiseCanExecuteChanged();
            try
            {
                await execute();
            }
            finally
            {
                running = false;
                RaiseCanExecuteChanged();
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

public sealed class JournalInspectorEventViewModel
{
    public JournalInspectorEventViewModel(JournalEventEnvelope journalEvent)
    {
        JournalEvent = journalEvent;
    }

    public JournalEventEnvelope JournalEvent { get; }

    public string EventName => JournalEvent.EventName;

    public string Timestamp => JournalEvent.Timestamp?.ToLocalTime()
        .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
        ?? "No timestamp";
}

public sealed class JournalInspectorPropertyViewModel : INotifyPropertyChanged
{
    private readonly Action changed;
    private bool isIncluded;

    public JournalInspectorPropertyViewModel(
        string path,
        string luaAccess,
        string displayValue,
        int depth,
        bool isSelectable,
        JsonElement value,
        Action changed)
    {
        Path = path;
        LuaAccess = luaAccess;
        DisplayValue = displayValue;
        Depth = depth;
        IsSelectable = isSelectable;
        Value = value;
        this.changed = changed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Path { get; }

    public string LuaAccess { get; }

    public string DisplayValue { get; }

    public int Depth { get; }

    public Thickness Indent => new(Depth * 16, 0, 0, 0);

    public bool IsSelectable { get; }

    public JsonElement Value { get; }

    public bool IsIncluded
    {
        get => isIncluded;
        set
        {
            if (isIncluded == value || !IsSelectable)
            {
                return;
            }

            isIncluded = value;
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(IsIncluded)));
            changed();
        }
    }
}
