# Changelog

All notable changes to EdgeStage. Versions follow [Semantic Versioning](https://semver.org/).

## [2.0.0] - 2026-10-11

### Added
- **Exposé**: every window at once in an adaptive grid on a dimmed full-screen overlay
  (default shortcut **Ctrl+Alt+Up**, also from the sidebar's header button, the tray, or
  `EdgeStage.exe --expose`). Previews fly out of the windows' real positions into the grid and back;
  the chosen window comes forward under the overlay. Arrow keys move spatially, Enter switches,
  Esc returns to the window you were using. Scrolls when there are too many windows.
- **Search in both views**: start typing; matches title and app name, ignoring case. Esc clears the
  search first. A count shows how many windows match.
- **Group by app** (optional) in the sidebar and in Exposé, with collapsible group headers.
- **Themes**: Dark (the 1.x look), Light, or System (follows Windows, live).
- **Accent colors**: the Windows accent, nine curated colors, or any custom `#RRGGBB`.
- **Layout presets** (Compact, Balanced, Spacious), card size, card spacing, sidebar width and
  corner radius.
- **Motion settings**: animations on/off, animation duration (50–200 %), reduce motion (fades only).
- **Edge over full-screen apps** (optional; off by default).
- **Ctrl+W** closes the selected window; if it is still open after 3 seconds (for example a save
  prompt), its card says so.
- Tray: *Open Exposé*, *Hidden apps…*, and shortcut hints next to the entries.
- *Show all apps again* resets the hidden-apps list; *Reset to defaults* (with confirmation) in
  Settings › General keeps hidden apps.

### Changed
- New settings window with pages: Appearance, Sidebar, Exposé, Hidden apps, General.
- Settings file schema 2 (sections `sidebar`, `expose`, `appearance`). A 1.x file is converted
  automatically on first start; the original is kept as `settings.v1.json`. Your shortcut and every
  other 1.x value are kept as they were.
- Hover selects a card only after the pointer moves, so a list appearing under a resting pointer no
  longer changes the keyboard selection.
- The sidebar has a header with the search box and an Exposé button.

## [1.1.0] - 2026-10-11

### Added
- **Hidden apps**: right-click a card → "Never show *app* in EdgeStage", or manage the list in Settings.
- **Close button** on the card under the pointer, and "Close window" in the card's context menu.
- **Type-to-filter**: type while the sidebar has focus to filter cards by title or app name.
- **Hold to switch** (optional): Alt+Tab-style stepping while the shortcut's modifiers are held.
- **This monitor only** (optional): list only windows on the sidebar's monitor.
- **Zoom-to-window transition**: the preview grows from the card to the window (about 200 ms).
- **Rounded preview corners** (a click-through corner-mask window owned by the sidebar).
- Reactive card borders: current window, hover and selection each get their own color.

### Changed
- Cards are tighter, and the app icon, title and badges are shown above the live preview.
- The settings window scrolls on small screens.

## [1.0.0] - 2026-10-10

First release: live DWM previews in a floating sidebar, global shortcut and screen-edge activation,
MRU ordering, keyboard navigation, multi-monitor and per-monitor DPI support, tray icon and settings
window, start with Windows, local logging.
