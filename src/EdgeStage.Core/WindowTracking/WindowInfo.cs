namespace EdgeStage.Core.WindowTracking;

/// <summary>A switchable window as presented to the UI. Identity is the HWND; everything else may change.</summary>
public sealed record WindowInfo(
    nint Hwnd,
    uint ProcessId,
    string Title,
    string ClassName,
    string? ProcessPath,
    bool IsMinimized)
{
    /// <summary>File name without extension, used when the window has no title.</summary>
    public string ProcessName => ProcessPath is null ? "" : Path.GetFileNameWithoutExtension(ProcessPath);

    public string DisplayTitle => !string.IsNullOrWhiteSpace(Title) ? Title : ProcessName is { Length: > 0 } name ? name : ClassName;
}
