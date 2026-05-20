# Multi-Clicker — Refactoring & MVP Plan

> **Goal:** turn the current half-migrated Dofus multi-boxing tool into a clean, reliable, modernized MVP that ships as a single .NET 8 Windows EXE. The plan is divided into ordered phases — each phase ends with a successful build and a runnable app.

---

## 0. Current state (baseline summary)

**What the app is**
A Windows multi-boxing utility for the game **Dofus**. It shows a floating always-on-top bar with one tile per detected Dofus window and lets the user broadcast clicks/keys to all clients via global low-level hooks (`WH_KEYBOARD_LL` / `WH_MOUSE_LL`), plus a Tesseract-OCR helper to auto-fill HDV sell prices.

**Code layout today**
```
Multi-Clicker/
?? Program.cs                       entry point, installs LL hooks
?? Core/
?   ?? ApplicationManager.cs        singleton orchestrator
?   ?? EventHandler.cs
?? Services/
?   ?? ConfigurationService.cs      static JSON store (Newtonsoft)
?   ?? HookManagementService.cs     LL hook callbacks + trigger dispatch
?   ?? WindowManagementService.cs   EnumWindows + SendInput + OCR
?   ?? PanelManagementService.cs    UI selection state
?   ?? OCRService.cs                Tesseract init/dispose
?   ?? LocalizationService.cs       FR/EN/ES resx switcher
?? Models/ConfigModels.cs           Config / KeyCombination / TRIGGERS / POINT / RECT
?? UI/
?   ?? MultiClickerForm.cs          floating bar + CharacterSelectionForm
?   ?? KeybindsConfigForm.cs        keybinds editor (has its own mouse hook)
?   ?? PositionConfigurationForm.cs OCR rectangles editor
?   ?? PositionOverlayForm.cs
?? Properties/Strings.resx (+ .fr, .es)
?? FileTraceListener.cs

Orphan legacy files still on disk (already removed in Phase 0 below):
  MultiClicker.cs, MultiClicker.Designer.cs,
  HookManagement.cs, WindowManagement.cs,
  PanelManagement.cs, ConfigManagement.cs,
  Services/ConfigService.cs, Interop/User32.cs,
  Constants.cs, ReplicateTextForm{,.Designer}.cs
```

**Target framework today:** `.NET Framework 4.7.2` + WinForms + C# 7.3.

---

## 1. MVP scope (locked)

| Feature | MVP? |
|---|---|
| Simple click broadcast | ? mandatory |
| Double click broadcast | ? mandatory |
| Select next / previous panel | ? mandatory |
| Group characters (`/invite`) | ? mandatory |
| Paste on all windows | ? mandatory |
| Fill HDV (OCR pricing) | ? mandatory + keep `OPTIONS`/F12 + `PositionConfigurationForm` |
| Background image per panel | ? mandatory + improve UX |
| Localization (FR / EN / ES) | ? mandatory — 100 % via `Strings.resx` |
| Manual vs Automatic display mode | ? keep, scoped to detected Dofus windows |
| VM (VirtualBox) support | ? dropped from MVP |
| `SIMPLE_CLICK_NO_DELAY` | ? removed |
| `TRAVEL` (broadcast text) | ? removed |
| `TOGGLE_AUTOPILOT` / `DOFUS_AUTOPILOT_SHORTCUT` | ? removed |
| `DOFUS_HAVENBAG` (orphan trigger, no handler) | ? removed |
| **Background clicks** (PostMessage, foreground fallback) | ? mandatory if achievable |
| **Modernized UI** (still WinForms, always-on-top) | ? mandatory |
| **.NET 8 migration** | ? mandatory |
| Auto-update (Velopack) | ?? deferred post-MVP |

---

## 2. Target architecture

```
MultiClicker (.NET 8 Windows, single-file publish)
?? Program.cs                       bootstrap + hook lifecycle (thin)
?? App/
?   ?? Bootstrapper.cs              ordered init: config ? loc ? ocr ? hooks ? discovery ? UI
?? Domain/                          pure POCOs (no UI / no Win32)
?   ?? Config.cs / GeneralConfig / PanelConfig / Position
?   ?? KeyCombination.cs
?   ?? Triggers.cs                  trimmed enum
?   ?? DofusWindow.cs               {Handle, Title, CharacterName}
?? Abstractions/                    interfaces (testable seams)
?   ?? IConfigStore.cs
?   ?? IWindowFinder.cs
?   ?? IInputBroadcaster.cs
?   ?? IHookService.cs
?   ?? IOcrService.cs
?   ?? IPanelStateService.cs
?? Infrastructure/
?   ?? Win32/User32.cs              single P/Invoke surface
?   ?? Hooks/Win32HookService.cs    LL hook install + dispatch
?   ?? Input/
?   ?    ?? BackgroundInputBroadcaster.cs   PostMessage path
?   ?    ?? ForegroundInputBroadcaster.cs   current SendInput path
?   ?? WindowFinder/DofusWindowFinder.cs    process-name + title heuristic
?   ?? Config/JsonConfigStore.cs            debounced atomic save + .bak rotation
?   ?? Ocr/TesseractOcrService.cs
?? Features/
?   ?? Click/ClickFeature.cs
?   ?? Selection/PanelSelectionFeature.cs
?   ?? Group/InviteAllFeature.cs
?   ?? Paste/PasteAllFeature.cs
?   ?? FillHdv/FillHdvFeature.cs
?? UI/
?   ?? MainOverlayForm.cs           modernized floating bar
?   ?? Controls/CharacterTile.cs    replaces ExtendedPanel
?   ?? Dialogs/CharacterPickerDialog.cs (Dofus windows only)
?   ?? KeybindsConfigForm.cs
?   ?? PositionConfigurationForm.cs / PositionOverlayForm.cs
?   ?? ImagePickerDialog.cs         built-in cosmetics grid
?? Properties/Strings.resx (+.fr/.es)
```

**Key behavior changes:**

| Concern | Today | Target |
|---|---|---|
| Clicks | foreground swap + `SendInput` (steals real cursor) | **`PostMessage(WM_LBUTTONDOWN/UP)` first; fallback to current path on failure** |
| `config.json` save cadence | on every event | **debounced (500 ms) + flush on shutdown** + atomic temp+`File.Replace` + `.bak` |
| Panel rebuild | `Panels.Clear()` then rebuild (loses backgrounds) | **merge** preserving `Background` for known characters |
| Hooks | scattered (Program + KeybindsConfigForm) | **centralized** in `Win32HookService` (+ isolated capture helper for the keybinds dialog) |
| Character picker | every desktop process | **detected Dofus windows only**, optional auto-detect `GameVersion` |
| Strings | mixed FR-hardcoded + resx | **100 % via `Strings.resx`** |
| Target framework | `net472` | **`net8.0-windows`** |
| Packaging | manual zip | **`dotnet publish` single-file self-contained** |

---

## 3. Execution phases

Each phase ends with `run_build` green and a runnable app.

### Phase 0 — Cleanup (? done)
- Deleted orphan legacy files unreferenced by `.csproj`: `MultiClicker.cs`, `MultiClicker.Designer.cs`, `HookManagement.cs`, `WindowManagement.cs`, `PanelManagement.cs`, `ConfigManagement.cs`, `Services/ConfigService.cs`, `Interop/User32.cs`, `Constants.cs`.
- Removed `ReplicateTextForm.cs` + `.Designer.cs` (Travel feature dropped). Verify the `<Compile Include="ReplicateTextForm*.cs" />` items are also removed from `MultiClicker.csproj`.

### Phase 1 — Trim domain (? done)
**Files touched:** `Models/ConfigModels.cs`, `Services/ConfigurationService.cs`, `Services/HookManagementService.cs`, `UI/MultiClickerForm.cs`, `UI/KeybindsConfigForm.cs`, `Properties/Strings.resx` (+ fr/es).

- `TRIGGERS` enum reduced to: `SELECT_NEXT, SELECT_PREVIOUS, OPTIONS, SIMPLE_CLICK, DOUBLE_CLICK, DOFUS_OPEN_DISCUSSION, GROUP_CHARACTERS, FILL_HDV, PASTE_ON_ALL_WINDOWS`.
- `PanelConfig.IsVM` removed (Newtonsoft ignores unknown props ? legacy configs still load).
- `GetDefaultKeybinds()` updated; migration code drops removed entries from existing configs.
- `HookManagementService.InitializeKeyActions` purged; `HandleToggleAutoPilot`, `SendAutoPilotKeyCombination`, `SendScanCodes`, VM-bypass block, `ShouldOpenMenuTravel` event all deleted.
- `MultiClickerForm`: remove `_markVmMenuItem`, `MarkVmItem_Click`, `ContextMenu_Opening`, `HandleTravelMenuRequest`, the "Marquer comme VM" menu entry, the `ShouldOpenMenuTravel` subscription.
- `KeybindsConfigForm._triggerDescriptions`: drop the entries for removed triggers.
- Resx files: remove keys `TRAVEL`, `SIMPLE_CLICK_NO_DELAY`, `DOFUS_HAVENBAG`, `TOGGLE_AUTOPILOT`, `DOFUS_AUTOPILOT_SHORTCUT`, `PASTE_ON_ALL_WINDOWS` duplicates, and the `Menu` / `Validate` keys used only by the removed `ReplicateTextForm`.

### Phase 2 — Persistence reliability (? done)
**Files touched:** `Services/ConfigurationService.cs`, `Core/ApplicationManager.cs`, `UI/MultiClickerForm.cs`.

- New API:
  - `SaveConfig()` ? schedules a debounced flush (500 ms `System.Threading.Timer`, single writer).
  - `SaveConfigImmediate()` ? synchronous flush (called from `Shutdown()` and from sensitive save points: keybind save, position save, character selection save).
- Atomic write: serialize to `config.json.tmp`, then `File.Replace(tmp, config.json, config.json.bak)`.
- **Fix data-loss pattern**: `MultiClickerForm.CreatePanels` and `UpdatePanelOrder` currently do `Panels.Clear()` then rebuild. Replace with a **merge** that preserves `Background` for any panel name already present in the config. Same fix for `UpdatePanelOrder`.
- Fix drag-Y typo in `TitleBar_MouseMove` (`_dragStartPoint.X` ? `_dragStartPoint.Y`).

### Phase 3 — Background-click input pipeline (? done)
**Files touched:** `Services/WindowManagementService.cs` (will be split later), `Services/HookManagementService.cs`.

- Introduce `IInputBroadcaster` (kept internal for now, plumbed via a `BroadcasterFactory` static method to limit churn).
  - `BackgroundInputBroadcaster.Click(handle, clientX, clientY)`:
    - `PostMessage(WM_MOUSEACTIVATE)` ? `PostMessage(WM_LBUTTONDOWN, MK_LBUTTON, MAKELPARAM(x,y))` ? small delay ? `WM_LBUTTONUP`.
    - For double-click: also `WM_LBUTTONDBLCLK`.
    - Computes client coords from `ScreenToClient(handle, cursor)`.
  - `ForegroundInputBroadcaster` = current `SetForegroundWindow` + `SendInput` path.
- Strategy: try background first; if Dofus's 3D canvas doesn't react (heuristic: optional setting per-panel "Background clicks fail here ? use foreground"), fall back. For MVP, expose a single global toggle in the title-bar menu *Préférer clics en arrière-plan* (default ON).
- Replace cursor-screen-coords with **per-window client coords** so users can move the mouse during a broadcast without disturbing it.

### Phase 4 — Dofus window discovery (? done)
**Files touched:** `Services/WindowManagementService.cs`, `Core/ApplicationManager.cs`, `UI/MultiClickerForm.cs` (CharacterSelectionForm), `Models/ConfigModels.cs`.

- New `WindowManagementService.EnumerateDofusWindows(out string detectedVersion)` enumerates running processes whose name starts with `Dofus`, accepts those with a valid `MainWindowHandle` and non-empty title, extracts character name from the head of the title, and auto-detects the game version using the longest `\d+(\.\d+){0,3}` chunk in any window title.
- New `WindowManagementService.FindDofusWindows()` consumes the enumerator, caches the detected `GameVersion` in `GeneralConfig` (debounced save) and refreshes `WindowHandles`.
- `ApplicationManager.RefreshWindowHandles()` now calls `FindDofusWindows()` instead of the title-pattern `FindWindows("- {ver} -")`.
- `CharacterSelectionForm.LoadAvailableCharacters()` now lists only detected Dofus character names instead of every desktop process.
- `WindowInfo` gained a `Handle` property to round-trip results from the enumerator.

### Phase 5 — UI modernization (? done — first pass)
**Files touched:** `UI/MultiClickerForm.cs`, `UI/ImagePickerDialog.cs` (new), `MultiClicker.csproj`.

- `ExtendedPanel` now uses an owner-drawn rounded-corner region (`GraphicsPath` + `Region`) and a `Timer`-driven hover/selected animation. Selected = LimeGreen ring (alpha 220); hover = SteelBlue ring (alpha 90); idle = subtle border. Painting is fully double-buffered.
- Main `MultiClickerForm` now sets `CS_DROPSHADOW` via `CreateParams` to drop a native shadow around the borderless overlay.
- New `UI/ImagePickerDialog.cs`: modal grid (`FlowLayoutPanel` of `PictureBox` thumbnails) over the cosmetics folder. The change-background context menu now opens it instead of a raw `OpenFileDialog`, so users no longer browse the filesystem to pick a tile.
- Cosmetics directory is resolved at runtime by `ImagePickerDialog.ResolveCosmeticsDirectory()` with the same probing pattern as `tessdata`: `BaseDirectory\cosmetics`, then `BaseDirectory\mandatory_assets\cosmetics`, then `Environment.CurrentDirectory\cosmetics`.
- `MultiClicker.csproj`: added a `CopyCosmetics` MSBuild target (`AfterTargets="AfterBuild"`) that copies every `mandatory_assets\cosmetics\*.png` into `bin\<Config>\cosmetics\` (288 tiles verified shipped in `bin\Debug\cosmetics\`).

Deferred to Phase 5b / 6:
- Rename `MultiClickerForm` ? `MainOverlayForm` and split `ExtendedPanel` into `UI/Controls/CharacterTile.cs`.
- Acrylic-ish title bar redesign and icon-only buttons.
- Localized tooltips on the new picker.

### Phase 6 — Localization completeness
**Files touched:** all UI files + `Properties/Strings*.resx`.

- Audit every `MessageBox.Show`, hardcoded `Text = "..."`, `ToolStripMenuItem("...")` in `UI/`. Replace with `Strings.X`.
- Keys to add (non-exhaustive): `ModeMenu`, `ModeAutomatic`, `ModeManual`, `SelectCharacters`, `ErrorCharacterSelection`, `NoCharactersSelected`, `SaveLabel`, `CancelLabel`, `SelectAll`, `Deselect`, `CharactersTitle`, `CharactersInstruction`, `PreferBackgroundClicks`.
- Fill `Strings.fr.resx` and `Strings.es.resx` for every new key.

### Phase 7 — .NET 8 migration (? done)
**Files touched:** `MultiClicker.csproj` (rewritten SDK-style), `Properties/AssemblyInfo.cs` (deleted), `App.config` (deleted), `packages.config` (deleted), `Properties/Resources.resx` + `Resources.Designer.cs` (deleted — unused), `MultiClicker.resx` (deleted — unused).

- `MultiClicker.csproj` converted to SDK-style targeting `net8.0-windows` with `<UseWindowsForms>true</UseWindowsForms>`. Assembly metadata (`Title`, `Product`, `Copyright`, `Version`, `FileVersion`, `AssemblyVersion`) moved into MSBuild properties; legacy `AssemblyInfo.cs` removed.
- NuGet references migrated from `packages.config` to `PackageReference`:
  - `Newtonsoft.Json` 13.0.3 (kept — preserves `KeyCombinationConverter`).
  - `System.Configuration.ConfigurationManager` 8.0.0 (needed by `Properties.Settings` on .NET 8).
  - `Tesseract` 5.2.0 + `Tesseract.Drawing` 5.2.0 (latter restores `Bitmap`/`PixConverter` interop dropped from `Tesseract` core on netstandard2.0).
- Removed `BootstrapperPackage` items, `System.Deployment` reference, and the legacy `<Import Project="..\packages\Tesseract...">` block; `Tesseract` package now wires native libs via standard SDK targets.
- `CopyCosmetics` MSBuild target preserved as `AfterTargets="AfterBuild"`; tessdata `Content` items preserved.
- Designer/resx relationships re-declared with `<Compile Update>` and `<EmbeddedResource Update>` items.
- Verified `dotnet build` is green and output ships `MultiClicker.exe` + `tessdata\*.traineddata` (2) + `cosmetics\*.png` (288).

### Phase 8 — Packaging (? done)
**Files touched:** `Multi-Clicker/MultiClicker.csproj`, `publish.ps1` (new at repo root).

- `Content` items for `tessdata\*.traineddata` now carry `CopyToPublishDirectory=PreserveNewest` so they ship next to the published EXE.
- New `CopyCosmeticsToPublish` MSBuild target (`AfterTargets="Publish"`) copies `mandatory_assets\cosmetics\*.png` and `mandatory_assets\tessdata\*.traineddata` into `$(PublishDir)`. (Tessdata is also covered there as a safety net for the `Link`-based content items when publishing single-file.)
- `publish.ps1` at the repo root:
  - Defaults to `Configuration=Release`, `Runtime=win-x64`, version read from the csproj `<Version>` (override with `-Version`).
  - Runs `dotnet publish` with `PublishSingleFile=true`, `SelfContained=true`, `IncludeNativeLibrariesForSelfExtract=true`, `EnableCompressionInSingleFile=true`, `DebugType=embedded`.
  - Asserts that the published EXE, tessdata, and cosmetics are all present, then zips the publish folder to `dist/MultiClicker-v<version>.zip`.
- Verified end-to-end: single-file `MultiClicker.exe` (85.7 MB) + `tessdata/` (2 files) + `cosmetics/` (288 files) packaged as `dist/MultiClicker-v1.0.0.zip` (98.2 MB).

### Phase 9 — Deferred (post-MVP)
- **VM support** with real polling (`GetAsyncKeyState` worker tied to a configured VM host window).
- **Velopack auto-update** from a GitHub Releases feed.
- **xUnit test project** against `Abstractions/` (KeyCombination matching, window-list rotation, JsonConfigStore debounce, legacy keybind migration).

---

## 4. Known bugs to fix along the way

| # | Where | Symptom | Fixed in phase |
|---|---|---|---|
| 1 | `MultiClickerForm.TitleBar_MouseMove` | Drag uses `_dragStartPoint.X` for Y ? window snaps vertically | Phase 2 |
| 2 | `MultiClickerForm.CreatePanels` / `UpdatePanelOrder` | `Panels.Clear()` then rebuild loses backgrounds when generating UI | Phase 2 |
| 3 | `ConfigurationService.SaveConfig` | Called on every UI change ? races with reads on hot path | Phase 2 |
| 4 | `WindowManagementService.PerformWindowClick` | Steals real cursor during broadcast | Phase 3 |
| 5 | `CharacterSelectionForm` | Lists every desktop process, not just Dofus | Phase 4 |
| 6 | `KeybindsConfigForm` | Installs its own LL mouse hook in parallel with the global one | Phase 5 cleanup |
| 7 | `HookManagementService.IsKeyCombinationPressed` | XButton state only true for an instant; mouse keybinds rely on same down event — fragile | Phase 3 (state model rewrite) |
| 8 | `ConfigurationService.LoadConfig` | Calls `SaveConfig()` synchronously during load ? IO on startup | Phase 2 |
| 9 | Hardcoded FR strings | "Marquer comme VM", "Erreur", etc. | Phases 1 & 6 |

---

## 5. Acceptance criteria for MVP

- ? App starts with **no `config.json`** present and creates a valid default file.
- ? App detects running Dofus clients automatically; no manual `GameVersion` needed.
- ? Floating bar shows one rounded tile per character with selected state visible.
- ? Simple click + double click + select next/previous + paste on all + group + Fill HDV all work end-to-end against ? 2 Dofus clients.
- ? Default keybind path: `Simple click = XButton1`, `Double = XButton2`, `Paste all = Ctrl+Alt+V`, `Group = F5`, `Fill HDV = Alt+Oem7 (²)`, `Options = F12`, `Next/Prev = F1/F2`.
- ? Closing the app saves every UI change made during the session (backgrounds, tile order, mode, selected characters, keybinds).
- ? Background-click mode works for Dofus UI; falls back transparently to foreground for the 3D canvas.
- ? Full FR/EN/ES localization — no hardcoded string in any UI file.
- ? Single-file `MultiClicker.exe` (~80–120 MB self-contained, ~5 MB framework-dependent) runs on a clean Windows 10/11 box with `tessdata/` and `cosmetics/` beside it.

---

## 6. Resume checkpoint

Phases 0–5 are complete; the build is green. Highlights:

- OCR/tessdata regression resolved via `AppDomain.BaseDirectory` resolver in `OCRService` + content-copy items in `MultiClicker.csproj`.
- `ConfigurationService.LoadConfig()` only rewrites `config.json` when defaults or validators actually change something.
- `WindowManagementService.PerformClickOnWindow` now tries `PostMessage(WM_LBUTTONDOWN/UP)` first (no cursor steal), falling back to the original `SendInput` path on failure. Behavior is gated by `GeneralConfig.PreferBackgroundClicks` (default `true`) and exposed via a new "Préférer clics en arrière-plan" toggle in the title-bar menu.
- Dofus-only window discovery via `WindowManagementService.FindDofusWindows()` with automatic `GameVersion` detection; `CharacterSelectionForm` now lists only detected Dofus clients.
- `ExtendedPanel` has rounded corners + animated hover/selected ring, the main form casts a native drop-shadow, and a new `ImagePickerDialog` shows the cosmetics folder as a grid. `MultiClicker.csproj` now ships the 288 cosmetic PNGs to the output directory via a `CopyCosmetics` MSBuild target.

**Next up: Phase 6 (localization completeness).**
