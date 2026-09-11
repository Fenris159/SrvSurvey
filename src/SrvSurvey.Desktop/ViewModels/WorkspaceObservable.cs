using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Data.Converters;

namespace SrvSurvey.Desktop.ViewModels;

public abstract class WorkspaceObservable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Changed(name);
        return true;
    }
}

internal sealed class WorkspaceCommand(Action execute, Func<bool>? enabled = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => enabled?.Invoke() ?? true;
    public void Execute(object? parameter) { if (CanExecute(parameter)) execute(); }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

internal sealed class WorkspaceParameterCommand(Action<object?> execute) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { /* This command is always enabled. */ }
        remove { /* This command is always enabled. */ }
    }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute(parameter);
}

internal sealed class WorkspaceTableSorter
{
    private string propertyName = string.Empty;
    private bool descending;

    public void Toggle(object? parameter)
    {
        if (parameter is not string requested || requested.Length == 0)
        {
            return;
        }

        if (propertyName.Equals(requested, StringComparison.Ordinal))
        {
            descending = !descending;
            return;
        }

        propertyName = requested;
        descending = false;
    }

    public IReadOnlyList<T> Apply<T>(IEnumerable<T> source)
    {
        var rows = source.ToArray();
        if (propertyName.Length == 0)
        {
            return rows;
        }

        var property = typeof(T).GetProperty(propertyName);
        if (property is null)
        {
            return rows;
        }

        var ordered = descending
            ? rows.OrderByDescending(row => property.GetValue(row), SortValueComparer.Instance)
            : rows.OrderBy(row => property.GetValue(row), SortValueComparer.Instance);
        return ordered.ToArray();
    }

    public string Indicator(string requested)
    {
        if (!propertyName.Equals(requested, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return descending ? "↓" : "↑";
    }

    private sealed class SortValueComparer : IComparer<object?>
    {
        public static SortValueComparer Instance { get; } = new();

        public int Compare(object? left, object? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return 1;
            if (right is null) return -1;
            if (left is string leftText && right is string rightText)
                return StringComparer.CurrentCultureIgnoreCase.Compare(leftText, rightText);
            if (left.GetType() == right.GetType() && left is IComparable comparable)
                return comparable.CompareTo(right);
            return StringComparer.CurrentCultureIgnoreCase.Compare(left.ToString(), right.ToString());
        }
    }
}

public sealed class WorkspaceSortIndicators
{
    private readonly Func<string, string> indicator;

    internal WorkspaceSortIndicators(Func<string, string> indicator) =>
        this.indicator = indicator;

    public string this[string propertyName] => indicator(propertyName);

}

public sealed class WorkspaceSortIndicatorConverter : IValueConverter
{
    public static WorkspaceSortIndicatorConverter Instance { get; } = new();

    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        value is WorkspaceSortIndicators indicators && parameter is string propertyName
            ? indicators[propertyName]
            : string.Empty;

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}
