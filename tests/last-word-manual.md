# Last-word replacement regression checks (1.3.7)

Run `dotnet run --project tests/Regression/Regression.csproj -c Release` for
conversion, deletion-count safety, portable paths and native Edit/RichEdit
selection/replacement checks on private Windows controls, plus STA worker
dispatch, overlap rejection and recovery after exceptions. These do not test
the installed Notepad/ChatGPT UI Automation providers or send desktop input.

For live checks, use a disposable draft in ChatGPT Windows, a Chromium
contenteditable editor and Notepad. Install EN/RU/HE layouts. Preserve a known
clipboard value before each test and confirm it is restored afterwards.

1. Reproduce the reported quick EN -> HE -> EN cycle with `velorum4400`,
   pressing Insert at its end with no selection. For UIA text `4400הקךםרוצ`, expect only
   `velorum4400`, with no Hebrew residue. An unreliable selection must log
   `Last-word replacement using backspace fallback, length=11`.
2. Repeat after an unrelated prefix, in the middle of a document with a suffix,
   and with trailing spaces. The prefix, suffix and spaces must survive.
3. Repeat with plain Hebrew, Russian and English words, including punctuation.
   In Notepad Edit/RichEdit, expect `Native last-word: class=...,
   selection confirmed, length=...`. For UIA editors, when selection copies the entire word, expect
   `Last-word selection confirmed through clipboard` and no backspace log.
4. Select a portion of text manually before Insert. Only the selection should
   be converted; the new last-word backspace fallback must not run.
5. Use the full-text hotkey. Existing full-text conversion must be unchanged.
6. In an editor without UIA text ranges, confirm the existing Ctrl+Shift+Left
   and clipboard path still works. Without a known word-end range, this version
   does not guess the number of characters to delete.
7. If selection is unreliable for text containing emoji, combining marks or
   bidi controls, expect a logged safe failure and no Backspace deletion.
8. If focus changes or the provider cannot confirm a collapsed caret at the
   saved logical word end, expect a logged failure and no deletion/paste.

For portable builds, diagnostics belong in `data/diagnostic.log` beside the
executable; installed builds use `%APPDATA%/LayoutFixer/diagnostic.log`.

For 1.3.7, repeat corrections in ChatGPT while checking that the tray menu and
keyboard remain responsive. Compare the full START-to-TIMING-total interval;
per-stage TIMING values are cumulative, so their differences locate remaining
latency. A slow UIA provider may still take time, but must not block the hook
thread. Rapid repeat hotkeys and scanner corrections must not overlap.
