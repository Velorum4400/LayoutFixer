# Layout Fixer

Windows tray utility for correcting text typed with the wrong keyboard layout.

## Current architecture (1.4.0)

The active application contains one correction command: **Correct all text**.
Its behavior is unchanged while the command is being redesigned separately.

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
