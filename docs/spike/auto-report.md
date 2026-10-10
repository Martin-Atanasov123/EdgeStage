# Spike auto-experiment report

- Generated: 2026-10-10 21:10:36
- OS: Microsoft Windows NT 10.0.26300.0 (Microsoft Windows 10.0.26300)
- Runtime: .NET 10.0.8, x64 process
- Primary work area (DIP): 0,0,1920,1032
- Machine: (redacted), 12 logical CPUs

- Display scale of source window: 100%

## 1. Render + live update per destination-window configuration

Thumbnail of a solid-magenta window registered into each host; probe = central 60 % of the destination rectangle.
Live latency = time from changing the source color to the color appearing (a) in the source window itself and (b) in the thumbnail. Probe resolution ≈ 1–3 ms.

| Config | Native setup | Register HR | Source size | Update HR | Thumbnail visible (frac magenta) | Edge alignment | Backdrop strip mean (yellow behind) | Opacity 128 mean | Latency src median/p95 ms | Latency thumb median/p95 ms |
|---|---|---|---|---|---|---|---|---|---|---|
| Solid | darkmode=0x00000000, corner=0x00000000 | 0x00000000 | 466x313 | 0x00000000 | YES (100%, mean #FF00FF) | exact (±1px) | #202020 | #981898 | 14 / 22 | 28 / 36 |
| AcrylicNoFrame | darkmode=0x00000000, corner=0x00000000, extendFrame=0x00000000, backdrop=0x00000000 | 0x00000000 | 466x313 | 0x00000000 | YES (100%, mean #FF00FF) | exact (±1px) | #545454 | #981898 | 15 / 23 | 30 / 35 |
| AcrylicChrome | darkmode=0x00000000, corner=0x00000000, extendFrame=0x00000000, backdrop=0x00000000 | 0x00000000 | 466x313 | 0x00000000 | YES (100%, mean #FF00FF) | exact (±1px) | #968904 | #981898 | 19 / 23 | 30 / 40 |
| WpfAllowsTransparency | darkmode=0x00000000, corner=0x00000000 | 0x00000000 | 466x313 | 0x00000000 | YES (100%, mean #FF00FF) | exact (±1px) | #3B3B1C | #981898 | 12 / 24 | 28 / 35 |
| LayeredAlpha | darkmode=0x00000000, corner=0x00000000, layeredAlpha=False | 0x00000000 | 466x313 | 0x00000000 | YES (100%, mean #FF00FF) | exact (±1px) | #202020 | #981898 | 16 / 20 | 30 / 35 |

> LayeredAlpha whole-window alpha 128 → thumbnail region mean #FF00FF (frac magenta 100%); back to 255 → #FF00FF (100%).


## 2. Minimized source window

- Before minimize: source size 464x281, thumbnail magenta 100%.
- Minimized: DwmQueryThumbnailSourceSize HR 0x00000000 → 464x281; update HR 0x00000000; thumbnail center magenta 100% (mean #FF00FF); full dest-rect mean #FF00FF.
- Color changed to cyan while minimized: thumbnail center cyan 0% (mean #FF00FF).
- After restore: source size 464x281; thumbnail center cyan 100% (mean #00FFFF).

## 3. Per-frame thumbnail updates (animation feasibility)

- 8 thumbnails updated (rect + opacity) on every WPF render frame for 1 s: 35 frames, 0 failed calls, cost per frame for all 8: avg 0.406 ms, max 0.554 ms.

## 4. Native resource release (leak test)

- 1,000 × (register → query size → update → dispose): 0 failed calls, 0 failed releases, avg 0.165 ms per cycle.
  - Before: handles 586, GDI 21, USER 28, private 104.2 MB
  - After:  handles 586, GDI 21, USER 28, private 99.4 MB
- 100 × (register 12 → update → dispose all):
  - Before: handles 586, GDI 21, USER 28, private 95.6 MB
  - After:  handles 586, GDI 21, USER 28, private 87.6 MB

## 5. Destination window destroyed while thumbnails are registered

- Update after destination destroyed: 0x80070057
- Release after destination destroyed: 0x80070057 (no exception, no crash)

## 6. Source window destroyed while its thumbnail is shown

- While alive: cyan 100%. After source closed: cyan 0% (mean #303030).
- Update HR 0x80070057, query-size HR 0x80070057 (0x0), release HR 0x80070057.

## 7. Activation of an out-of-process window

- Spike host was foreground before the test: True (process launched from a script, so foreground rights are not guaranteed).
- Normal window: SetForegroundWindow returned True; target became foreground: True.
- Minimized window (iconic=True): SW_RESTORE + SetForegroundWindow returned True; target restored and foreground: True.

