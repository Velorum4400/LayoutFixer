# Layout Fixer

Windows tray utility for correcting text typed in the wrong keyboard layout.

## 1.3.10 — mixed-language correction cycles

Successive corrections within 15 seconds retain the original logical text when
the document exactly matches the preceding conversion result. A RU → HE → EN →
RU cycle therefore restores the original Russian text while preserving genuine
English terms such as `Gamer's Nexus`, `LG` and `Wi-Fi`. Standalone English text
continues to convert normally.

## 1.3.9 — direct Unicode replacement

When UI Automation provides a verified logical end of the last word, LayoutFixer
deletes that word and sends the converted Unicode characters directly. This is
used for Chromium and Qt editors such as ChatGPT and Telegram, so the correction
does not need to open, write or restore the clipboard. The log records
`Last-word replacement sent as direct Unicode text`.

## 1.3.8 — clipboard publication and log viewer

Converted text is published as eagerly rendered Unicode through the native
Windows clipboard API, avoiding OLE publication waits. The user's original
clipboard is still restored after the paste. Timing logs bracket publication.
The settings button now opens a live viewer with `diagnostic.log` and
`scanner_diagnostic.log` tabs and a separate Clear log button on each tab.
Existing `scaner_diagnostic.log` is renamed automatically when the new file
does not exist. Correction actions are delimited by START and END, including
failed actions. Large logs display their last 512 KB without truncating files.

## 1.3.7 — responsive correction

Correction runs on a separate STA worker, keeping the tray UI and low-level
keyboard hooks responsive during UIA/clipboard waits. Hotkey and scanner
corrections share a single-operation guard; overlapping requests are skipped.
Last-word paste confirmation no longer reads the entire document. Clipboard
polling responds every 10 ms while preserving the previous overall timeout.
`TIMING` diagnostics report cumulative elapsed milliseconds for each stage.

## 1.3.6 — Notepad last-word selection

Edit/RichEdit controls now use native Windows text and selection messages before
trying UI Automation. Logical selection offsets avoid RTL arrow-key ambiguity,
preserve surrounding whitespace, and support selections beyond 65,535 characters.
The diagnostic log includes the focused window class and native selection result.
Chromium/WebView continues to use the verified UIA/backspace path introduced below.

## 1.3.5 — last-word replacement

Automatically selected words are checked through the clipboard before replacement.
If the editor selected only part of the word (for example, the digits in a Hebrew
word in ChatGPT Windows/Chromium), LayoutFixer restores and verifies a collapsed
caret at the logical word end, sends exactly `original.Length` Backspace presses,
then pastes the converted text. Confirmed selections keep the normal paste path.
The fallback logs `Last-word replacement using backspace fallback, length=...`.
It aborts if focus/caret cannot be verified or character counts would be unsafe.

Portable logs are stored in `data` next to the executable; installed logs remain
in `%APPDATA%\LayoutFixer`. See [regression checks](tests/last-word-manual.md).

Default hotkeys:
- Ctrl+Shift — correct all text in the current field
- Ctrl+Alt — correct the last word

Languages:
- English (US)
- Russian
- Hebrew

This revision fixes global hotkey detection for left/right Ctrl, Shift and Alt,
handles WM_SYSKEYDOWN/WM_SYSKEYUP for Alt, ignores keys injected by the app
itself, and supports all hotkey choices exposed by the Settings window.

## Build

```powershell
dotnet build
```

## Run

```powershell
dotnet run
```

## Publish single self-contained EXE (Windows x64)

```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```

Output:
`bin\\Release\\net8.0-windows\\win-x64\\publish\\LayoutFixer.exe`


## v4 fix
Исправлен размер native WinAPI структуры INPUT для 64-bit Windows.
В предыдущей версии SendInput мог возвращать ERROR_INVALID_PARAMETER,
из-за чего синтетические Ctrl+A / Ctrl+C / Ctrl+V не отправлялись.

При внутренней ошибке диагностический лог сохраняется в:
%APPDATA%\LayoutFixer\error.log


## v5 fix
Исправлена ошибка Clipboard 0x800401D0 (CLIPBRD_E_CANT_OPEN).
Windows может кратковременно блокировать буфер обмена другим процессом.
Теперь чтение, очистка и запись буфера выполняются с повторными попытками.


## v6 diagnostics / fallback
- Добавлен подробный `%APPDATA%\LayoutFixer\diagnostic.log`.
- Если Ctrl+C не меняет Clipboard, программа дополнительно пробует WM_COPY на реально сфокусированном контроле.
- Переключение раскладки отправляется и окну, и сфокусированному контролу.


## v7
Исправлено определение направления конвертации.

Теперь:
- исходный язык определяется по символам выделенного текста;
- текущая раскладка Windows используется как целевая, если Ctrl+Shift уже успел её переключить;
- если текущая раскладка совпадает с языком текста (например, при Ctrl+Alt), выбирается следующая раскладка.

Пример:
`ghbdtn` + Windows уже переключилась на RU -> программа определяет EN -> RU -> `привет`.

## Как обновлять без удаления папки
Папку проекта больше удалять не нужно.

1. Полностью закрой LayoutFixer.
2. Распакуй новую версию поверх существующей папки с заменой файлов.
3. Запусти `build.cmd`.
4. Готовый exe будет в `dist\LayoutFixer.exe`.

`build.cmd` сам закрывает запущенный LayoutFixer перед сборкой.


## v8
Добавлено чтение выделенного текста через Windows UI Automation.
Clipboard больше не является единственным способом получить текст:
1. UI Automation
2. Ctrl+C
3. WM_COPY

Это особенно полезно для Windows 11 Notepad и других современных приложений.


## v9
Исправлен режим Ctrl+Alt (последнее слово).

Причина:
UI Automation через ValuePattern возвращал весь текст поля, даже когда
Ctrl+Shift+Left выделял только последнее слово.

Теперь:
- для Ctrl+Alt используется только реальный selection из TextPattern;
- полный ValuePattern запрещён в режиме "last word";
- если приложение не отдаёт выделение через UI Automation, используется Ctrl+C fallback.


## v10
Режим Ctrl+Alt больше не использует синтетический Ctrl+Shift+Left.

Теперь программа:
1. получает позицию каретки через Windows UI Automation;
2. читает текст перед кареткой;
3. находит последний непрерывный фрагмент до пробела/переноса строки;
4. выделяет ровно этот диапазон через TextPatternRange.Select();
5. конвертирует и заменяет только его.

Это исправляет случай, когда Windows 11 Notepad не создавал выделение после SendInput.


## v10.1 build fix
Добавлены явные ссылки на:
- UIAutomationClient
- UIAutomationTypes

Это исправляет ошибки компиляции для TextPatternRange,
TextPatternRangeEndpoint и TextUnit.


## v10.2 build fix
Исправлен namespace для UI Automation text API.

Добавлено:
using System.Windows.Automation.Text;

Удалены лишние ручные ссылки на UIAutomationClient/UIAutomationTypes,
которые конфликтовали с .NET 8 Windows Desktop reference pack.


## v11 — installed layouts only

При запуске LayoutFixer один раз получает список раскладок, уже загруженных/установленных в Windows,
и оставляет из них только поддерживаемые языки: EN, RU, HE.

Цикл строится только из реально доступных языков:
- EN + RU -> EN <-> RU
- EN + HE -> EN <-> HE
- RU + HE -> RU <-> HE
- EN + RU + HE -> EN -> RU -> HE -> EN
- только один поддерживаемый язык -> исправление не выполняется

Важно: LayoutFixer больше нигде не вызывает LoadKeyboardLayout.
Для переключения используется только HKL, который Windows уже вернула через GetKeyboardLayoutList.

Список обнаруженных языков записывается в diagnostic.log в поле:
available=[EN,RU,HE]


## v12 — startup layout status
Сразу при запуске в `%APPDATA%\LayoutFixer\diagnostic.log` записывается:
`LayoutFixer started. Installed supported layouts: [EN,RU,HE]`

В настройках также отображается список доступных раскладок.


## v13 — UI/build/version improvements

- `build.cmd` automatically closes the terminal after a successful build.
  If the build fails, the terminal stays open so the error can be read.
- Settings window increased from 450x300 to 900x600 (100% larger in both dimensions).
- Settings window is now resizable with the mouse and can be maximized.
- Application version is centralized in the project: `1.12.0`.
- Version is shown:
  - in the tray tooltip;
  - at the top of the tray menu;
  - in the Settings window title;
  - inside Settings;
  - in Windows EXE file metadata (`File version` / `Product version`).


## v0.14

- Новая схема версий: v0.14, затем v0.15, v0.16 и т.д.
- Удалена настройка «Показывать уведомления в трее».
- Информационный текст, список доступных раскладок и номер версии
  в окне настроек теперь являются read-only TextBox: текст можно
  выделять мышкой и копировать через Ctrl+C.


## v0.15 — improved hotkey editor

- Fixed list of hotkeys removed.
- Click a hotkey field and press the desired key/chord directly.
- Supports 1, 2, or 3 keyboard keys.
- Examples: `F8`, `Ctrl+Q`, `Ctrl+Shift+Q`, `Alt+F2`.
- Left/right Ctrl, Shift, Alt and Win are normalized to the same logical key.
- Duplicate keys in one chord do not count twice.
- A fourth key is rejected.
- The two actions cannot use identical hotkeys.
- The global keyboard hook now recognizes ordinary keys as well as modifiers.


## v0.16

- Функция «Исправлять последнее слово» теперь работает как:
  «Исправлять выделенный текст или последнее слово».
- При существующем выделении исправляется только выделенный текст.
- При отсутствии выделения исправляется последнее слово перед курсором.
- Новый хоткей по умолчанию для этой функции: `Insert`.
- Нажатие на поле текущего хоткея теперь открывает маленькое отдельное окно.
  Поле записи в нём всегда пустое и ждёт новую комбинацию.
- В настройки добавлена кнопка «Список изменений».
- Добавлено отдельное окно с историей изменений по версиям.


## v0.17

- Исправлена обработка ошибок в `build.ps1`.
- После `dotnet publish` теперь явно проверяется `$LASTEXITCODE`.
- Любая ошибка сборки возвращает код выхода `1`.
- `build.cmd` сохраняет этот код и при ошибке выполняет `pause`.
- При успешной сборке окно терминала по-прежнему закрывается автоматически.


## v0.18

- Fixed compiler error `CS0136` in `TextFixer.cs`.
- Renamed conflicting local variables used for existing selection and last-word text.


## v0.19

### New features
- Migrates the old default `Ctrl+Alt` last-word hotkey to `Insert`.
- Captures the clipboard before correction and restores it afterward.

### Fixes
- The full “selected text or last word” setting label is no longer clipped.
- Corrected text no longer remains in the clipboard.
- Clipboard restoration also runs when correction fails.
- Changelog entries are separated by version and by “New features” / “Fixes”.


## v0.20

### New features
- Setting names are selectable/copyable while retaining their checkbox controls.
- Changelog now uses rich text formatting.

### Fixes
- Removed the old repeated equals-sign separators.
- Version headings are larger, bold, and end with a colon.
- “Новые функции:” and “Исправления:” are bold and end with colons.
- Every changelog item is placed on its own line.
- Versions are separated by a horizontal line.
- Settings window is wider so the “selected text or last word” option and its hotkey stay on one row.


## v0.21

### New features
- None.

### Fixes
- Removed borders around selectable text in Settings.
- Added a reusable `SelectableLabel` control: text remains selectable/copyable, but the caret is hidden.
- Mouse pointer over selectable setting text is no longer an I-beam.
- Added a small left inset to changelog content.


## v0.22

### New features
- Added UI language selector: English (default), Russian, Hebrew.
- Language is saved in `settings.json`.
- Tray menu, Settings, hotkey editor, validation messages and changelog are localized.
- Hebrew uses right-to-left layout.
- Added a Restore defaults button next to Save.
- Restore defaults resets startup, enabled actions, hotkeys and language to the application's defaults.

### Fixes
- Hotkey hint changed to the requested wording.
- Increased the vertical space reserved for the hotkey hint.

## v1.0.0

### New features
- Added the official LayoutFixer application icon to the executable, Windows shortcuts, application windows and tray icon.
- Redesigned the Settings window with a blue branded header, application logo, card-based layout and modern action buttons.
- Updated the hotkey editor to match the new interface.

### Fixes
- Preserved all existing hotkey, clipboard-restoration, language and installed-layout behavior while moving to the new interface.
- Version metadata now reports 1.0.0 consistently.


## v1.0.1

### New features
- Added global crash/error logging to `%APPDATA%\LayoutFixer\crash.log`.
- Logs WinForms UI-thread exceptions, AppDomain unhandled exceptions, and unobserved task exceptions.
- Language switching now logs the old/new language and ApplyLanguage stages.

### Fixes
- Diagnostic logging itself is isolated so it cannot crash LayoutFixer.
