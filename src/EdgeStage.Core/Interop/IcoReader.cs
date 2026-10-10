using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace EdgeStage.Core.Interop;

/// <summary>Picks the best image out of a multi-resolution .ico file and turns it into an HICON.</summary>
public static partial class IcoReader
{
    public readonly record struct Entry(int Size, int Offset, int Length);

    /// <summary>Parses the ICONDIR table. Returns an empty list for data that is not an icon file.</summary>
    public static List<Entry> ReadEntries(ReadOnlySpan<byte> ico)
    {
        var entries = new List<Entry>();
        if (ico.Length < 6 || BinaryPrimitives.ReadUInt16LittleEndian(ico) != 0 || BinaryPrimitives.ReadUInt16LittleEndian(ico[2..]) != 1)
            return entries;
        int count = BinaryPrimitives.ReadUInt16LittleEndian(ico[4..]);
        for (int i = 0; i < count; i++)
        {
            int at = 6 + i * 16;
            if (at + 16 > ico.Length)
                break;
            int size = ico[at] == 0 ? 256 : ico[at];
            int length = BinaryPrimitives.ReadInt32LittleEndian(ico[(at + 8)..]);
            int offset = BinaryPrimitives.ReadInt32LittleEndian(ico[(at + 12)..]);
            if (offset > 0 && length > 0 && (long)offset + length <= ico.Length)
                entries.Add(new Entry(size, offset, length));
        }
        return entries;
    }

    /// <summary>Smallest image at least <paramref name="size"/> pixels (downscaling looks better than upscaling), else the largest.</summary>
    public static Entry? SelectBest(IReadOnlyList<Entry> entries, int size)
    {
        Entry? best = null;
        foreach (Entry e in entries.OrderBy(e => e.Size))
        {
            best = e;
            if (e.Size >= size)
                break;
        }
        return best;
    }

    /// <summary>Creates an HICON of <paramref name="size"/> pixels; the caller owns it (DestroyIcon).</summary>
    public static unsafe nint CreateIcon(byte[] ico, int size)
    {
        if (SelectBest(ReadEntries(ico), size) is not Entry entry)
            return 0;
        fixed (byte* p = &ico[entry.Offset])
            return CreateIconFromResourceEx(p, (uint)entry.Length, true, 0x00030000, size, size, 0);
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static unsafe partial nint CreateIconFromResourceEx(byte* presbits, uint dwResSize,
        [MarshalAs(UnmanagedType.Bool)] bool fIcon, uint dwVer, int cxDesired, int cyDesired, uint flags);
}
