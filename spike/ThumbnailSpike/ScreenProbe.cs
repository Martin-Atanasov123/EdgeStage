using System.Runtime.InteropServices;
using EdgeStage.Core.Interop;

namespace ThumbnailSpike;

/// <summary>
/// TEST INSTRUMENTATION ONLY. Reads composed screen pixels (GDI BitBlt from the screen DC) so the
/// spike can verify objectively that a DWM thumbnail is visible and changing. EdgeStage itself never
/// captures the screen; previews are pure DWM thumbnails.
/// </summary>
internal static unsafe partial class ScreenProbe
{
    private const uint SRCCOPY = 0x00CC0020;
    private const uint CAPTUREBLT = 0x40000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
        public uint color0, color1, color2, color3; // room for masks; unused for BI_RGB
    }

    [LibraryImport("user32.dll")] private static partial nint GetDC(nint hwnd);
    [LibraryImport("user32.dll")] private static partial int ReleaseDC(nint hwnd, nint hdc);
    [LibraryImport("gdi32.dll")] private static partial nint CreateCompatibleDC(nint hdc);
    [LibraryImport("gdi32.dll")] private static partial nint CreateCompatibleBitmap(nint hdc, int cx, int cy);
    [LibraryImport("gdi32.dll")] private static partial nint SelectObject(nint hdc, nint h);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(nint hdc, int x, int y, int cx, int cy, nint hdcSrc, int x1, int y1, uint rop);
    [LibraryImport("gdi32.dll")] private static partial int GetDIBits(nint hdc, nint hbm, uint start, uint cLines, byte* lpvBits, ref BITMAPINFO lpbmi, uint usage);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DeleteObject(nint ho);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DeleteDC(nint hdc);

    /// <summary>Captures a screen rectangle (physical pixels) as top-down BGRA.</summary>
    public static byte[] Capture(RECT r)
    {
        int w = Math.Max(1, r.Width), h = Math.Max(1, r.Height);
        nint screen = GetDC(0);
        nint mem = CreateCompatibleDC(screen);
        nint bmp = CreateCompatibleBitmap(screen, w, h);
        nint old = SelectObject(mem, bmp);
        try
        {
            BitBlt(mem, 0, 0, w, h, screen, r.Left, r.Top, SRCCOPY | CAPTUREBLT);
            SelectObject(mem, old);
            var info = new BITMAPINFO { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            var pixels = new byte[w * h * 4];
            fixed (byte* p = pixels)
                GetDIBits(mem, bmp, 0, (uint)h, p, ref info, 0);
            return pixels;
        }
        finally
        {
            DeleteObject(bmp);
            DeleteDC(mem);
            ReleaseDC(0, screen);
        }
    }

    public readonly record struct Rgb(byte R, byte G, byte B)
    {
        public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
        public double DistanceTo(Rgb o) => Math.Sqrt(Sq(R - o.R) + Sq(G - o.G) + Sq(B - o.B));
        private static double Sq(int v) => v * v;
    }

    public readonly record struct Stats(Rgb Mean, double FractionNear, int Pixels);

    /// <summary>Mean color of the region and the fraction of pixels within <paramref name="tolerance"/> of <paramref name="target"/>.</summary>
    public static Stats Analyze(RECT r, Rgb target, double tolerance = 60)
    {
        byte[] px = Capture(r);
        long sr = 0, sg = 0, sb = 0;
        int near = 0, n = px.Length / 4;
        for (int i = 0; i < px.Length; i += 4)
        {
            byte b = px[i], g = px[i + 1], rr = px[i + 2];
            sr += rr; sg += g; sb += b;
            if (new Rgb(rr, g, b).DistanceTo(target) <= tolerance) near++;
        }
        return new Stats(new Rgb((byte)(sr / n), (byte)(sg / n), (byte)(sb / n)), (double)near / n, n);
    }

    /// <summary>Shrinks a rectangle toward its center to avoid edge blending when probing.</summary>
    public static RECT Inset(RECT r, double fraction)
    {
        int dx = (int)(r.Width * fraction), dy = (int)(r.Height * fraction);
        return new RECT(r.Left + dx, r.Top + dy, r.Right - dx, r.Bottom - dy);
    }
}
