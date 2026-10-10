# Spike real-window probe (WpfAllowsTransparency sidebar)

- Generated: 2026-10-10 21:22:36

## 1. Thumbnails of the windows currently open

| # | Process | Window class | Minimized | Registered | In viewport | Content drawn (non-placeholder fraction) |
|---|---|---|---|---|---|---|
| 1 | ThumbnailSpike — "EdgeStage Spike Flip" | HwndWrapper[ThumbnailSpike;;e327107f-113b-4bb6-a1cc-9453f332ad51] | False | True | True | YES (100%) |
| 2 | ThumbnailSpike — "EdgeStage Spike Minimized" | HwndWrapper[ThumbnailSpike;;7e0c799f-ecd8-422b-8e8b-9ce37b875ed7] | True | True | True | YES (100%) |
| 3 | ApplicationFrameHost | ApplicationFrameWindow | False | True | True | YES (100%) |
| 4 | mspaint | MSPaintApp | False | True | True | YES (100%) |
| 5 | Notepad | Notepad | False | True | True | YES (100%) |
| 6 | claude | Chrome_WidgetWin_1 | True | True | True | YES (100%) |

Registered 6 of 6. Titles omitted for privacy except EdgeStage's own test windows.

## 2. Live updates from another process (source flips magenta↔cyan every 400 ms)

- Samples every 50 ms for 4 s, collapsed sequence: `C?MCMCMCMCMC` → 10 color changes observed (≈10 expected).

## 3. Scroll clipping (DWM thumbnails are not clipped by WPF; EdgeStage clips them itself)

- Scrolled 90 DIP. Visible thumbnails all inside viewport [75,1008] px: True.
- Thumbnails cropped at the top edge (rcSource.Top > 0): 1 dest [35,75 193x54] src [0,183 466x130]
- 5-px band just above the viewport: mean before #202020, after #202020 (unchanged ⇒ no thumbnail bleeding outside the list).

## 4. 360 ms fade (thumbnail opacity + WPF content opacity per frame)

- 25 frames; per-frame thumbnail update cost avg 0.39 ms, max 0.48 ms.

## 5. 50 full rebuilds (unregister all → enumerate → register all)

- Before: handles 616, GDI 18, USER 26, private 59.7 MB
- After:  handles 617, GDI 18, USER 25, private 68.7 MB

