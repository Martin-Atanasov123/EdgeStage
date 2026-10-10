using System.Runtime.InteropServices;

namespace EdgeStage.Core.Interop;

// Blittable Win32 structures. Field order and sizes must match the Windows SDK headers exactly.

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public RECT(int left, int top, int right, int bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public readonly int Width => Right - Left;
    public readonly int Height => Bottom - Top;
    public readonly bool IsEmpty => Width <= 0 || Height <= 0;

    public override readonly string ToString() => $"[{Left},{Top} {Width}x{Height}]";
}

[StructLayout(LayoutKind.Sequential)]
public struct POINT
{
    public int X;
    public int Y;

    public POINT(int x, int y)
    {
        X = x;
        Y = y;
    }
}

/// <summary>Win32 SIZE (cx, cy).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct SIZE
{
    public int Width;
    public int Height;
}

[StructLayout(LayoutKind.Sequential)]
public struct MARGINS
{
    public int Left;
    public int Right;
    public int Top;
    public int Bottom;

    public static MARGINS All(int value) => new() { Left = value, Right = value, Top = value, Bottom = value };
}

[StructLayout(LayoutKind.Sequential)]
public struct MONITORINFO
{
    public int cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;

    public static MONITORINFO Create() => new() { cbSize = Marshal.SizeOf<MONITORINFO>() };
}

/// <summary>
/// DWM_THUMBNAIL_PROPERTIES. <c>opacity</c> is a BYTE followed by two BOOLs; sequential layout
/// inserts the same 3 padding bytes the native compiler does (total size 48).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct DWM_THUMBNAIL_PROPERTIES
{
    public uint dwFlags;
    public RECT rcDestination;
    public RECT rcSource;
    public byte opacity;
    public int fVisible;
    public int fSourceClientAreaOnly;
}

[StructLayout(LayoutKind.Sequential)]
public struct MSG
{
    public nint hwnd;
    public uint message;
    public nint wParam;
    public nint lParam;
    public uint time;
    public POINT pt;
    public uint lPrivate;
}

[StructLayout(LayoutKind.Sequential)]
public struct WINDOWPLACEMENT
{
    public uint length;
    public uint flags;
    public uint showCmd;
    public POINT ptMinPosition;
    public POINT ptMaxPosition;
    public RECT rcNormalPosition;
}

/// <summary>Payload of a WH_MOUSE_LL hook callback.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct MSLLHOOKSTRUCT
{
    public POINT pt;
    public uint mouseData;
    public uint flags;
    public uint time;
    public nuint dwExtraInfo;
}
