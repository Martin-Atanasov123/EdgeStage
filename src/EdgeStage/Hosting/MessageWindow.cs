using System.Windows.Interop;

namespace EdgeStage.Hosting;

/// <summary>
/// Hidden, never-shown top-level window that receives WM_HOTKEY, the single-instance signals,
/// and broadcast messages (a message-only window would miss broadcasts such as TaskbarCreated).
/// </summary>
internal sealed class MessageWindow : IDisposable
{
    public const string WindowTitle = "EdgeStage.MessageWindow";

    public delegate bool MessageHandler(uint message, nint wParam, nint lParam);

    private readonly HwndSource _source;

    public MessageWindow()
    {
        var parameters = new HwndSourceParameters(WindowTitle)
        {
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP, not WS_VISIBLE
            ExtendedWindowStyle = 0x00000080,          // WS_EX_TOOLWINDOW
            Width = 0,
            Height = 0,
            PositionX = -32000,
            PositionY = -32000,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public nint Handle => _source.Handle;

    /// <summary>Handlers return true when they consumed the message.</summary>
    public MessageHandler? Handler { get; set; }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (Handler?.Invoke((uint)msg, wParam, lParam) == true)
            handled = true;
        return 0;
    }

    public void Dispose() => _source.Dispose();
}
