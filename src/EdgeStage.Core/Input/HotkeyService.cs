using System.Runtime.InteropServices;
using EdgeStage.Core.Diagnostics;
using EdgeStage.Core.Interop;
using static EdgeStage.Core.Interop.NativeConstants;

namespace EdgeStage.Core.Input;

public enum HotkeyRegistrationStatus
{
    Registered,
    /// <summary>Another application (or Windows) already owns this combination.</summary>
    Conflict,
    /// <summary>The combination is not usable as a global shortcut.</summary>
    Invalid,
    Failed,
    /// <summary>The same combination is already used by EdgeStage's other shortcut.</summary>
    Duplicate,
}

public readonly record struct HotkeyRegistrationResult(HotkeyRegistrationStatus Status, int Win32Error = 0)
{
    public bool Succeeded => Status == HotkeyRegistrationStatus.Registered;
}

/// <summary>
/// Global hotkeys via <c>RegisterHotKey</c>. The owner supplies a window that receives
/// <c>WM_HOTKEY</c> (or 0 to bind to the calling thread) and forwards messages to
/// <see cref="HandleMessage"/>. Registration failures are reported, never retried silently.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private readonly nint _window;
    private readonly Dictionary<int, Hotkey> _registered = [];

    public HotkeyService(nint window)
    {
        _window = window;
    }

    /// <summary>Raised with the registration id when a registered hotkey is pressed.</summary>
    public event Action<int>? Pressed;

    public bool IsRegistered(int id) => _registered.ContainsKey(id);

    /// <summary>Registers (or replaces) the hotkey for <paramref name="id"/>.</summary>
    public HotkeyRegistrationResult Register(int id, Hotkey hotkey)
    {
        if (!hotkey.IsValid)
            return new HotkeyRegistrationResult(HotkeyRegistrationStatus.Invalid);

        Unregister(id);
        // MOD_NOREPEAT: holding the keys down fires once, not on every auto-repeat.
        if (User32.RegisterHotKey(_window, id, (uint)hotkey.Modifiers | MOD_NOREPEAT, (uint)hotkey.VirtualKey))
        {
            _registered[id] = hotkey;
            return new HotkeyRegistrationResult(HotkeyRegistrationStatus.Registered);
        }

        int error = Marshal.GetLastPInvokeError();
        var status = error == ERROR_HOTKEY_ALREADY_REGISTERED ? HotkeyRegistrationStatus.Conflict : HotkeyRegistrationStatus.Failed;
        Log.Warn($"RegisterHotKey({hotkey}) failed: {status} (Win32 error {error}).");
        return new HotkeyRegistrationResult(status, error);
    }

    /// <summary>
    /// Registers a bare key (no modifiers) such as Escape. Only for short-lived use while EdgeStage
    /// is on screen without keyboard focus; callers must <see cref="Unregister"/> it promptly,
    /// because while registered the key does not reach other applications.
    /// </summary>
    public HotkeyRegistrationResult RegisterTransientKey(int id, int virtualKey)
    {
        Unregister(id);
        if (User32.RegisterHotKey(_window, id, MOD_NOREPEAT, (uint)virtualKey))
        {
            _registered[id] = new Hotkey(HotkeyModifiers.None, virtualKey);
            return new HotkeyRegistrationResult(HotkeyRegistrationStatus.Registered);
        }
        int error = Marshal.GetLastPInvokeError();
        Log.Warn($"Transient key 0x{virtualKey:X2} could not be registered (Win32 error {error}).");
        return new HotkeyRegistrationResult(error == ERROR_HOTKEY_ALREADY_REGISTERED ? HotkeyRegistrationStatus.Conflict : HotkeyRegistrationStatus.Failed, error);
    }

    /// <summary>Checks whether a combination is free right now by registering and releasing it.</summary>
    public HotkeyRegistrationResult Probe(Hotkey hotkey, int probeId = 0xBFFF)
    {
        var result = Register(probeId, hotkey);
        Unregister(probeId);
        return result;
    }

    public void Unregister(int id)
    {
        if (_registered.Remove(id))
            User32.UnregisterHotKey(_window, id);
    }

    /// <summary>Call from the window procedure; returns true when the message was a hotkey this service owns.</summary>
    public bool HandleMessage(uint message, nint wParam)
    {
        if (message != WM_HOTKEY)
            return false;
        int id = (int)wParam;
        if (!_registered.ContainsKey(id))
            return false;
        Pressed?.Invoke(id);
        return true;
    }

    public void Dispose()
    {
        foreach (int id in _registered.Keys.ToList())
            Unregister(id);
    }
}
