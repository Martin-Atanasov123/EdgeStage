# Phase 1 spike findings — DWM live thumbnails in WPF

**Machine:** Windows 11 Pro build 26300 (Insider) · Ryzen 5 5600X · 16 GB · GeForce GTX 1650 · one 1920×1080 @ 60 Hz monitor at 100 % scaling · .NET 10.0.8 x64.
**Spike:** `spike/ThumbnailSpike` (uses the production interop in `src/EdgeStage.Core/Interop`).
**Raw evidence:** [`spike/auto-report.md`](spike/auto-report.md), [`spike/followup-report.md`](spike/followup-report.md), [`spike/realwindow-report.md`](spike/realwindow-report.md).

How it was measured: test windows with solid known colors are used as thumbnail sources; the
composed screen is read back with GDI `BitBlt` (spike-only test instrumentation — EdgeStage never
captures the screen) and pixel statistics decide whether a thumbnail is visible, where it is, and
when it changes. Against the user's real windows only aggregate statistics were recorded.

## Verdict

**DWM thumbnails work reliably as the preview mechanism. Windows.Graphics.Capture is not needed.**

| Spike requirement | Result | Evidence |
|---|---|---|
| WPF window hosts DWM thumbnails of other windows | **Verified** — all 5 window configurations tested render the thumbnail; placement exact to ±1 px | auto-report §1 |
| Thumbnails visible inside the preview UI | **Verified** — scrolling card list with clipping; 6/6 real windows drawn (Notepad, Paint, UWP Calculator, Claude, 2 test windows) | realwindow §1, §3 |
| Thumbnails update when the source changes | **Verified** — in-process and out-of-process; thumbnail trails the source window by ≈1 frame (source median 12–19 ms, thumbnail 28–30 ms after the color change) | auto-report §1, realwindow §2 (10/10 changes seen) |
| Clicking a preview activates the right window | **Partly verified** — `SW_RESTORE` + `SetForegroundWindow` brought an out-of-process window (normal and minimized) to the foreground when EdgeStage held foreground rights. Real mouse clicks **NOT RUN** (desktop-control permission was declined for this session) | auto-report §7 |
| Thumbnails register/release without leaks | **Verified** — 0 handle/GDI/USER growth after 1,000 single and 100×12 batch cycles and 50 full real-window rebuilds; release never throws, even after source or destination is destroyed | auto-report §4–6, realwindow §5 |

## Decisions taken from the evidence

1. **Sidebar window = WPF `AllowsTransparency=True`, `WindowStyle=None`, semi-opaque charcoal drawn by WPF (rounded corners drawn by WPF).**
   - Thumbnails render on it exactly like on an opaque window (verified).
   - The system Acrylic backdrop only shows while the window is *active*; inactive it falls back to flat `#545454` (follow-up §C). Edge-opened sidebars are never active, so Acrylic would look different depending on how the sidebar was opened. The layered window keeps one consistent look in both modes (strip mean identical active/inactive).
   - It needs no Windows 11-only APIs, so the Windows 10 path is the same code.
   - Trade-off accepted: no background blur; WPF re-uploads the layered bitmap when content changes (only during animations/hover).
2. **Fades = `Window.Opacity` + per-frame DWM thumbnail opacity in lockstep.** DWM thumbnails ignore WPF window opacity (follow-up §E) and WPF refuses `WS_EX_LAYERED`/`SetLayeredWindowAttributes` on non-transparent windows (error 87, follow-up §D). Per-frame updates cost ≈0.05 ms per thumbnail (8 thumbnails: avg 0.41 ms, max 0.55 ms; real fade ran at 24 frames in 360 ms ≈ 66 fps).
3. **Minimized windows show DWM's retained last frame plus a "Minimized" badge.** DWM returned the last rendered image both for windows minimized after registration and for windows already minimized before (follow-up §B; real minimized Claude window drew 100 %). It is not live (content changed while minimized was not shown), so it is labeled. An icon placeholder is rendered *underneath* the thumbnail so it shows through if DWM draws nothing.
4. **Use the whole window (`fSourceClientAreaOnly = false`) and query the source size after setting properties.** `DwmQueryThumbnailSourceSize` reflects the client-only flag (466×313 whole window vs 464×281 client area for the same window).
5. **Dead-window detection:** after the source window is destroyed the thumbnail disappears and `DwmUpdateThumbnailProperties`/`DwmQueryThumbnailSourceSize` return `E_INVALIDARG (0x80070057)`. WinEvent `EVENT_OBJECT_DESTROY` stays the primary signal; the HRESULT is a backstop. Unregistering a dead relationship also returns `E_INVALIDARG` and is harmless.
6. **Scroll clipping is done by EdgeStage** (`ThumbnailGeometry.Compute` crops `rcDestination` and `rcSource` together). Verified: no thumbnail pixels outside the viewport; a half-scrolled card showed `src [0,183 466x130]`.
7. **P/Invoke via `LibraryImport` with `UnmanagedCallersOnly` callbacks works** for `EnumWindows`; the same pattern will be used for WinEvent and low-level mouse hooks.

## Observations to carry forward

- `CompositionTarget.Rendering` fires ≈34×/s when nothing is changing but ≈66×/s during an animation; animations should be driven per frame, not by a fixed timer.
- `SetForegroundWindow` only succeeds while EdgeStage holds foreground rights (follow-up §C shows `Activate()` failing in a script-launched run). In the product those rights come from the hotkey (`WM_HOTKEY`) or from the user's click on the sidebar; failures are reported, never worked around.

## Not covered by the spike (status: NOT RUN)

| Case | Why | Plan |
|---|---|---|
| Real mouse click on a card → activation | Desktop-control permission declined this session | Phase 5 E2E (ask again) or user manual check |
| Elevated (administrator) source window, e.g. Task Manager | Launching it may raise UAC and needs the user | Phase 5 / user manual check; documented as a limitation if blocked |
| DRM-protected content | No protected source available | Documented: such content renders black by design |
| Window on another virtual desktop (cloaked) | Would change the user's desktop layout | Covered by unit-tested filter (`DWMWA_CLOAKED`); manual check optional |
| Other DPI scales / multiple monitors | Only one 100 % monitor attached | Geometry unit-tested at 100–200 %; hardware check needs the user |
