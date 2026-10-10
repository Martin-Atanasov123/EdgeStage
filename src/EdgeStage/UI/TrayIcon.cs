using System.IO;
using System.Windows;
using EdgeStage.Core.Diagnostics;
using EdgeStage.Core.Interop;
using static EdgeStage.Core.Interop.NotifyIconNative;

namespace EdgeStage.UI;

/// <summary>
/// The notification-area icon, via Shell_NotifyIcon directly (no WinForms dependency). Survives
/// Explorer restarts by re-adding itself on the "TaskbarCreated" broadcast.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint IconId = 1;
    public static readonly uint CallbackMessage = NativeConstants.WM_APP + 1;
    private static readonly uint TaskbarCreatedMessage = User32.RegisterWindowMessage("TaskbarCreated");

    private readonly nint _window;
    private nint _icon;
    private string _tip;
    private bool _added;

    public TrayIcon(nint window, string tip)
    {
        _window = window;
        _tip = tip;
        _icon = LoadAppIcon();
        Add();
    }

    /// <summary>Left click or keyboard selection of the icon.</summary>
    public event Action? Invoked;

    /// <summary>Right click / context-menu key.</summary>
    public event Action? ContextMenuRequested;

    public event Action? BalloonClicked;

    public bool IsVisible => _added;

    public void SetTip(string tip)
    {
        _tip = tip;
        if (!_added)
            return;
        var data = NOTIFYICONDATAW.Create(_window, IconId);
        data.uFlags = NIF_TIP | NIF_SHOWTIP;
        data.SetTip(tip);
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    /// <summary>Shows a notification balloon (rendered as a toast on Windows 10/11); respects Focus Assist quiet time.</summary>
    public void ShowBalloon(string title, string text, bool warning = false)
    {
        if (!_added)
            return;
        var data = NOTIFYICONDATAW.Create(_window, IconId);
        data.uFlags = NIF_INFO;
        data.dwInfoFlags = (warning ? NIIF_WARNING : NIIF_INFO) | NIIF_RESPECT_QUIET_TIME;
        data.SetInfoTitle(title);
        data.SetInfo(text);
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    /// <summary>Call for every message of the owning window; returns true when consumed.</summary>
    public bool HandleMessage(uint message, nint wParam, nint lParam)
    {
        if (message == TaskbarCreatedMessage)
        {
            Log.Info("Taskbar was re-created (Explorer restarted); re-adding the tray icon.");
            _added = false;
            Add();
            return false; // broadcast: let others see it too
        }
        if (message != CallbackMessage)
            return false;

        switch ((uint)(lParam & 0xFFFF)) // NOTIFYICON_VERSION_4: event in LOWORD(lParam)
        {
            case NIN_SELECT or NIN_KEYSELECT:
                Invoked?.Invoke();
                break;
            case WM_CONTEXTMENU:
                ContextMenuRequested?.Invoke();
                break;
            case NIN_BALLOONUSERCLICK:
                BalloonClicked?.Invoke();
                break;
        }
        return true;
    }

    private void Add()
    {
        var data = NOTIFYICONDATAW.Create(_window, IconId);
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = _icon;
        data.SetTip(_tip);
        _added = Shell_NotifyIcon(NIM_ADD, ref data);
        if (!_added)
        {
            Log.Warn("Tray icon could not be added (the taskbar may not be running yet).");
            return;
        }
        data.uTimeoutOrVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIcon(NIM_SETVERSION, ref data);
    }

    /// <summary>Loads the embedded .ico at the notification-area size for the current system DPI.</summary>
    private static nint LoadAppIcon()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/EdgeStage;component/Assets/EdgeStage.ico"));
            if (info is null)
                return 0;
            using var stream = info.Stream;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            int size = (int)Math.Round(16 * User32.GetDpiForSystem() / 96.0);
            nint icon = IcoReader.CreateIcon(buffer.ToArray(), size);
            if (icon == 0)
                Log.Warn($"Tray icon image ({size}px) could not be created.");
            return icon;
        }
        catch (IOException ex)
        {
            Log.Warn($"Tray icon image could not be loaded: {ex.Message}");
            return 0;
        }
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = NOTIFYICONDATAW.Create(_window, IconId);
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _added = false;
        }
        if (_icon != 0)
        {
            User32.DestroyIcon(_icon);
            _icon = 0;
        }
    }
}
