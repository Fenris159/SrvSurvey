using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using SrvSurvey.Desktop.Localization;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Controls;

/// <summary>Keeps unavailable VR routes visible while skipping them in every user selection path.</summary>
public sealed class VrPlatformPicker : ComboBox
{
    public static readonly StyledProperty<IReadOnlyList<VrPlatformProfile>> ProfilesProperty =
        AvaloniaProperty.Register<VrPlatformPicker, IReadOnlyList<VrPlatformProfile>>(nameof(Profiles), []);
    public static readonly StyledProperty<VrPlatformProfile?> SelectedProfileProperty = AvaloniaProperty.Register<
        VrPlatformPicker,
        VrPlatformProfile?
    >(nameof(SelectedProfile), defaultBindingMode: BindingMode.TwoWay);
    private bool updating;
    private string searchPrefix = string.Empty;
    private long lastSearchTime;

    public VrPlatformPicker()
    {
        SelectionChanged += OnSelectionChanged;
    }

    protected override Type StyleKeyOverride => typeof(ComboBox);

    public IReadOnlyList<VrPlatformProfile> Profiles
    {
        get => GetValue(ProfilesProperty);
        set => SetValue(ProfilesProperty, value);
    }

    public VrPlatformProfile? SelectedProfile
    {
        get => GetValue(SelectedProfileProperty);
        set => SetValue(SelectedProfileProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ProfilesProperty)
        {
            updating = true;
            try
            {
                // Actual item controls retain IsEnabled before the popup creates its visual rows.
                ItemsSource = Profiles
                    .Select(profile => new ComboBoxItem
                    {
                        Content = profile,
                        IsEnabled = profile.IsAvailable,
                        ContentTemplate = ItemTemplate,
                    })
                    .ToArray();
            }
            finally
            {
                updating = false;
            }
            SynchronizeSelection();
        }
        else if (change.Property == ItemTemplateProperty)
        {
            foreach (ComboBoxItem item in Items.OfType<ComboBoxItem>())
            {
                item.ContentTemplate = ItemTemplate;
            }
        }
        else if (change.Property == SelectedProfileProperty)
        {
            SynchronizeSelection();
        }
    }

    private void SynchronizeSelection()
    {
        updating = true;
        try
        {
            SetCurrentValue(
                SelectedItemProperty,
                Items
                    .OfType<ComboBoxItem>()
                    .FirstOrDefault(item =>
                        item.Content is VrPlatformProfile profile && profile.Id == SelectedProfile?.Id
                    )
            );
        }
        finally
        {
            updating = false;
        }
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (updating)
        {
            return;
        }
        if (SelectedItem is ComboBoxItem { Content: VrPlatformProfile { IsAvailable: true } profile })
        {
            SetCurrentValue(SelectedProfileProperty, profile);
        }
        else
        {
            SynchronizeSelection();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!e.Handled && IsDropDownOpen && SelectedProfile?.IsAvailable == false && e.Key is Key.Down or Key.Up)
        {
            int step = e.Key == Key.Down ? 1 : -1;
            for (int index = SelectedIndex + step; index >= 0 && index < ItemCount; index += step)
            {
                if (Items[index] is ComboBoxItem { IsEnabled: true } item)
                {
                    SetCurrentValue(SelectedItemProperty, item);
                    item.Focus();
                    e.Handled = true;
                    return;
                }
            }
        }
        base.OnKeyDown(e);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        if (e.Handled || string.IsNullOrEmpty(e.Text))
        {
            return;
        }
        long now = Environment.TickCount64;
        searchPrefix = now - lastSearchTime <= 1000 ? searchPrefix + e.Text : e.Text;
        lastSearchTime = now;
        ComboBoxItem? match = Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item =>
                item.Content is VrPlatformProfile { IsAvailable: true } profile
                && LocalizationCatalog
                    .Translate(profile.DisplayName)
                    .StartsWith(searchPrefix, StringComparison.CurrentCultureIgnoreCase)
            );
        if (match is not null)
        {
            SetCurrentValue(SelectedItemProperty, match);
        }
        e.Handled = true;
    }
}
