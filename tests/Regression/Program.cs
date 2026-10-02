using System.Reflection;
using LayoutFixer;

int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine("PASS: " + name);
    passed++;
}

void CheckLastWord(string selection, int start, int length, int trailing, bool boundary, string name)
{
    LastWordSelectionAnalysis analysis = LastWordSelectionAnalyzer.Analyze(selection);
    Check(analysis.HasFragment && analysis.FragmentStart == start &&
          analysis.FragmentLength == length &&
          analysis.TrailingWhitespaceLength == trailing &&
          analysis.BoundaryWhitespaceFound == boundary, name);
}

Check(KeyboardLayoutService.RefreshLayouts(), "installed layouts refresh");
IReadOnlyList<KeyboardLayoutInfo> layouts = KeyboardLayoutService.Layouts;
Check(layouts.Count > 0, "installed layout list retained");
Check(layouts.Select(x => x.Handle).Distinct().Count() == layouts.Count, "layout handles are unique");
KeyboardLayoutMap Map(KeyboardLayoutInfo layout)
{
    Check(KeyboardLayoutService.TryGetMap(layout, out KeyboardLayoutMap map),
        $"layout map is cached for {layout.ShortName}");
    return map;
}
Check(LayoutConverter.Convert("text \t\r\n", Map(layouts[0]), Map(layouts[0]), out _) == "text \t\r\n",
    "same-layout conversion preserves text");
if (layouts.Count > 1)
{
    Check(KeyboardLayoutService.TryGetNext(layouts[0], out KeyboardLayoutInfo second) &&
          second.Handle == layouts[1].Handle, "next layout follows saved order");
    Check(KeyboardLayoutService.TryGetNext(layouts[^1], out KeyboardLayoutInfo first) &&
          first.Handle == layouts[0].Handle, "last layout wraps to first");
    Check(LayoutConverter.Convert(" \t\r\n", Map(layouts[0]), Map(layouts[1]), out _) == " \t\r\n",
        "whitespace survives cross-layout conversion");
}
KeyboardLayoutInfo? english = layouts.FirstOrDefault(x => (x.LanguageId & 0x03ff) == 0x09);
KeyboardLayoutInfo? russian = layouts.FirstOrDefault(x => (x.LanguageId & 0x03ff) == 0x19);
KeyboardLayoutInfo? hebrew = layouts.FirstOrDefault(x => (x.LanguageId & 0x03ff) == 0x0d);
if (english != null && russian != null)
{
    Check(LayoutConverter.Convert("ghbdtn", Map(english), Map(russian), out _) == "привет",
        "physical key conversion English to Russian");
    Check(LayoutConverter.Convert("#", Map(english), Map(russian), out _) == "№",
        "Shift punctuation conversion English to Russian");
}
if (english != null && hebrew != null)
    Check(LayoutConverter.Convert("akuo", Map(english), Map(hebrew), out _) == "שלום",
        "physical key conversion English to Hebrew");
if (english != null && russian != null && hebrew != null)
{
    const string cycleSource = "Привет, я исправленный текст. Все ли прошло так, как надо? Были ли проблемы с чем-то?";
    string englishText = LayoutConverter.Convert(cycleSource, Map(russian), Map(english), out _);
    string hebrewText = LayoutConverter.Convert(englishText, Map(english), Map(hebrew), out _);
    string cycleResult = LayoutConverter.Convert(hebrewText, Map(hebrew), Map(russian), out _);
    Check(cycleResult == cycleSource, "Russian-English-Hebrew-Russian cycle is lossless");
    Check(LayoutConverter.Convert(".", Map(russian), Map(english), out _) == "/" &&
          LayoutConverter.Convert("/", Map(english), Map(hebrew), out _) == "." &&
          LayoutConverter.Convert(".", Map(hebrew), Map(russian), out _) == ".",
        "Hebrew period follows scan code 0x35 to Russian period");
    Check(LayoutConverter.Convert("😀\tunsupported\r\n", Map(english), Map(russian), out _) == "😀\tгтыгззщкеув\r\n",
        "unsupported Unicode and whitespace are preserved");
}

Check(typeof(TextReplacementService).GetMethod("TryReplaceAllText") != null,
    "text replacement coordinator is available");
Check(typeof(ClipboardService).GetMethod("WaitForTextChange") != null,
    "clipboard sequence wait is isolated in ClipboardService");
Check(typeof(KeyboardInputService).GetMethod("SelectAll") != null,
    "SendInput chords are isolated in KeyboardInputService");
Check(typeof(KeyboardInputService).GetMethod("SelectPreviousWord") != null,
    "last-word selection chord is isolated in KeyboardInputService");
Check(typeof(KeyboardInputService).GetMethod("CollapseSelectionToStart") == null &&
      typeof(KeyboardInputService).GetMethod("CollapseSelectionToEnd") == null &&
      typeof(KeyboardInputService).GetMethod("SelectCharactersLeft") == null &&
      typeof(KeyboardInputService).GetMethod("SelectCharactersRight") == null,
    "obsolete exact last-word selection controls are removed");
Check(Enum.GetValues<LastWordSearchDirection>().SequenceEqual(new[]
      { LastWordSearchDirection.Left, LastWordSearchDirection.Right }),
    "last-word search supports Left and Right directions");
Check(KeyboardInputService.GetKeyboardEventFlags(0x25, false) == 0x0001 &&
      KeyboardInputService.GetKeyboardEventFlags(0x25, true) == 0x0003,
    "extended Left key uses KEYEVENTF_EXTENDEDKEY for down and up");
Check(KeyboardInputService.GetKeyboardEventFlags(0x10, false) == 0 &&
      KeyboardInputService.GetKeyboardEventFlags(0x11, true) == 0x0002,
    "Ctrl and Shift retain ordinary keyboard flags");
CheckLastWord("hello", 0, 5, 0, false, "start-of-field last word is recognized");
CheckLastWord("hello,", 0, 6, 0, false, "punctuation remains part of last word");
CheckLastWord("hello!!!", 0, 8, 0, false, "multiple punctuation remains part of last word");
CheckLastWord("one hello,", 4, 6, 0, true, "whitespace boundary precedes last fragment");
CheckLastWord("one hello,   ", 4, 6, 3, true, "trailing whitespace is excluded from last fragment");
LastWordSelectionAnalysis whitespaceOnly = LastWordSelectionAnalyzer.Analyze(" ");
Check(!whitespaceOnly.HasFragment && !whitespaceOnly.BoundaryWhitespaceFound &&
      whitespaceOnly.TrailingWhitespaceLength == 1,
    "trailing whitespace alone is not a last-word boundary");
CheckLastWord("оченьдлинноеслово", 0, 17, 0, false, "long Unicode last word is recognized");
CheckLastWord("שלום", 0, 4, 0, false, "RTL first word is recognized");
CheckLastWord("שלום עולם", 5, 4, 0, true, "RTL last word has a whitespace boundary");
CheckLastWord("שלום עולם!", 5, 5, 0, true, "RTL punctuation remains part of last word");
CheckLastWord("שלום עולם   ", 5, 4, 3, true, "RTL trailing whitespace is excluded");
Check(typeof(HotkeyService).GetEvents().Any(x => x.Name == "Pressed"),
    "hotkey service exposes only the trigger event");
Check(typeof(HotkeyService).GetProperty("LastVirtualKey") != null &&
      typeof(HotkeyService).GetProperty("LastMessage") != null &&
      typeof(HotkeyService).GetProperty("LastFlags") != null &&
      typeof(HotkeyService).GetProperty("LastInjected") != null,
    "hotkey service exposes callback diagnostics");
Check(typeof(TextReplacementService).Assembly.GetType("LayoutFixer.TextFixer") == null,
    "legacy combined text fixer removed");
Check(typeof(LayoutConverter).GetMethods(BindingFlags.Public | BindingFlags.Static)
    .All(method => !method.GetParameters().Any(parameter => parameter.ParameterType.Name.Contains("Clipboard"))),
    "layout converter has no Clipboard dependency");

Exception? staFailure = null;
var staThread = new Thread(() =>
{
    try
    {
        Check(ClipboardService.TryCapture(out ClipboardSnapshot snapshot), "Clipboard snapshot captured");
        uint beforeWrite = ClipboardService.SequenceNumber;
        Check(ClipboardService.TrySetText("тест שלום 123", out uint writtenSequence),
            "Unicode text written through ClipboardService");
        Check(writtenSequence != beforeWrite, "Clipboard sequence changes after write");
        Check(System.Windows.Forms.Clipboard.GetText() == "тест שלום 123", "written Clipboard text is readable");
        Check(ClipboardService.RestoreIfUnchanged(snapshot, writtenSequence) == ClipboardRestoreResult.Restored,
            "Clipboard restored when sequence is unchanged");

        Check(ClipboardService.TryCapture(out ClipboardSnapshot safetySnapshot),
            "Clipboard snapshot captured for external-change test");
        Check(ClipboardService.TrySetText("LayoutFixer value", out uint layoutFixerSequence),
            "LayoutFixer test value written");
        Check(ClipboardService.TrySetText("new external value", out uint externalSequence),
            "new external Clipboard value written");
        Check(ClipboardService.RestoreIfUnchanged(safetySnapshot, layoutFixerSequence) ==
              ClipboardRestoreResult.SkippedBecauseChanged,
            "restore skips Clipboard changed after LayoutFixer write");
        Check(System.Windows.Forms.Clipboard.GetText() == "new external value",
            "new Clipboard data is not overwritten");
        Check(ClipboardService.RestoreIfUnchanged(safetySnapshot, externalSequence) ==
              ClipboardRestoreResult.Restored,
            "external-change test restores original Clipboard safely");

        Check(ClipboardService.TryCaptureStable(out ClipboardSnapshot stabilizationSnapshot,
                out _, out _), "Clipboard snapshot captured for stabilization tests");
        Check(ClipboardService.TrySetText("stabilization baseline", out uint sameTextBefore),
            "native Clipboard baseline written for stabilization tests");
        var sameTextWriter = new Thread(() =>
        {
            Thread.Sleep(10);
            ClipboardService.TrySetText("stable copied text", out _);
            Thread.Sleep(15);
            ClipboardService.TrySetText("stable copied text", out _);
        });
        sameTextWriter.SetApartmentState(ApartmentState.STA);
        sameTextWriter.Start();
        var sameTextTimer = System.Diagnostics.Stopwatch.StartNew();
        bool sameTextStable = ClipboardService.WaitForStableCopy(sameTextBefore, sameTextTimer,
                out ClipboardCopyResult sameTextResult);
        sameTextWriter.Join();
        Check(sameTextStable,
            "multiple same-text Clipboard changes stabilize");
        Check(sameTextResult.Text == "stable copied text" &&
              sameTextResult.NewExternalSnapshot == null &&
              sameTextResult.ChangeCount >= 2,
            "same hash is treated as one Copy pipeline");

        uint externalDuringCopyBefore = ClipboardService.SequenceNumber;
        var externalDuringCopyWriter = new Thread(() =>
        {
            Thread.Sleep(10);
            ClipboardService.TrySetText("source held in memory", out _);
            Thread.Sleep(15);
            ClipboardService.TrySetText("new external clipboard", out _);
        });
        externalDuringCopyWriter.SetApartmentState(ApartmentState.STA);
        externalDuringCopyWriter.Start();
        var externalDuringCopyTimer = System.Diagnostics.Stopwatch.StartNew();
        Check(ClipboardService.WaitForStableCopy(externalDuringCopyBefore,
                externalDuringCopyTimer, out ClipboardCopyResult externalDuringCopyResult),
            "external Clipboard change during stabilization is captured");
        externalDuringCopyWriter.Join();
        Check(externalDuringCopyResult.Text == "source held in memory" &&
              externalDuringCopyResult.NewExternalSnapshot != null &&
              !externalDuringCopyResult.ClipboardContainsSourceText,
            "source remains internal while external snapshot becomes restore state");
        Check(ClipboardService.RestoreIfUnchanged(stabilizationSnapshot,
                externalDuringCopyResult.LastSequence) == ClipboardRestoreResult.Restored,
            "stabilization tests restore original Clipboard");

        using var viewer = new LogViewerForm();
        viewer.CreateControl();
        Check(viewer.Controls.OfType<System.Windows.Forms.TabControl>().Single().TabPages.Count == 3,
            "log viewer includes scanner and hotkey watchdog tabs");

        using var scanner = new ScannerInputService(new AppSettings());
        using var settings = new SettingsShellForm(new AppSettings(), scanner);
        settings.CreateControl();
        var scannerInput = (System.Windows.Forms.TextBox)typeof(SettingsShellForm)
            .GetField("_scannerScanBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
        var wordHotkey = (System.Windows.Forms.Button)typeof(SettingsShellForm)
            .GetField("_wordHotkey", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
        var selectedHotkey = (System.Windows.Forms.Button)typeof(SettingsShellForm)
            .GetField("_selectedHotkey", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
        Check(wordHotkey.Enabled && wordHotkey.Text == "Insert",
            "last-word hotkey is available in Text correction");
        Check(selectedHotkey.Enabled && selectedHotkey.Text == "Pause",
            "selected-text hotkey is configurable in Text correction");
        var scannerEnabled = (System.Windows.Forms.CheckBox)typeof(SettingsShellForm)
            .GetField("_scannerEnabled", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
        Check(scannerEnabled.Text == UiText.Get("scanner_enable"),
            "scanner configuration is available");
        Check(scannerInput.ReadOnly && scannerInput.Text == UiText.Get("scanner_scan_here"),
            "scanner configuration provides a scan field");
    }
    catch (Exception ex) { staFailure = ex; }
});
staThread.SetApartmentState(ApartmentState.STA);
staThread.Start();
staThread.Join();
if (staFailure != null) throw staFailure;

var worker = typeof(TextReplacementService).Assembly.GetType("LayoutFixer.CorrectionWorker")!;
var run = worker.GetMethod("TryRun")!;
var busy = worker.GetProperty("IsBusy")!;
using var entered = new ManualResetEventSlim();
using var release = new ManualResetEventSlim();
Action blocked = () => { entered.Set(); release.Wait(TimeSpan.FromSeconds(5)); };
Check((bool)run.Invoke(null, new object[] { blocked })!, "worker accepts correction");
Check(entered.Wait(TimeSpan.FromSeconds(2)), "worker starts");
Check(!(bool)run.Invoke(null, new object[] { (Action)(() => { }) })!, "parallel correction rejected");
release.Set();
Check(SpinWait.SpinUntil(() => !(bool)busy.GetValue(null)!, 2000), "worker releases operation guard");

Check(HotkeyDefinition.Parse("Insert").SetEquals(new[] { System.Windows.Forms.Keys.Insert }),
    "Insert parses as an exact one-key hotkey");
Check(!HotkeyDefinition.Parse("Insert").SetEquals(HotkeyDefinition.Parse("Ctrl+Insert")) &&
      !HotkeyDefinition.Parse("Insert").SetEquals(HotkeyDefinition.Parse("Shift+Insert")) &&
      !HotkeyDefinition.Parse("Insert").SetEquals(HotkeyDefinition.Parse("Alt+Insert")),
    "modified Insert combinations do not equal the last-word hotkey");
Check(HotkeyDefinition.Parse("Pause").SetEquals(new[] { System.Windows.Forms.Keys.Pause }),
    "Pause parses as the selected-text hotkey");
Check(HotkeyDefinition.IsModifierOnly(HotkeyDefinition.Parse("Ctrl+Shift")),
    "Ctrl+Shift is recognized as a modifier-only hotkey");
Check(!HotkeyDefinition.IsModifierOnly(HotkeyDefinition.Parse("Ctrl+Shift+Left")) &&
      !HotkeyDefinition.IsModifierOnly(HotkeyDefinition.Parse("Insert")),
    "application shortcuts and Insert are not modifier-only hotkeys");

Console.WriteLine($"{passed} regression checks passed.");

