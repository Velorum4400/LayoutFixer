# Layout Fixer

Windows tray utility for correcting text typed with the wrong keyboard layout.

## Current architecture (1.6.0)

The active application contains one correction command: **Correct all text**.
At startup, LayoutFixer stores the Windows keyboard layouts in their system
order. Each successful correction advances exactly one position in that list
and wraps from the last layout to the first.

The operation uses these independent components:

- `HotkeyService` reports the configured hotkey without correction logic.
- `TextReplacementService` coordinates one operation at a time and binds it to
  the window that was active when the operation began.
- `ClipboardService` snapshots all available formats, waits for sequence-number
  changes and restores the snapshot only when no newer Clipboard data exists.
- `KeyboardInputService` sends only Ctrl+A, Ctrl+C and Ctrl+V through SendInput.
- `KeyboardLayoutService` refreshes installed layouts, builds and caches a map
  for each one, resolves the active window layout and switches to the next layout.
- `KeyboardLayoutMap` enumerates physical scan codes with None, Shift, AltGr and
  Shift+AltGr through `ToUnicodeExW`; its reverse map retains all candidates.
- `LayoutConverter` transfers scan code and modifiers through the cached source
  and target maps. It does not use `VkKeyScanExW`.

Copied text is retained in process memory. Clipboard copy changes are observed
until stable for 30 ms; repeated sequence changes with the same text hash are
treated as one copy pipeline. A confirmed newer external Clipboard state becomes
the new restore snapshot. The Clipboard is restored immediately after Copy,
checked again before Paste, and restored after Paste only if LayoutFixer's value
is still current. The default paste delay is 100 ms and is configurable in the
replacement service. Diagnostics retain owner, process, formats, length and
privacy-safe hash data without recording user text.

The following settings remain visible as disabled placeholders and display
**Temporarily unavailable**:

- Correct selected text or the last word
- Scanner configuration

The old selection/last-word implementation, selection-restoration option and
scanner subsystem have been removed from the runtime. Only `diagnostic.log`
remains in the log viewer.

## Build

Install the .NET 8 SDK, then run:

```powershell
.\build.cmd
```

or:

```powershell
dotnet build LayoutFixer.csproj -c Release
```

The build currently compiles and validates the application only. It does not
create an installer or portable package.

Regression checks:

```powershell
dotnet run --project tests/Regression/Regression.csproj -c Release
```

Application data and logs are stored in `%APPDATA%\LayoutFixer`.

