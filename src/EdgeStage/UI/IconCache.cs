using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EdgeStage.Core.Diagnostics;
using EdgeStage.Core.Interop;
using EdgeStage.Core.WindowTracking;

namespace EdgeStage.UI;

/// <summary>
/// Resolves window icons off the UI thread (a hung app can delay WM_GETICON by up to ~50 ms) and
/// caches them per window, so reopening the sidebar shows icons immediately.
/// </summary>
internal sealed class IconCache
{
    private const int MaxEntries = 256;
    private readonly Dictionary<nint, ImageSource?> _byWindow = [];

    public void Apply(CardViewModel card, WindowInfo info)
    {
        if (_byWindow.TryGetValue(info.Hwnd, out ImageSource? cached))
        {
            card.Icon = cached;
            if (cached is not null)
                return; // a missing icon is retried: apps sometimes set theirs late
        }
        _ = LoadAsync(card, info);
    }

    private async Task LoadAsync(CardViewModel card, WindowInfo info)
    {
        ImageSource? icon = await Task.Run(() => Resolve(info));
        if (_byWindow.Count >= MaxEntries)
            Prune();
        _byWindow[info.Hwnd] = icon;
        card.Icon = icon;
    }

    private void Prune()
    {
        foreach (nint hwnd in _byWindow.Keys.Where(h => !User32.IsWindow(h)).ToList())
            _byWindow.Remove(hwnd);
        if (_byWindow.Count >= MaxEntries)
            _byWindow.Clear();
    }

    private static ImageSource? Resolve(WindowInfo info)
    {
        IconHandle handle = IconResolver.Resolve(info.Hwnd, info.ProcessPath);
        if (!handle.IsValid)
            return null;
        try
        {
            BitmapSource bitmap = Imaging.CreateBitmapSourceFromHIcon(handle.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze(); // created off the UI thread; frozen objects can cross threads
            return bitmap;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException)
        {
            Log.Debug($"Icon conversion failed for 0x{info.Hwnd:X}: {ex.Message}");
            return null;
        }
        finally
        {
            handle.Release();
        }
    }
}
