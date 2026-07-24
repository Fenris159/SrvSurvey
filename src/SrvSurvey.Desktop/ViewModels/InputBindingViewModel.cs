using System.ComponentModel;
using System.Runtime.CompilerServices;
using SrvSurvey.Desktop.Input;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class InputBindingViewModel : INotifyPropertyChanged
{
    private readonly Action<InputBindingViewModel, string> save;
    private string chord;
    private string validationMessage = string.Empty;

    public InputBindingViewModel(
        GlobalInputActionDefinition definition,
        string chord,
        Action<InputBindingViewModel, string> save)
    {
        Definition = definition
            ?? throw new ArgumentNullException(nameof(definition));
        this.save = save
            ?? throw new ArgumentNullException(nameof(save));
        this.chord = chord;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public GlobalInputActionDefinition Definition { get; }

    public string DisplayName => Definition.DisplayName;

    public string Description => Definition.Description;

    public string DefaultChord => string.IsNullOrEmpty(Definition.DefaultChord)
        ? "No default shortcut"
        : $"Default: {Definition.DefaultChord}";

    public string Chord
    {
        get => chord;
        set
        {
            var candidate = value?.Trim() ?? string.Empty;
            if (string.Equals(chord, candidate, StringComparison.Ordinal))
            {
                return;
            }

            var normalized = string.Empty;
            if (candidate.Length > 0
                && !InputChord.TryNormalize(candidate, out normalized))
            {
                chord = candidate;
                ValidationMessage =
                    "Enter one key with ALT, CTRL, or SHIFT, or leave blank.";
                OnPropertyChanged();
                return;
            }

            chord = normalized;
            ValidationMessage = string.Empty;
            OnPropertyChanged();
            save(this, chord);
        }
    }

    public string ValidationMessage
    {
        get => validationMessage;
        private set
        {
            if (string.Equals(
                    validationMessage,
                    value,
                    StringComparison.Ordinal))
            {
                return;
            }

            validationMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasValidationError));
        }
    }

    public bool HasValidationError => ValidationMessage.Length > 0;

    internal void Reset(string value)
    {
        chord = value;
        ValidationMessage = string.Empty;
        OnPropertyChanged(nameof(Chord));
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}
