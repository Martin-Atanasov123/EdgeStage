# Changelog

All notable changes to EdgeStage. Versions follow [Semantic Versioning](https://semver.org/).

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
