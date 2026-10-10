using System.Runtime.InteropServices;

namespace EdgeStage.Core.Interop;

/// <summary>kernel32.dll imports.</summary>
public static partial class Kernel32
{
    private const string Lib = "kernel32.dll";

    /// <summary>Pseudo-handle for the current process; never needs closing.</summary>
    public static readonly nint CurrentProcess = -1;

    [LibraryImport(Lib, SetLastError = true)]
    public static partial nint OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [LibraryImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint hObject);

    [LibraryImport(Lib, EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool QueryFullProcessImageName(nint hProcess, uint dwFlags, char* lpExeName, ref int lpdwSize);

    [LibraryImport(Lib)]
    public static partial uint GetCurrentThreadId();

    [LibraryImport(Lib, EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint GetModuleHandle(string? lpModuleName);
}
