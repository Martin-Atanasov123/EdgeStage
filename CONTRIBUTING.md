# Contributing

Thanks for your interest in EdgeStage.

## Build and test

Prerequisite: the .NET 10 SDK (see `global.json`) on Windows 10/11.

```bash
dotnet build EdgeStage.slnx -c Release
dotnet test EdgeStage.slnx -c Release --filter "TestCategory!=Desktop"
```

Tests tagged `Desktop` create real windows and register global shortcuts, so they need an
interactive desktop session and are not run in CI. Run them locally before sending changes that touch
window tracking, thumbnails, hotkeys or activation:

```bash
dotnet test EdgeStage.slnx -c Release --filter "TestCategory=Desktop"
```

## Layout

- `src/EdgeStage.Core` — everything that does not need WPF: Win32/DWM interop, window filtering and
  tracking, thumbnail geometry, hotkeys, edge-trigger logic, settings. Keep logic here and unit-tested.
- `src/EdgeStage` — the WPF app (sidebar, cards, settings, tray, controller).
- `tests/EdgeStage.Tests` — unit and desktop integration tests.
- `spike/ThumbnailSpike` — the Phase 1 proof of concept and measurement tools.
- `tools/validation` — scripts used to validate behavior with synthetic windows (see docs/TESTING.md).

## Guidelines

- Native interop stays in `EdgeStage.Core/Interop` and uses source-generated `LibraryImport`.
- Do not work around Windows' foreground-lock rules; report refusals instead.
- Never report a behavior as verified unless it was actually run; record unverified items in
  `docs/TESTING.md` as NOT RUN.
- Keep idle cost near zero: no polling or hooks while the sidebar is hidden beyond what exists today.
