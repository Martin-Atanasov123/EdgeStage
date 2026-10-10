# Spike follow-up experiments

- Generated: 2026-10-10 21:12:56

## A. WPF render-frame rate on this machine (CompositionTarget.Rendering, idle)
- 34 frames/s over 2 s.

## B. Thumbnail registered for a window that was already minimized
- Register HR 0x00000000, update HR 0x00000000, source size HR 0x00000000 → 466x313.
- Thumbnail center: cyan 100%, mean #00FFFF (placeholder #303030 means nothing drawn).

## C. Backdrop when the host is NOT the active window (edge-trigger mode)
- AcrylicNoFrame: not activated (foreground=False) strip mean #545454; after Activate (foreground=False) strip mean #545454.
- AcrylicChrome: not activated (foreground=False) strip mean #545454; after Activate (foreground=False) strip mean #545454.
- WpfAllowsTransparency: not activated (foreground=False) strip mean #3B3B1C; after Activate (foreground=False) strip mean #3B3B1C.

## D. WS_EX_LAYERED + LWA_ALPHA on a WPF window
- Native setup: darkmode=0x00000000, corner=0x00000000, layeredAlpha=False (err 87, setLong err 0, exstyle 0x8)
- Runtime SetLayeredWindowAttributes(alpha 100) = False (err 87); thumbnail mean #FF00FF, host background strip mean #202020.

## E. WPF AllowsTransparency window with Window.Opacity = 0.4 (does the DWM thumbnail fade with it?)
- Thumbnail mean with Window.Opacity 0.4: #FF00FF (#FF00FF = thumbnail ignores window opacity).

