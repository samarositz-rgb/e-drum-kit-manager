# E-Drum Kit Manager (formerly TD-12 Kit Manager)

An accessible (JAWS/NVDA/VoiceOver-first) kit manager for EZdrummer 3 in REAPER, driven from an
electronic drum module.

## Layout

- `Core/` â€” shared, cross-platform (.NET, no UI): kits, EZdrummer state read/write (`EzdState`),
  drum catalog and library scanner (Windows registry + Mac Application Support paths, system SQLite),
  module profiles (`ModuleProfile`, `HardwareSettings`), pad levels, the REAPER bridge protocol, and the
  REAPER-side files embedded as resources:
  - `EDrum Kit Bridge.lua` â€” runs inside REAPER (started from `Scripts/__startup.lua`), file-based requests
  - `snare_rim_split.jsfx` â€” soft rim hits to cross-stick (notes set from the profile)
  - `pad_levels.jsfx` â€” per-note velocity scaling (128 hidden sliders, set per kit)
- Root â€” Windows window (WinForms): `MainForm`, `KitEditorForm`, `PadLevelsForm`, `HardwareForm`,
  `LearnForm`, `MetronomePanel`.

## Module profiles

`ModuleProfile.Zones` lists every pad zone "Learn my kit" asks for, with the EZdrummer map line for it.
The built-in Roland TD-12 profile keeps an e-drum map you already made in EZdrummer (`WriteMap = false`); learned
profiles write the map from the learned notes. Generating the map from the TD-12 notes reproduces the
hand-made TD-12 map line for line, which is the regression check for the map writer.

The bridge reads `bridge/module.txt` (drum channel, kit count, MIDI ports, rim split notes).

## Data

Windows: `%APPDATA%\EDrumKitManager`; Mac: `~/Library/Application Support/EDrumKitManager`.
REAPER must load EZdrummer as **VST3** (the AU state format differs).

## Mac version (next step)

Reuse `Core/` as is; build a native AppKit window (`net-macos`) for VoiceOver. Needs testing on a Mac.

## Diagnostics

`EDrumKitManager.exe --check out.txt` (load kits; set EDRUMKM_DATA to a scratch folder first), `--dump-catalog out.txt`, `--maplines out.txt`,
`--genkit profile.json kitIndex out.txt`.

## Building a release (Windows)

```
dotnet publish TD12KitManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true -p:DebugType=none -o dist/app
"%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" installer\EDrumKitManager.iss
```

The installer (`dist/EDrumKitManager-Setup-<version>.exe`) installs per user, without admin rights, to
`%LOCALAPPDATA%\Programs\E-Drum Kit Manager`, with Start menu entries, the user guide
(`installer/User Guide.html`) and an optional desktop shortcut.

Renamed from TD-12 Kit Manager: settings move from `%APPDATA%\TD12KitManager` to `%APPDATA%\EDrumKitManager`
on first start (the old folder is kept), and the REAPER bridge becomes `EDrum Kit Bridge.lua` (the old
startup line and script are replaced). Set `EDRUMKM_DATA` to a scratch folder to test a fresh install.


Speech: messages go to the screen reader through UI Automation notifications (JAWS, Narrator), or straight to NVDA through NV Access's controller client (`ThirdParty/NVDA`, LGPL 2.1) when NVDA is running. `--speech-check out.txt` reports whether it loads.
