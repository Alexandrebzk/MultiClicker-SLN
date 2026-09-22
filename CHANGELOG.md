# Changelog

All notable changes to MultiClicker are documented here.
This project follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.4.0] - 2026-09-22

The input pipeline, the OCR stack and the packaging were all reworked in this
release. Upgrading is recommended for anyone on Dofus 3.x: broadcast clicks and
the HDV price filler are both fixed here.

### Added
- Self-contained single-file distribution: the release zip runs on a clean
  Windows 10/11 machine with no .NET install (`publish.ps1`).
- The release zip now ships `prerequisites\vc_redist.x64.exe` and a
  `READ-ME-FIRST.txt` quick-start alongside the EXE, `tessdata\` and
  `cosmetics\`.
- `ImagePickerDialog` for choosing panel backgrounds.
- Debug helper `DumpInputState()` and stopwatch instrumentation that warns when
  a hook callback exceeds 50 ms.

### Changed
- **Migrated to .NET 8 (`net8.0-windows`)**, SDK-style project. `App.config`,
  `AssemblyInfo.cs` and `packages.config` are gone.
- **Triggers now fire on release, not on press.** A trigger arms on the
  key/button down edge and executes on the matching up edge. Windows refuses
  `SetForegroundWindow` while input is captured, so a held button made the first
  window of a broadcast sweep keep the previous window's focus and lose its
  click. A reconcile safety net fires any arm whose key the OS reports as up, so
  an action is never silently dropped.
- Foreground switching attaches input queues via `AttachThreadInput` before
  `BringWindowToTop` / `SetForegroundWindow` / `SetActiveWindow` / `SetFocus`.
  A window that refuses the switch is retried up to 3x, then skipped with a
  trace instead of being clicked blindly.
- `PreferBackgroundClicks` now defaults to `false`: Dofus 3.x (Unity) ignores
  synthesized window messages, so the `SendInput` path is the reliable default.
- Clicks are anchored as client coordinates of the source window and remapped
  per target via `ClientToScreen` - correct for unmaximized windows and
  multi-monitor setups.
- All trigger actions run on a single dispatcher thread, so two triggers can no
  longer interleave their injected input.
- Keybinds are canonicalized on load: mouse buttons stored as `Keys` values by
  older configs are folded into the mouse-button flags.
- HDV fill latency cut from roughly 1.3 s to ~0.4 s end to end (trigger settle
  500 ms -> 80 ms, tighter keystroke spacing).
- Services (Configuration, Hook, OCR, Panel, Window) and Core
  (ApplicationManager, EventHandler) refactored; EN/FR/ES strings updated.

### Fixed
- **Self-retrigger loop**: the app's own `SendInput` / `keybd_event` calls were
  re-entering the low-level hooks. Injected events are now detected via
  `LLKHF_INJECTED` / `LLMHF_INJECTED` and ignored.
- **HDV price was recognized but never typed.** `SendKeys.SendWait` injects
  virtual keys with scan code 0, which the Unity client discards. Every
  `SendInput` path now fills `wScan` from `MapVirtualKey` and sets
  `KEYEVENTF_EXTENDEDKEY` for extended keys. Characters are typed via
  `VkKeyScan`, so an AZERTY layout types digits rather than `&é"'(-è_çà`.
- **Greyed-out sell quantities are now readable.** Added a faint-text rescue
  tier (median denoise, contrast stretch, polarity-aware binarization, adaptive
  local-mean thresholding, stroke thickening) that only runs when the fast path
  fails - a well-contrasted read still completes in 1-6 ms.
- **Wrong price could be typed into the game.** The multi-pass OCR vote counted
  agreeing passes, letting two zero-confidence passes outvote one solid read.
  Candidates are now weighted by summed confidence and zero-confidence passes
  do not vote. Measured on synthetic panel crops: 63/71 correct with 1 wrong
  value -> 65/71 correct with 0 wrong values; remaining misses abort and log
  instead of returning a garbage number.
- Stuck input state: key/mouse state is updated on UP events regardless of
  foreground, so a focus change can no longer leave a key latched. A 500 ms
  reconciliation pass prunes drifted state via `GetAsyncKeyState` (prune-only -
  it never resurrects phantom modifiers).
- Left/right modifier variants are tracked independently
  (`VK_LCONTROL` / `VK_RCONTROL`, etc.) and `WM_SYSKEYDOWN` / `WM_SYSKEYUP` are
  handled, so Alt combinations work.
- The keybinds dialog no longer installs its own low-level hook; triggers are
  suspended while it captures input.
- `HookManagementService.Shutdown()` is wired into `Program.Cleanup`.
- Cursor position is restored after a foreground sweep.
- Wrong click behaviour introduced by Dofus 3.5.

## [1.3] - 2026-03-17
- Re-enabled the click mechanism after the Dofus 3.5 update.

## [1.2] - 2025-08-20
- Added the auto-follow feature.

## [1.1] - 2025-08-01
- README update and assorted bug fixes.

## [1.0] - 2025-07-21
- First stable release.

## [0.0.1] - 2024-06-14
- Initial pre-release.

[1.4.0]: https://github.com/Alexandrebzk/MultiClicker-SLN/releases/tag/1.4.0
[1.3]: https://github.com/Alexandrebzk/MultiClicker-SLN/releases/tag/1.3
[1.2]: https://github.com/Alexandrebzk/MultiClicker-SLN/releases/tag/1.2
[1.1]: https://github.com/Alexandrebzk/MultiClicker-SLN/releases/tag/1.1
[1.0]: https://github.com/Alexandrebzk/MultiClicker-SLN/releases/tag/1.0
[0.0.1]: https://github.com/Alexandrebzk/MultiClicker-SLN/releases/tag/0.0.1
