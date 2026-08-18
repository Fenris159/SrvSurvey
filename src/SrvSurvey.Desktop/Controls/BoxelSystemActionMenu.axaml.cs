using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace SrvSurvey.Desktop.Controls;

public sealed partial class BoxelSystemActionMenu : UserControl
{
    internal const int RevealDelayMilliseconds = 1_500;
    private const string EngagedClass = "engaged";
    private const int RevealAnimationDelayMilliseconds = 50;
    private static WeakReference<BoxelSystemActionMenu>? activeMenu;
    private readonly DispatcherTimer closeTimer;
    private readonly DispatcherTimer revealAnimationTimer;
    private readonly DispatcherTimer revealTimer;
    private bool explicitOpenRequested;
    private bool revealPending;

    internal bool IsRevealPending => revealPending;

    public BoxelSystemActionMenu()
    {
        InitializeComponent();
        closeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        closeTimer.Tick += CloseTimer_Tick;
        revealTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(RevealDelayMilliseconds)
        };
        revealTimer.Tick += RevealTimer_Tick;
        revealAnimationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(RevealAnimationDelayMilliseconds)
        };
        revealAnimationTimer.Tick += RevealAnimationTimer_Tick;
        DetachedFromVisualTree += (_, _) => CloseMenu();
    }

    private void Menu_PointerEntered(object? sender, PointerEventArgs eventArgs)
    {
        closeTimer.Stop();
        if (sender == Launcher)
        {
            BeginOpenIntent(explicitRequest: false);
        }
    }

    private void Menu_PointerExited(object? sender, PointerEventArgs eventArgs)
    {
        closeTimer.Stop();
        if (sender == Launcher && !MenuPopup.IsOpen)
        {
            CancelOpenIntent();
            return;
        }

        closeTimer.Start();
    }

    private void Menu_PointerWheelChanged(
        object? sender,
        PointerWheelEventArgs eventArgs)
    {
        CloseMenu();
    }

    private void Launcher_Click(object? sender, RoutedEventArgs eventArgs)
    {
        BeginOpenIntent(explicitRequest: true);
    }

    private void Launcher_KeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.Escape)
        {
            return;
        }

        CloseMenu();
        eventArgs.Handled = true;
    }

    private void Action_Click(object? sender, RoutedEventArgs eventArgs)
    {
        Dispatcher.UIThread.Post(CloseMenu, DispatcherPriority.Background);
    }

    private void CloseTimer_Tick(object? sender, EventArgs eventArgs)
    {
        closeTimer.Stop();
        TryCloseForPointerExit(
            Launcher.IsPointerOver,
            MenuHitSurface.IsPointerOver);
    }

    private void RevealTimer_Tick(object? sender, EventArgs eventArgs)
    {
        TryRevealMenu(Launcher.IsPointerOver);
    }

    private void RevealAnimationTimer_Tick(object? sender, EventArgs eventArgs)
    {
        AdvanceCommittedReveal();
    }

    internal void AdvanceCommittedReveal()
    {
        revealAnimationTimer.Stop();
        if (MenuPopup.IsOpen
            && MenuSurface.IsVisible
            && !MenuSurface.Classes.Contains("open"))
        {
            MenuSurface.Classes.Add("open");
        }
    }

    internal void BeginOpenIntent(bool explicitRequest)
    {
        ClaimActiveMenu(this);
        closeTimer.Stop();
        explicitOpenRequested |= explicitRequest;
        if (!Launcher.Classes.Contains(EngagedClass))
        {
            Launcher.Classes.Add(EngagedClass);
        }

        if (MenuPopup.IsOpen || revealPending)
        {
            return;
        }

        revealPending = true;
        MenuSurface.Classes.Remove("open");
        MenuSurface.IsVisible = false;
        revealTimer.Stop();
        revealTimer.Start();
    }

    internal void CancelOpenIntent()
    {
        if (!MenuPopup.IsOpen)
        {
            CloseMenu();
        }
    }

    internal bool TryRevealMenu(bool launcherIsPointerOver)
    {
        revealTimer.Stop();
        if (!revealPending)
        {
            return false;
        }

        revealPending = false;
        if (!launcherIsPointerOver && !explicitOpenRequested)
        {
            CloseMenu();
            return false;
        }

        explicitOpenRequested = false;
        if (!Launcher.Classes.Contains(EngagedClass))
        {
            CloseMenu();
            return false;
        }

        MenuSurface.Classes.Remove("open");
        MenuSurface.IsVisible = true;
        MenuPopup.IsOpen = true;
        revealAnimationTimer.Stop();
        revealAnimationTimer.Start();
        return true;
    }

    internal bool TryCloseForPointerExit(
        bool launcherIsPointerOver,
        bool menuIsPointerOver)
    {
        if (launcherIsPointerOver || menuIsPointerOver)
        {
            return false;
        }

        CloseMenu();
        return true;
    }

    internal static bool DismissActiveMenuForScroll()
    {
        if (activeMenu?.TryGetTarget(out var active) != true || active is null)
        {
            activeMenu = null;
            return false;
        }

        active.CloseMenu();
        return true;
    }

    private void CloseMenu()
    {
        closeTimer.Stop();
        revealAnimationTimer.Stop();
        revealTimer.Stop();
        explicitOpenRequested = false;
        revealPending = false;
        Launcher.Classes.Remove(EngagedClass);
        MenuSurface.Classes.Remove("open");
        MenuSurface.IsVisible = false;
        MenuPopup.IsOpen = false;
        ReleaseActiveMenu(this);
    }

    private static void ClaimActiveMenu(BoxelSystemActionMenu menu)
    {
        if (activeMenu?.TryGetTarget(out var active) == true
            && !ReferenceEquals(active, menu))
        {
            active.CloseMenu();
        }
        activeMenu = new WeakReference<BoxelSystemActionMenu>(menu);
    }

    private static void ReleaseActiveMenu(BoxelSystemActionMenu menu)
    {
        if (activeMenu?.TryGetTarget(out var active) == true
            && ReferenceEquals(active, menu))
        {
            activeMenu = null;
        }
    }

}
