# EdgeStage — test report

Status words: **PASS** = executed and met the expectation · **PARTIAL** = the logic was executed
automatically but the real user-input path was not · **NOT RUN** = not executed (reason given).
Nothing below is marked PASS unless it was actually run. The 2.0 section comes first; the 1.x
report follows unchanged as the baseline.

## EdgeStage 2.0

Environment as in 1.x below, except: a **second 1920×1080 monitor** (100 %, 60 Hz) is attached to
the left of the primary one, 4 px lower (`[-1920,4 1920x1080]`). Final runs: 2026-10-12.

Rules for every run: no synthesized input; screenshots only of EdgeStage and synthetic windows over
opaque backdrops covering **every** monitor (`EDGESTAGE_TEST_ONLY_PROCESS` limits the views to the
synthetic windows); the scripts never print other windows' titles, back up and restore the user's
settings file, ask the user's running EdgeStage to exit normally and restart it afterwards, run with
the real edge trigger off (a pointer resting at a screen edge must not open the sidebar mid-check; the
edge mode is exercised through a DEBUG hook), and refuse to start while a full-screen app is in front
(`tools/validation/guard.ps1`). Twice the guard stopped the runs because a full-screen game was
running; they were resumed only after the user closed it.

### Automated tests — 256 PASS, 1 inconclusive (257 total)

```bash
dotnet test EdgeStage.slnx -c Release
```

| Run | Result |
|---|---|
| Full suite incl. Desktop integration, Release, final code | **256 passed**, 0 failed, 1 skipped/inconclusive (the activation test that needs foreground rights a test runner does not get — same as 1.x) |
| Unit tests only (`--filter "TestCategory!=Desktop"`) | **219 passed**, 0 failed |
| Debug and Release builds | 0 warnings, 0 errors |

New in 2.0: Exposé grid layout 14 (+data rows) · grid arrow navigation 5 · display-to-display
navigation 3 · grouping 7 · settings schema 2 / migration 10 (+data rows; one uses the **real 1.x
settings file** of the development machine as a fixture) · themes 5 (incl. "every brush used by the
views exists in both themes") · reset to defaults · tray menu Exposé/hidden-apps entries · settings
window (Desktop): pages, theme/accent/custom color (invalid input not applied), layout preset moves the
spacing slider, Exposé display mode, Exposé shortcut duplicate/conflict states and suggestion, Reset
needs confirmation and keeps hidden apps.

**Found and fixed during these runs:** the two shortcut-recorder tests had become inconclusive because
the recorder now sits on the Sidebar page, which is hidden when the window opens; the tests now open
that page first (they pass).

### Scripted end-to-end checks (Debug build, synthetic windows)

`tools/validation/e2e-expose.ps1` — Exposé, two monitors:

| Check | Result |
|---|---|
| **Ctrl+Alt+Up** is held by EdgeStage while it runs (another registration attempt fails) and free again after exit | **PASS** |
| Exposé opens on **both** displays (default *Every display*): primary lists its 5 windows, the left display lists the window that is on it | **PASS** |
| Fly-in from the real window positions into a centered grid; minimized window fades in | **PASS** (frame captures) |
| Animation smoothness (slowed ×3.3 for capture): 123–149 frames per 1000 ms open, 70–108 frames per ≈ 775 ms close, both overlays animating together | **PASS** (renders at the display's refresh rate) |
| Hit-testing at a card (Windows' own `WindowFromPoint`, on the overlays' thread) finds the overlay, not the full-screen corner mask, on both displays — clicks reach the cards | **PASS** |
| Arrow navigation within a grid; ← at the edge of the primary grid continues on the left display, whose window is then chosen with Enter and becomes the foreground window | **PASS** |
| A window opened / closed while Exposé is open appears / disappears | **PASS** |
| Search "REPORT" filters both displays (1 of 5 on the primary, "No matching windows" on the other); first Esc clears it and Exposé stays open; only the display with the keyboard keeps a selection | **PASS** |
| Group by app → header row, grid below it | **PASS** |
| Select a card → that window becomes the foreground window; Esc → back to the window that was active before | **PASS** |
| **Ctrl+W** on a window that refuses to close: after 3 s its card says "Still open — the app may be asking something" | **PASS** |
| Exposé button in the sidebar's header → sidebar closes, Exposé opens | **PASS** |
| Clean exit, 0 DWM registrations left | **PASS** |

`tools/validation/e2e-synthetic.ps1` — sidebar regression: open with live previews, window
added/removed while open, activation of the selected card, edge-style open keeps the previous window
in the foreground and closes when the pointer is elsewhere, settings window, tray menu (now with
*Open Exposé  Ctrl+Alt+Up* and *Hidden apps…*), clean exit — **PASS**.

`tools/validation/e2e-appearance.ps1` — **PASS** (reviewed captures): all five settings pages in the
Light theme with the Purple accent and in the Dark theme with the Windows accent; sidebar and Exposé
in the Light theme (light cards, purple selection ring); light title bar in Light. The settings window
had first been captured *under* the topmost backdrops (test setup only); the DEBUG hook now puts it on
top for captures.

A real 1.x settings file was migrated on start (log), and restored by the scripts afterwards — **PASS**.

### Leak cycles — `tools/validation/leak-cycles.ps1` (Debug build) — PASS

100 sidebar open/close cycles, 50 Exposé open/close cycles (two overlays each), 30 windows opened and
closed while hidden:

| Stage | Handles | GDI | USER | Private MB | Threads |
|---|---|---|---|---|---|
| after warm-up | 690 | 100 | 62 | 157.2 | 23 |
| after 100 sidebar cycles | 669 | 67 | 44 | 178.8 | 18 |
| after 50 Exposé cycles | 674 | 100 | 62 | 170.6 | 18 |
| after 30 windows opened + closed while hidden | 675 | 76 | 45 | 170.1 | 20 |
| 20 s after the last use (idle overlays released) | **667** | **76** | **41** | **158.0** | 15 |

Log: sidebar opened 104 / dismissed 104, Exposé 106 overlay opens (53 sessions), 0 errors, 0 DWM
registrations at exit. GDI/USER alternate between two fixed levels (overlays alive vs. released).

### Resource measurements — `tools/validation/measure-resources.ps1` (Release, self-contained)

| State | CPU | Working set | Private | Handles / GDI / USER |
|---|---|---|---|---|
| Idle, hidden, edge polling on, 60 s | 78 ms = 0.13 % of one core | 98 MB | 64 MB | 652 / 53 / 30 |
| Sidebar open and idle, 15 s | 63 ms = 0.42 % of one core | 136 MB | 93 MB | 703 / 59 / 49 |
| Exposé open on both displays and idle, 15 s | 15 ms = 0.10 % of one core | 144 MB | 142 MB | 728 / 100 / 66 |
| Hidden again, 20 s after Exposé closed | 63 ms | 131 MB | 131 MB | 705 / 76 / 44 |

**Found and fixed during these measurements:** the first measurement kept **~120 MB** after Exposé
closed (private 186 MB). Cause: full-screen *layered* (per-pixel transparent) windows — the overlays
and their corner masks — have every frame copied into screen-sized system-memory bitmaps, and that
memory stayed with the process. Exposé's overlays and masks are now non-layered "glass" windows
(DWM composites WPF's transparent pixels directly), and idle overlays are destroyed 15 s after
Exposé closes. Result: 142 MB while open (was 194), 131 MB afterwards (was 186). The remaining
≈ 38 MB above "after the sidebar" is a one-time cost of the first Exposé use (not growing — see the
leak cycles). As a side effect the overlays animate at the display's refresh rate.

Single-run measurements; CPU figures of a few tens of ms vary between runs (one 60 s idle run read
47 ms, another 188 ms).

### Release builds

`dotnet publish` with both profiles (to `artifacts/v2/...`): self-contained **125.1 MB**,
framework-dependent **0.68 MB**.

### Manual checklist additions for 2.0 (needs a person)

16. Press **Ctrl+Alt+Up** → Exposé opens on every monitor, previews fly out of the windows; arrows move (← → cross to the other monitor); Enter switches; Esc goes back; pressing Ctrl+Alt+Up again closes.
17. In Exposé: click a card (also on the other monitor) → that window comes forward; click the dimmed background → closes; scroll with the wheel when there are many windows; hover a card and click its ×.
18. Type in Exposé and in the sidebar → search; Backspace edits; Esc clears, then closes.
19. Settings › Appearance: switch Dark / Light / System, accents, a custom `#RRGGBB`, the layout presets and sliders → open sidebar and Exposé update live.
20. Settings › Exposé: record a new shortcut; try one that equals the sidebar shortcut → refused with an explanation. Try *This display* and *All on one*.
21. Settings › General › Reset… → Cancel does nothing; Reset restores defaults and keeps hidden apps.
22. Turn on *Reduce motion* → sidebar fades without sliding; no zoom; Exposé fades instead of flying. Turn *Animations* off → views appear at once.

---

## EdgeStage 1.x (baseline)

### Environment

| | |
|---|---|
| OS | Windows 11 Pro, build 26300 (Insider channel), 64-bit |
| Hardware | AMD Ryzen 5 5600X (6C/12T), 16 GB RAM, NVIDIA GeForce GTX 1650 |
| Display | one 1920×1080 monitor, 60 Hz, 100 % scaling |
| .NET | SDK 10.0.204, runtime 10.0.8 (WindowsDesktop) |
| Tests | MSTest.Sdk 4.5.1 |
| Date | 2026-10-10 |

**Desktop control:** the automated validation never moved the mouse, typed, or synthesized input.
Permission for desktop control was requested twice (Phase 1 and Phase 5) and declined, so every
check that needs a real keyboard or mouse is marked PARTIAL or NOT RUN, and listed in the
[manual checklist](#manual-checklist-needs-a-person).

**Privacy:** screenshots were taken only of EdgeStage's own windows while a DEBUG-only filter
(`EDGESTAGE_TEST_ONLY_PROCESS`) limited the sidebar to synthetic test windows. Checks against the
user's real windows recorded aggregate pixel statistics only.

### 1. Automated tests — 200 PASS, 1 inconclusive (201 total)

```bash
dotnet test EdgeStage.slnx -c Release
```

Result (Debug and Release): **200 passed, 0 failed, 1 skipped/inconclusive** (the activation test whose final step needs foreground rights that Windows does not give a non-interactive runner).

| Group | Tests | What they cover |
|---|---|---|
| Unit (169) | WindowFilter 16 · MruOrder 7 · Hotkey 6 (+data rows) · EdgeTriggerStateMachine 12 · MonitorLayout 10 · SidebarPlacement 8 · ThumbnailGeometry 10 · SettingsStore 10 · StartupRegistration 8 · SidebarViewModel 12 · Fullscreen/Log 7 · ICO + native struct layout 5 · smoke 1 | Alt+Tab filtering rules (never titles), MRU ordering and the "switch back" pre-selection, hotkey parsing/formatting, edge dwell/re-arm/button-held/full-screen rules, outer-edge zones with negative coordinates and monitor seams, placement and keep-open region at 100–200 % DPI, aspect-fit and scroll clipping, settings defaults/clamping/corruption/forward compatibility/atomic save, Start-with-Windows states incl. Task Manager "disabled", card list behaviour, icon sizes, NOTIFYICONDATAW = 976 bytes |
| Desktop integration (32) | `TestCategory=Desktop` | Real windows created by the test process: enumeration excludes tool windows/owned dialogs/own process, minimized state; DWM register/apply/release, dead source detected, 300 registrations leave no live handles; hotkey conflict (Win32 error 1409) and release; **activation restores a minimized window and brings it to the foreground**; window gone; WinEvent show/title/destroy and stop-after-end; monitor layout; icon extraction; edge polling stops when disabled/paused; tray menu built for real and every item clicked (programmatically) against a fake host; settings window: load, toggles, debounced sliders, conflict suggestion, recorder suspends/resumes the global hotkey; Start with Windows against the real registry in a scratch key |

### 2. Phase 1 spike — DWM thumbnails (PASS)

Details and raw reports: [spike-findings.md](spike-findings.md).

| Check | Result |
|---|---|
| Thumbnail renders inside a WPF window, exact placement | PASS — all 5 window configurations, ±1 px |
| Live updates | PASS — ≈1 frame behind the source window; 10/10 changes seen from another process |
| Activation of the previewed window | PASS (programmatic, with foreground rights) |
| Resource release | PASS — 0 handle/GDI/USER growth over 2,500+ registrations; release is safe after source or destination is destroyed |
| Real windows (Notepad, Paint, UWP Calculator, minimized windows) | PASS — 6/6 drew content |

### 3. Scripted end-to-end checks (real EdgeStage, synthetic windows)

Scripts: `tools\validation\e2e-synthetic.ps1`, `tools\validation\e2e-titles.ps1` (Debug build; they
use DEBUG-only hooks to open the sidebar and to trigger "activate selected card" without input).

| Check | Status | Evidence |
|---|---|---|
| Sidebar opens with live previews of all listed windows | PASS | log "4 windows, 4 live thumbnails"; screenshot `docs/images/sidebar.png` |
| Preview content updates while open | PASS | consecutive frames 300 ms apart show the source color change |
| Window opened while sidebar open → card appended, panel grows | PASS | panel 774 → 964 px (5 × 190 + 14) |
| Window closed while open → card removed, selection kept | PASS | panel back to 774 px; same card still selected |
| Activation of the selected (minimized) card | PASS | before: minimized=True; after: foreground = that window, not minimized, sidebar hidden ("window selected") |
| Edge-mode open does not steal focus | PASS | foreground window unchanged after opening |
| Esc reserved while edge-opened sidebar visible | PASS | log "Esc reserved…" (pressing Esc itself: NOT RUN) |
| Pointer-leave dismissal | PASS (incidental) | with the real cursor outside the sidebar column, it closed after ≈490 ms (400 ms setting) |
| Show/hide animation timing | PASS | show 181–195 ms (13–14 frames), hide 140–156 ms (9–12 frames) |
| Long title | PASS | trimmed with ellipsis, full title in tooltip |
| Changing title | PASS | card label changed between frames ("step 13" → "step 18") |
| Window without icon | PASS | generic glyph shown |
| Settings window, conflict notice, tray menu rendering | PASS | screenshots in `docs/images/` (tray menu opened by a DEBUG hook at a fixed position) |
| Zoom transition runs to completion, hides itself, releases its thumbnail | PASS | integration test `ZoomOverlayTests` (≥250 ms; 20 frames / 286 ms grow and 9 frames / 123 ms fade measured in the app) |
| Zoom ends with the target window in front | NOT RUN | in the scripted runs Windows refused foreground activation (the user was working in another app, no real input); the refusal path (tray notice, overlay still hides) was exercised |
| Hidden apps: normalization, case-insensitive match, cap/dedupe, JSON round-trip, old settings files | PASS | `ExcludedAppsTests` (unit) |
| Type-to-filter: title/app-name match, selection kept or moved, order restored, empty state, window opened/closed while filtered | PASS | `TypeToFilterTests` (unit) |
| Hold-to-switch selection wraps around both ends | PASS | `CycleSelection_WrapsAroundBothEnds`; the modifier-release watcher itself is NOT RUN (needs real keys) |
| Settings window: add/remove hidden apps, behavior toggles | PASS | `SettingsUiTests` against a fake host |
| Close button, context menu, typing into the sidebar, "this monitor only" | NOT RUN | need real mouse/keyboard input or a second monitor; the code paths build and are covered only indirectly |
| Exit via `--exit` while sidebar open | PASS | exit code 0, "live DWM thumbnail registrations: 0" |
| Second launch shows the running instance's sidebar | PASS | log "Sidebar opened" after relaunch with `--show`; single process |
| Published self-contained exe starts, shows, exits | PASS | smoke run of `artifacts\SelfContained\EdgeStage.exe` |

### 4. Specification edge cases (§10)

| Case | Status | Notes |
|---|---|---|
| Opening/closing apps while the sidebar is active | PASS | §3 add/remove; WinEvent integration test |
| Multiple windows from the same application | PASS | windows of the same executable (separate processes) listed as separate cards; nothing in the filter groups by process, and the activation test targets one specific window handle |
| Minimized windows | PASS | DWM retained frame + "Minimized" badge; restore + activate verified |
| Long or changing titles | PASS | §3 |
| Apps without a usable icon | PASS | generic glyph |
| Windows opening and closing rapidly | PASS | 30 + 10 windows opened/closed while hidden; add/remove while open; no errors |
| Full-screen applications | PARTIAL | detection rule unit-tested (incl. maximized + auto-hide taskbar); no real full-screen app was run |
| Protected/unavailable content | NOT RUN | no DRM source available; documented as a Windows limitation |
| Edge trigger while moving/resizing windows | PARTIAL | "button held → suppressed until the pointer leaves" unit-tested; no real drag performed |
| Shortcut registration conflicts | PASS | real conflict on this machine (Ctrl+Alt+Space, error 1409): non-modal notice, settings warning, free alternative suggested (Alt+`) |
| Locking and unlocking Windows | NOT RUN | implemented (hide + pause on lock, refresh + retry shortcut on unlock); locking would require re-entering the user's password |
| Different display scaling | NOT RUN on hardware | placement/geometry unit-tested at 125–200 %; only a 100 % monitor available and scaling was not changed |
| Closing EdgeStage while thumbnails are registered | PASS | exit with the sidebar open → 0 live registrations |
| Multiple monitors | NOT RUN on hardware | layout/seam/negative-coordinate logic unit-tested; one monitor attached |
| Elevated (administrator) windows | NOT RUN | would need a UAC prompt; behaviour documented |

### 5. Resource measurements

Script: `tools\validation\measure-resources.ps1`; leak cycles: `tools\validation\leak-cycles.ps1`.

| Build / state | Duration | CPU | Working set | Private | Handles | GDI | USER | Threads |
|---|---|---|---|---|---|---|---|---|
| Release self-contained (uncompressed), idle, sidebar hidden, edge polling on | 60 s | 62 ms = 0.103 % of one core, 0.0086 % of total CPU | 104.2 MB | 63.3 MB | 690 | 35 | 34 | 16 |
| Release framework-dependent, same | 60 s | 46 ms = 0.077 % of one core | 102.2 MB | 62.2 MB | 642 | 35 | 34 | 16 |
| Release, sidebar open and idle (real windows) | 15 s | 94 ms = 0.63 % of one core | 129–131 MB | 88.8 MB | ~700 | 35 | 40 | 20 |
| Self-contained **with** single-file compression (rejected), idle | 20 s | — | 196 MB | 119 MB | 695 | 35 | 34 | 19 |

Leak cycles (Debug build, DEBUG hook opens the sidebar exactly like the edge trigger; it closes
itself ~0.4 s later; real windows listed):

| After | Handles | GDI | USER | Private | Working set |
|---|---|---|---|---|---|
| warm-up | 654 | 35 | 35 | 93.7 MB | 134.9 MB |
| 50 cycles | 645 | 35 | 36 | 96.6 MB | 140.1 MB |
| 100 cycles | 638 | 35 | 36 | 98.3 MB | 142.1 MB |
| 150 cycles | 638 | 35 | 36 | 99.1 MB | 141.2 MB |
| 200 cycles | 642 | 35 | 37 | 99.7 MB | 141.7 MB |
| 250 cycles | 640 | 35 | 36 | 99.6 MB | 141.4 MB |
| + 10 windows opened/closed while hidden | 640 | 35 | 36 | 99.6 MB | 141.4 MB |

254 opens / 254 dismissals, 0 errors logged, 0 DWM registrations at exit. Native resources are flat;
managed memory plateaus after ~150 cycles (no leak).

### 6. Bugs found and fixed during validation

| Bug | Found by | Fix |
|---|---|---|
| Owned dialogs of WPF/WinForms apps leaked into the list | desktop integration test | root owner found by walking `GW_OWNER` (GA_ROOTOWNER follows GetParent, which misses owned overlapped windows) |
| Modal MessageBox for the shortcut conflict blocked shutdown | E2E (`--exit` did not finish) | non-modal notice window |
| Settings JSON escaped "+" as `+` | unit test | relaxed escaping for the local, hand-editable file |
| "Ctrl++" could not be parsed | unit test | "+" alias |
| Shortcut box silently restarted recording after the settings window was re-activated | settings UI test | focus parked on the window when recording ends |
| Icon resources resolved against the wrong assembly in non-EdgeStage hosts | settings UI test | assembly-qualified pack URIs |
| Single-file compression doubled idle memory | measurement | compression disabled for the self-contained build |

### Manual checklist (needs a person)

These need real input and were **NOT RUN**. With EdgeStage running (and, on this machine, a free
shortcut chosen first — Settings › Keyboard shortcut › *Use Alt+`*):

1. Press the shortcut → sidebar slides in on the monitor under the pointer; the previously used window is selected; ↑/↓ move the selection; Enter switches; Esc closes; pressing the shortcut again closes.
2. Rest the pointer at the left screen edge ~0.3 s → sidebar opens without taking focus (keep typing in an editor: text still goes there); click a card → that window comes forward and the sidebar hides; Esc closes it; moving the pointer away closes it after ~0.4 s.
3. Drag a window by its title bar to the left edge and hold → the sidebar must **not** open; release and move away, come back → it opens.
4. Play a full-screen video or game → resting at the edge does nothing; the shortcut still works (except exclusive full-screen games).
5. Click a minimized window's card → it restores and comes forward.
6. Open an administrator window (e.g. Task Manager) → check its preview and whether switching to it works; if refused, the card shows "Can't switch: runs as administrator".
7. Right-click the tray icon → menu opens and closes when clicking elsewhere; try each item; left-click the icon → sidebar opens.
8. Restart Explorer (Task Manager › Windows Explorer › Restart) → tray icon returns.
9. Lock (Win+L) and unlock → no sidebar left on screen; the edge and shortcut still work afterwards.
10. Change display scaling to 125 % / 150 % and, if available, use a second monitor (also one to the left of the primary) → sidebar placement, sharpness and edge zones.
11. In Settings, record a new shortcut with the keyboard; toggle *Start with Windows* and sign out/in.
12. Hover a card → a × appears; click it → the window closes (or asks to save) and its card disappears. Right-click a card → "Close window" and "Never show … in EdgeStage" both work.
13. Open the sidebar with the shortcut and type part of a window title → cards filter; Backspace edits; Esc clears the filter and a second Esc closes.
14. Turn on *Hold to switch* in Settings (with a shortcut that has a modifier): hold the modifiers, tap the key repeatedly → selection steps; release → that window comes forward. A quick tap switches to the previous window.
15. Hide an app in Settings → its windows vanish from the sidebar; *Show again* brings them back. With two monitors, turn on *Only windows on this monitor* → the sidebar lists only that screen's windows.
