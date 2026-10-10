using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EdgeStage.Core.Interop;

namespace ThumbnailSpike;

/// <summary>
/// TEST INSTRUMENTATION ONLY: saves PNG captures of one window's screen area (e.g. the EdgeStage
/// sidebar while it lists only synthetic test windows).
/// </summary>
internal static class Capture
{
    public static async Task<string> WindowFrames(string title, string outPrefix, int frames, int intervalMs)
    {
        var log = new StringBuilder();
        nint hwnd = 0;
        for (int i = 0; i < 40 && hwnd == 0; i++)
        {
            hwnd = User32.FindWindow(null, title);
            if (hwnd == 0) await Task.Delay(100);
        }
        if (hwnd == 0 || !User32.IsWindowVisible(hwnd))
            return $"window '{title}' not found or not visible";

        User32.GetWindowRect(hwnd, out RECT r);
        var area = r; // exactly the window: nothing of other windows behind it
        log.AppendLine($"window rect {r}, capture {area}");
        for (int f = 0; f < frames; f++)
        {
            byte[] pixels = ScreenProbe.Capture(area);
            string path = $"{outPrefix}-{f}.png";
            Save(pixels, area.Width, area.Height, path);
            log.AppendLine(path);
            if (f < frames - 1) await Task.Delay(intervalMs);
        }
        return log.ToString();
    }

    public static string Rect(int x, int y, int width, int height, string outPrefix)
    {
        var area = new RECT(x, y, x + width, y + height);
        string path = $"{outPrefix}-0.png";
        Save(ScreenProbe.Capture(area), width, height, path);
        return $"rect {area}\n{path}";
    }

    private static void Save(byte[] bgra, int width, int height, string path)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, bgra, width * 4);
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }
}
