using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using EdgeStage.Core.Diagnostics;
using EdgeStage.Core.Input;
using EdgeStage.Core.Interop;
using EdgeStage.Core.Settings;

namespace EdgeStage.UI;

/// <summary>Actions the tray menu can trigger; implemented by the controller.</summary>
internal interface ITrayActions
{
    AppSettings Settings { get; }
    bool HotkeyAvailable { get; }
    bool StartWithWindows { get; }
    void ShowSidebar();
    void UpdateSettings(AppSettings settings);
    void SetStartWithWindows(bool enabled);
    void OpenSettings();
    void Exit();
}

/// <summary>The small tray context menu. Rebuilt on every open so it always reflects current state.</summary>
internal static class TrayMenu
{
    private static readonly int[] DelayPresets = [150, 300, 500];
    private static ContextMenu? s_lastMenu;

    public static void CloseOpenMenu()
    {
        if (s_lastMenu is { IsOpen: true })
            s_lastMenu.IsOpen = false;
        s_lastMenu = null;
    }

    /// <param name="at">Fixed screen position (DIPs) for automated screenshots; null = at the mouse pointer.</param>
    public static void Show(ITrayActions actions, Point? at = null)
    {
        ContextMenu menu = Build(actions);
        menu.Placement = at is null ? PlacementMode.MousePoint : PlacementMode.AbsolutePoint;
        menu.HorizontalOffset = at?.X ?? 0;
        menu.VerticalOffset = at?.Y ?? 0;
        // A screenshot run has no user click, hence no foreground rights; keep the menu up for it.
        menu.StaysOpen = at is not null;
        Attach(menu, at);
    }

    /// <summary>Builds the menu for the current state (separate from showing it, for tests).</summary>
    public static ContextMenu Build(ITrayActions actions)
    {
        AppSettings s = actions.Settings;
        var menu = new ContextMenu { Style = Application.Current?.TryFindResource("TrayMenu") as Style };

        menu.Items.Add(Item("Show sidebar", () => actions.ShowSidebar(), bold: true));
        menu.Items.Add(new Separator());

        menu.Items.Add(Check("Edge activation", s.EdgeActivationEnabled,
            () => actions.UpdateSettings(actions.Settings with { EdgeActivationEnabled = !actions.Settings.EdgeActivationEnabled })));

        var position = new MenuItem { Header = "Position" };
        foreach (ScreenEdge edge in new[] { ScreenEdge.Left, ScreenEdge.Right })
            position.Items.Add(Check(edge == ScreenEdge.Left ? "Left edge" : "Right edge", s.Edge == edge,
                () => actions.UpdateSettings(actions.Settings with { Edge = edge })));
        menu.Items.Add(position);

        var delay = new MenuItem { Header = "Activation delay" };
        foreach (int ms in DelayPresets)
            delay.Items.Add(Check($"{ms} ms", s.ActivationDelayMs == ms,
                () => actions.UpdateSettings(actions.Settings with { ActivationDelayMs = ms })));
        delay.Items.Add(new Separator());
        delay.Items.Add(Check(DelayPresets.Contains(s.ActivationDelayMs) ? "Custom…" : $"Custom ({s.ActivationDelayMs} ms)…",
            !DelayPresets.Contains(s.ActivationDelayMs), actions.OpenSettings));
        menu.Items.Add(delay);

        menu.Items.Add(Item(actions.HotkeyAvailable ? $"Shortcut: {s.Hotkey}…" : $"Shortcut: {s.Hotkey} (unavailable)…", actions.OpenSettings));
        menu.Items.Add(Check("Start with Windows", actions.StartWithWindows,
            () => actions.SetStartWithWindows(!actions.StartWithWindows)));

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Settings…", actions.OpenSettings));
        menu.Items.Add(Item("Exit EdgeStage", actions.Exit));
        return menu;
    }

    private static void Attach(ContextMenu menu, Point? at)
    {
        // A tray menu must belong to the foreground window, or it will not close when the user
        // clicks elsewhere (documented TrackPopupMenu behavior; same for WPF popups). The click on
        // the notification icon gives this process the right to take the foreground.
        menu.Opened += (_, _) =>
        {
            bool foreground = at is null && PresentationSource.FromVisual(menu) is HwndSource source && User32.SetForegroundWindow(source.Handle);
            Log.Debug($"Tray menu opened (foreground: {foreground}).");
        };
        menu.Closed += (_, _) => Log.Debug("Tray menu closed.");
        menu.IsOpen = true;
        s_lastMenu = menu;
    }

    private static MenuItem Item(string header, Action onClick, bool bold = false)
    {
        var item = new MenuItem { Header = header };
        if (bold)
            item.FontWeight = FontWeights.SemiBold;
        item.Click += (_, _) => onClick();
        return item;
    }

    private static MenuItem Check(string header, bool isChecked, Action onClick)
    {
        var item = new MenuItem { Header = header, IsChecked = isChecked };
        item.Click += (_, _) => onClick();
        return item;
    }
}
