using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Controls;

public sealed partial class SystemNameEntry : UserControl
{
    private static readonly ISystemNameSuggestionClient DefaultClient =
        new FallbackSystemNameSuggestionClient(
            new EdsmSystemNameSuggestionClient(),
            new ArdentSystemNameSuggestionClient());

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<SystemNameEntry, string?>(
            nameof(Text),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> PlaceholderTextProperty =
        AvaloniaProperty.Register<SystemNameEntry, string?>(
            nameof(PlaceholderText));

    public static readonly StyledProperty<long?> SelectedSystemAddressProperty =
        AvaloniaProperty.Register<SystemNameEntry, long?>(
            nameof(SelectedSystemAddress),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly DirectProperty<SystemNameEntry,
        IReadOnlyList<SystemNameSuggestion>> SuggestionsProperty =
        AvaloniaProperty.RegisterDirect<SystemNameEntry,
            IReadOnlyList<SystemNameSuggestion>>(
            nameof(Suggestions),
            control => control.Suggestions);

    public static readonly DirectProperty<SystemNameEntry, int> SelectedIndexProperty =
        AvaloniaProperty.RegisterDirect<SystemNameEntry, int>(
            nameof(SelectedIndex),
            control => control.SelectedIndex,
            (control, value) => control.SelectedIndex = value,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly DirectProperty<SystemNameEntry, string> StatusProperty =
        AvaloniaProperty.RegisterDirect<SystemNameEntry, string>(
            nameof(Status),
            control => control.Status);

    private readonly ISystemNameSuggestionClient suggestionClient;
    private readonly TimeSpan suggestionDelay;
    private IReadOnlyList<SystemNameSuggestion> suggestions = [];
    private int selectedIndex = -1;
    private string status = string.Empty;
    private string? selectedSystemName;
    private CancellationTokenSource? suggestionCancellation;

    static SystemNameEntry()
    {
        TextProperty.Changed.AddClassHandler<SystemNameEntry>(
            (control, eventArgs) =>
                control.OnTextChanged(eventArgs.NewValue as string));
    }

    public SystemNameEntry()
        : this(DefaultClient, TimeSpan.FromMilliseconds(450))
    {
    }

    internal SystemNameEntry(
        ISystemNameSuggestionClient suggestionClient,
        TimeSpan suggestionDelay)
    {
        ArgumentNullException.ThrowIfNull(suggestionClient);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            suggestionDelay,
            TimeSpan.Zero);
        this.suggestionClient = suggestionClient;
        this.suggestionDelay = suggestionDelay;
        InitializeComponent();
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string? PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    public long? SelectedSystemAddress
    {
        get => GetValue(SelectedSystemAddressProperty);
        set => SetValue(SelectedSystemAddressProperty, value);
    }

    public IReadOnlyList<SystemNameSuggestion> Suggestions
    {
        get => suggestions;
        private set
        {
            var previouslyHadSuggestions = HasSuggestions;
            SetAndRaise(SuggestionsProperty, ref suggestions, value);
            RaisePropertyChanged(
                HasSuggestionsProperty,
                previouslyHadSuggestions,
                HasSuggestions);
        }
    }

    public static readonly DirectProperty<SystemNameEntry, bool> HasSuggestionsProperty =
        AvaloniaProperty.RegisterDirect<SystemNameEntry, bool>(
            nameof(HasSuggestions),
            control => control.HasSuggestions);

    public bool HasSuggestions => Suggestions.Count > 0;

    public int SelectedIndex
    {
        get => selectedIndex;
        set => SetAndRaise(SelectedIndexProperty, ref selectedIndex, value);
    }

    public string Status
    {
        get => status;
        private set
        {
            var previouslyHadStatus = HasStatus;
            SetAndRaise(StatusProperty, ref status, value);
            RaisePropertyChanged(
                HasStatusProperty,
                previouslyHadStatus,
                HasStatus);
        }
    }

    public static readonly DirectProperty<SystemNameEntry, bool> HasStatusProperty =
        AvaloniaProperty.RegisterDirect<SystemNameEntry, bool>(
            nameof(HasStatus),
            control => control.HasStatus);

    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);

    protected override void OnDetachedFromVisualTree(
        VisualTreeAttachmentEventArgs eventArgs)
    {
        CancelSuggestions();
        base.OnDetachedFromVisualTree(eventArgs);
    }

    private void OnTextChanged(string? value)
    {
        var query = value?.Trim() ?? string.Empty;
        if (!string.Equals(
                query,
                selectedSystemName,
                StringComparison.OrdinalIgnoreCase))
        {
            selectedSystemName = null;
            SelectedSystemAddress = null;
        }

        ScheduleSuggestions(query);
    }

    private void ScheduleSuggestions(string query)
    {
        CancelSuggestions();
        if (query.Length < 3
            || string.Equals(
                query,
                selectedSystemName,
                StringComparison.OrdinalIgnoreCase))
        {
            Suggestions = [];
            SelectedIndex = -1;
            Status = string.Empty;
            return;
        }

        var cancellation = new CancellationTokenSource();
        suggestionCancellation = cancellation;
        _ = LoadSuggestionsAsync(query, cancellation);
    }

    private async Task LoadSuggestionsAsync(
        string query,
        CancellationTokenSource cancellation)
    {
        try
        {
            Status = "Searching for system suggestions…";
            await Task.Delay(suggestionDelay, cancellation.Token);
            var results = await suggestionClient.SearchAsync(
                query,
                cancellation.Token);
            if (!ReferenceEquals(suggestionCancellation, cancellation)
                || !string.Equals(Text?.Trim(), query, StringComparison.Ordinal))
            {
                return;
            }

            Suggestions = results;
            SelectedIndex = results.Count > 0 ? 0 : -1;
            Status = results.Count > 0
                ? $"{results.Count:N0} suggestion"
                    + (results.Count == 1 ? string.Empty : "s")
                    + $" from {results[0].Source}."
                : "No matching systems found.";
        }
        catch (OperationCanceledException)
        {
            // A newer query superseded this one.
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or InvalidDataException
                or JsonException)
        {
            if (ReferenceEquals(suggestionCancellation, cancellation))
            {
                Suggestions = [];
                SelectedIndex = -1;
                Status = "Suggestions are unavailable; manual entry still works.";
            }
        }
        finally
        {
            if (ReferenceEquals(suggestionCancellation, cancellation))
            {
                suggestionCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void InputBox_KeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Down && HasSuggestions)
        {
            SelectedIndex = Math.Min(SelectedIndex + 1, Suggestions.Count - 1);
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.Up && HasSuggestions)
        {
            SelectedIndex = Math.Max(SelectedIndex - 1, 0);
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.Enter && SelectCurrentSuggestion())
        {
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.Escape && HasSuggestions)
        {
            DismissSuggestions();
            eventArgs.Handled = true;
        }
    }

    private void Suggestion_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { DataContext: SystemNameSuggestion suggestion })
        {
            SelectSuggestion(suggestion);
        }
    }

    internal bool SelectCurrentSuggestion()
    {
        return SelectedIndex >= 0
            && SelectedIndex < Suggestions.Count
            && SelectSuggestion(Suggestions[SelectedIndex]);
    }

    private bool SelectSuggestion(SystemNameSuggestion suggestion)
    {
        if (suggestion.SystemAddress <= 0
            || string.IsNullOrWhiteSpace(suggestion.Name))
        {
            return false;
        }

        CancelSuggestions();
        selectedSystemName = suggestion.Name.Trim();
        SelectedSystemAddress = suggestion.SystemAddress;
        Text = selectedSystemName;
        Suggestions = [];
        SelectedIndex = -1;
        Status = $"Selected {selectedSystemName} · "
            + SystemAddressFormatter.Format(suggestion.SystemAddress)
            + ".";
        return true;
    }

    private void DismissSuggestions()
    {
        CancelSuggestions();
        Suggestions = [];
        SelectedIndex = -1;
        Status = string.Empty;
    }

    private void CancelSuggestions()
    {
        var cancellation = suggestionCancellation;
        suggestionCancellation = null;
        cancellation?.Cancel();
    }
}
