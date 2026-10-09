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
{
    Check(LayoutConverter.Convert("akuo", Map(english), Map(hebrew), out _) == "שלום",
        "physical key conversion English to Hebrew");
    string hebrewLower = LayoutConverter.Convert("ghbdtn", Map(english), Map(hebrew), out _);
    Check(LayoutConverter.Convert("Ghbdtn", Map(english), Map(hebrew), out _) == "G" + hebrewLower[1..] &&
          LayoutConverter.Convert("GHBDTN", Map(english), Map(hebrew), out _) == "GHBDTN",
        "English Shift state is preserved when converting to Hebrew");
    if (russian != null)
    {
        string mixedHebrew = LayoutConverter.Convert("Ghbdtn", Map(english), Map(hebrew), out _);
        var first = new TextReplacementOperation { TargetWindow = (IntPtr)123, SourceLayout = english, TargetLayout = hebrew, SourceMap = Map(english), TargetMap = Map(hebrew) };
        LastWordLayoutResolver.RecordVerifiedConversion(first, "Ghbdtn", mixedHebrew, 0, mixedHebrew.Length);
        var next = new TextReplacementOperation { TargetWindow = (IntPtr)123, SourceLayout = russian, TargetLayout = english, SourceMap = Map(russian), TargetMap = Map(english) };
        Check(LastWordLayoutResolver.Apply(next, mixedHebrew, 0, mixedHebrew.Length, out string contextReason) &&
              next.SourceLayout.Handle == hebrew.Handle && next.TargetLayout.Handle == russian.Handle &&
              contextReason == "PreviousVerifiedConversion",
            "verified mixed Hebrew result continues from previous target layout");
    }
}
if (english != null && russian != null && hebrew != null)
{
    // A Hebrew layout has no case distinction; verify the lossless physical-key cycle
    // with lowercase source separately from the explicit uppercase-to-Hebrew assertions.
    const string cycleSource = "привет, я исправленный текст. все ли прошло так, как надо? были ли проблемы с чем-то?";
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
      typeof(KeyboardInputService).GetMethod("SelectCharactersLeft") == null &&
      typeof(KeyboardInputService).GetMethod("SelectCharactersRight") == null,
    "obsolete exact last-word selection controls are removed");
Check(typeof(KeyboardInputService).GetMethod("CollapseSelectionToEnd") != null,
    "Monaco fallback can clear a verified temporary selection");
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
for (uint index = 0; index < 185; index++)
{
    HotkeyDiagnosticBuffer.Record(new HotkeyRecognitionDiagnostic(DateTime.UtcNow, "test", "event",
        index.ToString(), false, true, "[]", "[]", "[Insert]", "null", "null", "[]", "[]",
        true, false, "pending-created"));
}
string hotkeyDiagnostics = HotkeyDiagnosticBuffer.FormatTail(180);
Check(hotkeyDiagnostics.Contains("count=180") && hotkeyDiagnostics.Contains("key=5,") &&
       hotkeyDiagnostics.Contains("key=184,"),
    "hotkey recognition diagnostic buffer is bounded and ordered");
Check(typeof(TextReplacementService).Assembly.GetType("LayoutFixer.TextFixer") == null,
    "legacy combined text fixer removed");
Check(typeof(LayoutConverter).GetMethods(BindingFlags.Public | BindingFlags.Static)
    .All(method => !method.GetParameters().Any(parameter => parameter.ParameterType.Name.Contains("Clipboard"))),
    "layout converter has no Clipboard dependency");
Check(UiaTargetedReplacement.TryBuildPlan("цшт", 3, "win", out int shortStart, out int shortLength,
        out string shortFragment, out string shortExpected, out int shortCaret, out _) &&
      shortStart == 0 && shortLength == 3 && shortFragment == "цшт" && shortExpected == "win" && shortCaret == 3,
    "Chromium targeted plan replaces a word in an empty field");
Check(UiaTargetedReplacement.TryBuildPlan("hello цшт", 9, "win", out int middleStart, out int middleLength,
        out _, out string middleExpected, out int middleCaret, out _) &&
      middleStart == 6 && middleLength == 3 && middleExpected == "hello win" && middleCaret == 9,
    "Chromium targeted plan preserves text before a final word");
Check(UiaTargetedReplacement.TryBuildPlan("hello цшт world", 9, "win", out int embeddedStart, out _,
        out _, out string embeddedExpected, out int embeddedCaret, out _) &&
      embeddedStart == 6 && embeddedExpected == "hello win world" && embeddedCaret == 9,
    "Chromium targeted plan preserves text after the caret");
Check(UiaTargetedReplacement.TryBuildPlan("цшт tail", 3, "win", out int leadingStart, out _,
        out _, out string leadingExpected, out _, out _) && leadingStart == 0 && leadingExpected == "win tail",
    "Chromium targeted plan supports a word at document start");
string longChromiumText = new string('a', 6000) + " цшт";
Check(UiaTargetedReplacement.TryBuildPlan(longChromiumText, longChromiumText.Length, "win", out int longStart,
        out _, out _, out string longExpected, out int longCaret, out _) &&
      longStart == 6001 && longExpected.EndsWith(" win", StringComparison.Ordinal) && longCaret == longExpected.Length,
    "Chromium targeted plan supports documents longer than 5000 characters");
Check(UiaTargetedReplacement.TryBuildPlan("one\nцшт\nשךם", 7, "win", out _, out _, out _,
        out string paragraphExpected, out _, out _) && paragraphExpected == "one\nwin\nשךם",
    "Chromium targeted plan preserves paragraphs and adjacent Hebrew text");
Check(UiaTargetedReplacement.TryBuildPlan("abcd", 4, "я", out _, out _, out _,
        out string shorterExpected, out int targetedShorterCaret, out _) && shorterExpected == "я" && targetedShorterCaret == 1,
    "Chromium targeted plan supports replacement text of a different length");
Check(!UiaTargetedReplacement.TryBuildPlan("", 0, "win", out _, out _, out _, out _, out _, out string noWordReason) &&
      noWordReason == "NoFragmentBeforeCaret",
    "Chromium targeted plan rejects an absent last word");
Check(!UiaTargetedReplacement.TryBuildPlan("hello", 6, "win", out _, out _, out _, out _, out _, out string invalidCaretReason) &&
      invalidCaretReason == "CaretOutOfRange",
    "Chromium targeted plan rejects an unverified caret offset");


const int WmKeyDown = 0x0100;
const int WmKeyUp = 0x0101;
const int WmSysKeyDown = 0x0104;
const int WmSysKeyUp = 0x0105;
using (var staleTabProvider = new FakePhysicalKeyStateProvider())
using (var staleTab = new HotkeyService("TestInsert", staleTabProvider, installHook: false) { Hotkey = "Insert" })
{
    int dispatchCount = 0;
    staleTab.Pressed += () => dispatchCount++;
    staleTab.ProcessKeyEventForTesting(WmKeyDown, System.Windows.Forms.Keys.Tab);
    staleTabProvider.Set(System.Windows.Forms.Keys.Tab, false);
    staleTab.ProcessKeyEventForTesting(WmKeyDown, System.Windows.Forms.Keys.Insert);
    staleTab.ProcessKeyEventForTesting(WmKeyUp, System.Windows.Forms.Keys.Insert);
    Check(dispatchCount == 1 && staleTab.PressedKeysForTesting == "[]",
        "stale Tab is recovered and Insert dispatches exactly once");
}
using (var heldTabProvider = new FakePhysicalKeyStateProvider())
using (var heldTab = new HotkeyService("TestInsert", heldTabProvider, installHook: false) { Hotkey = "Insert" })
{
    int dispatchCount = 0;
    heldTab.Pressed += () => dispatchCount++;
    heldTab.ProcessKeyEventForTesting(WmKeyDown, System.Windows.Forms.Keys.Tab);
    heldTabProvider.Set(System.Windows.Forms.Keys.Tab, true);
    heldTab.ProcessKeyEventForTesting(WmKeyDown, System.Windows.Forms.Keys.Insert);
    heldTab.ProcessKeyEventForTesting(WmKeyUp, System.Windows.Forms.Keys.Insert);
    Check(dispatchCount == 0 && heldTab.PressedKeysForTesting == "[Tab]",
        "physically held Tab remains and blocks Insert");
}
using (var modifiersProvider = new FakePhysicalKeyStateProvider())
using (var modifiers = new HotkeyService("TestFull", modifiersProvider, installHook: false) { Hotkey = "Ctrl+Shift" })
{
    int dispatchCount = 0;
    modifiers.Pressed += () => dispatchCount++;
    modifiersProvider.Set(System.Windows.Forms.Keys.ControlKey, true);
    modifiers.ProcessKeyEventForTesting(WmKeyDown, System.Windows.Forms.Keys.ControlKey);
    modifiersProvider.Set(System.Windows.Forms.Keys.ShiftKey, true);
    modifiers.ProcessKeyEventForTesting(WmKeyDown, System.Windows.Forms.Keys.ShiftKey);
    modifiersProvider.Set(System.Windows.Forms.Keys.ShiftKey, false);
    modifiers.ProcessKeyEventForTesting(WmKeyUp, System.Windows.Forms.Keys.ShiftKey);
    modifiersProvider.Set(System.Windows.Forms.Keys.ControlKey, false);
    modifiers.ProcessKeyEventForTesting(WmKeyUp, System.Windows.Forms.Keys.ControlKey);
    modifiersProvider.Set(System.Windows.Forms.Keys.ShiftKey, true);
    modifiers.ProcessKeyEventForTesting(WmKeyDown, System.Windows.Forms.Keys.ShiftKey);
    modifiersProvider.Set(System.Windows.Forms.Keys.ControlKey, true);
    modifiers.ProcessKeyEventForTesting(WmKeyDown, System.Windows.Forms.Keys.ControlKey);
    modifiersProvider.Set(System.Windows.Forms.Keys.ControlKey, false);
    modifiers.ProcessKeyEventForTesting(WmKeyUp, System.Windows.Forms.Keys.ControlKey);
    modifiersProvider.Set(System.Windows.Forms.Keys.ShiftKey, false);
    modifiers.ProcessKeyEventForTesting(WmKeyUp, System.Windows.Forms.Keys.ShiftKey);
    Check(dispatchCount == 2 && modifiers.PressedKeysForTesting == "[]",
        "Ctrl Shift fires once in either press and release order");
}
using (var altTabProvider = new FakePhysicalKeyStateProvider())
using (var altTab = new HotkeyService("TestInsert", altTabProvider, installHook: false) { Hotkey = "Insert" })
{
    int dispatchCount = 0;
    altTab.Pressed += () => dispatchCount++;
    altTabProvider.Set(System.Windows.Forms.Keys.Menu, true);
    altTab.ProcessKeyEventForTesting(WmSysKeyDown, System.Windows.Forms.Keys.Menu);
    altTabProvider.Set(System.Windows.Forms.Keys.Tab, true);
    altTab.ProcessKeyEventForTesting(WmSysKeyDown, System.Windows.Forms.Keys.Tab);
    altTabProvider.Set(System.Windows.Forms.Keys.Menu, false);
    altTab.ProcessKeyEventForTesting(WmSysKeyUp, System.Windows.Forms.Keys.Menu);
    altTabProvider.Set(System.Windows.Forms.Keys.Tab, false); // Simulate a lost WM_SYSKEYUP for Tab.
    altTab.ProcessKeyEventForTesting(WmKeyDown, System.Windows.Forms.Keys.Insert);
    altTab.ProcessKeyEventForTesting(WmKeyUp, System.Windows.Forms.Keys.Insert);
    Check(dispatchCount == 1 && altTab.PressedKeysForTesting == "[]",
        "lost system Tab release is recovered before the next Insert");
}
using (var repeatedProvider = new FakePhysicalKeyStateProvider())
using (var repeated = new HotkeyService("TestInsert", repeatedProvider, installHook: false) { Hotkey = "Insert" })
{
    int dispatchCount = 0;
    repeated.Pressed += () => dispatchCount++;
    for (int index = 0; index < 2; index++)
    {
        repeated.ProcessKeyEventForTesting(WmKeyDown, System.Windows.Forms.Keys.Insert);
        repeated.ProcessKeyEventForTesting(WmKeyUp, System.Windows.Forms.Keys.Insert);
    }
    Check(dispatchCount == 2, "repeated Insert dispatches once per press");
}
using (var pendingProvider = new FakePhysicalKeyStateProvider())
using (var pending = new HotkeyService("TestInsert", pendingProvider, installHook: false) { Hotkey = "Insert" })
{
    int dispatchCount = 0;
    pending.Pressed += () => dispatchCount++;
    pendingProvider.Set(System.Windows.Forms.Keys.Insert, true);
    pending.ProcessKeyEventForTesting(WmKeyDown, System.Windows.Forms.Keys.Insert);
    pendingProvider.Set(System.Windows.Forms.Keys.Tab, true);
    pending.ProcessKeyEventForTesting(WmKeyDown, System.Windows.Forms.Keys.Tab);
    pendingProvider.Set(System.Windows.Forms.Keys.Insert, false);
    pendingProvider.Set(System.Windows.Forms.Keys.Tab, false);
    pending.ProcessKeyEventForTesting(WmKeyDown, System.Windows.Forms.Keys.N);
    Check(dispatchCount == 0 && pending.PressedKeysForTesting == "[N]" &&
          pending.PendingKeysForTesting == "null" && pending.SuppressedKeysForTesting == "[]",
        "recovery clears stale pending and suppressed state without a false dispatch");
}
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
Check(!HotkeyService.IsTargetHotkeyRejection(
          new HashSet<System.Windows.Forms.Keys> { System.Windows.Forms.Keys.H },
          HotkeyDefinition.Parse("Insert"), System.Windows.Forms.Keys.H, "extra-key"),
    "ordinary typing is not a hotkey recognition rejection");
Check(!HotkeyService.IsTargetHotkeyRejection(
          new HashSet<System.Windows.Forms.Keys> { System.Windows.Forms.Keys.ShiftKey },
          HotkeyDefinition.Parse("Ctrl+Shift"), System.Windows.Forms.Keys.ShiftKey, "extra-key"),
    "partial FullText chord is not a hotkey recognition rejection");
Check(HotkeyService.IsTargetHotkeyRejection(
          new HashSet<System.Windows.Forms.Keys> { System.Windows.Forms.Keys.Menu, System.Windows.Forms.Keys.Insert },
          HotkeyDefinition.Parse("Insert"), System.Windows.Forms.Keys.Insert, "extra-key"),
    "Insert with stale Alt is a hotkey recognition rejection");
Check(HotkeyService.IsTargetHotkeyRejection(
          new HashSet<System.Windows.Forms.Keys> { System.Windows.Forms.Keys.Menu, System.Windows.Forms.Keys.ControlKey, System.Windows.Forms.Keys.ShiftKey },
          HotkeyDefinition.Parse("Ctrl+Shift"), System.Windows.Forms.Keys.ShiftKey, "extra-key"),
    "FullText chord with stale Alt is a hotkey recognition rejection");
Check(UiaLastWordProbe.IsFocusedEditCandidate(true, true, true, false, false, true),
    "focused editable TextPattern candidate bypasses traversal");
Check(!UiaLastWordProbe.IsFocusedEditCandidate(true, false, true, false, false, true),
    "unfocused Edit candidate does not bypass traversal");
Check(!UiaLastWordProbe.IsFocusedEditCandidate(true, true, true, true, false, true),
    "password Edit candidate does not bypass traversal");

Check(UiaWriteProbe.TryBuildReplacement("hello Привет world", 12, "Ghbdtn", out int wordStart,
        out int wordEnd, out string word, out string wholeValue, out int expectedCaret, out string planReason) &&
      wordStart == 6 && wordEnd == 12 && word == "Привет" && wholeValue == "hello Ghbdtn world" &&
      expectedCaret == 12 && planReason == string.Empty,
    "UIA whole-value plan replaces a middle Unicode word and preserves expected caret offset");
Check(UiaWriteProbe.TryBuildReplacement("тест.", 5, "ntcn.", out _, out _, out _, out string endReplacement,
        out int endCaret, out _) && endReplacement == "ntcn." && endCaret == 5,
    "UIA whole-value plan supports punctuation at end of value");
Check(UiaWriteProbe.TryBuildReplacement("ab x", 4, "longer", out _, out _, out _, out string longerReplacement,
        out int longerCaret, out _) && longerReplacement == "ab longer" && longerCaret == 9,
    "UIA whole-value plan supports a longer converted fragment");
Check(UiaWriteProbe.TryBuildReplacement("ab longer", 9, "x", out _, out _, out _, out string shorterReplacement,
        out int shorterCaret, out _) && shorterReplacement == "ab x" && shorterCaret == 4,
    "UIA whole-value plan supports a shorter converted fragment");
Check(!UiaWriteProbe.TryBuildReplacement("hello", 0, "x", out _, out _, out _, out _, out int zeroCaret,
        out string zeroReason) && zeroCaret == 0 && zeroReason == "NoFragmentBeforeCaret",
    "UIA whole-value plan rejects a start-of-text caret with no word");
Check(!UiaWriteProbe.TryBuildReplacement("hello", 6, "x", out _, out _, out _, out _, out _, out string boundsReason) &&
      boundsReason == "CaretOutOfRange",
    "UIA whole-value plan rejects invalid caret bounds");
Check(!UiaWriteProbe.TryBuildReplacement("hello", 5, "hello", out _, out _, out _, out _, out _, out string unchangedReason) &&
      unchangedReason == "ConversionUnchanged",
    "UIA whole-value plan rejects unchanged conversion");
Check(LastWordLayoutResolver.Detect("Привет") == FragmentLayoutDetection.Russian &&
      LastWordLayoutResolver.Detect("привет123") == FragmentLayoutDetection.Russian,
    "Cyrillic fragments select Russian layout");
Check(LastWordLayoutResolver.Detect("שלום") == FragmentLayoutDetection.Hebrew &&
      LastWordLayoutResolver.Detect("שלום123") == FragmentLayoutDetection.Hebrew,
    "Hebrew fragments select Hebrew layout");
Check(LastWordLayoutResolver.Detect("hello") == FragmentLayoutDetection.English &&
      LastWordLayoutResolver.Detect("hello123") == FragmentLayoutDetection.English,
    "Latin fragments select English layout");
Check(LastWordLayoutResolver.Detect("123-456") == FragmentLayoutDetection.Ambiguous &&
      LastWordLayoutResolver.Detect("...") == FragmentLayoutDetection.Ambiguous,
    "numeric and punctuation fragments are ambiguous");
Check(LastWordLayoutResolver.Detect("teст") == FragmentLayoutDetection.Mixed &&
      LastWordLayoutResolver.Detect("тש") == FragmentLayoutDetection.Mixed,
    "mixed-script fragments remain mixed");
Check(NativeEditLastWordService.ResolveHandler("Chrome_WidgetWin_1", "Chrome_WidgetWin_1", true, true, out _) ==
      NativeEditLastWordService.NativeHandler.ChromiumProbe &&
      NativeEditLastWordService.ResolveHandler("Chrome_WidgetWin_1", "Chrome_RenderWidgetHostHWND", true, true, out _) ==
      NativeEditLastWordService.NativeHandler.ChromiumProbe,
    "Chromium widget and render-widget focus route to UIA probe");
Check(NativeEditLastWordService.ResolveHandler("Chrome_WidgetWin_1", "Unknown", true, true, out _) ==
      NativeEditLastWordService.NativeHandler.Unsupported &&
      NativeEditLastWordService.ResolveHandler("Chrome_WidgetWin_1", "Chrome_RenderWidgetHostHWND", false, true, out _) ==
      NativeEditLastWordService.NativeHandler.Unsupported &&
      NativeEditLastWordService.ResolveHandler("Chrome_WidgetWin_1", "Chrome_RenderWidgetHostHWND", true, false, out _) ==
      NativeEditLastWordService.NativeHandler.Unsupported,
    "Chromium routing rejects unsupported, foreign, and unowned focus windows");
Check(NativeEditLastWordService.ResolveHandler("Windows.UI.Core.CoreWindow", "Windows.UI.Core.CoreWindow", true, true, out _) ==
      NativeEditLastWordService.NativeHandler.WindowsSearchProbe &&
      NativeEditLastWordService.ResolveHandler("Windows.UI.Core.CoreWindow", "Other", true, true, out _) ==
      NativeEditLastWordService.NativeHandler.Unsupported &&
      NativeEditLastWordService.ResolveHandler("Windows.UI.Core.CoreWindow", "Windows.UI.Core.CoreWindow", false, true, out _) ==
      NativeEditLastWordService.NativeHandler.Unsupported,
    "CoreWindow route is limited to same-process Windows Search UIA diagnostics");
Check(NativeEditLastWordService.ResolveHandler("Notepad", "Edit", true, true, out _) == NativeEditLastWordService.NativeHandler.Edit &&
      NativeEditLastWordService.ResolveHandler("Notepad", "RichEditD2DPT", true, true, out _) == NativeEditLastWordService.NativeHandler.RichEdit,
    "native Edit and RichEdit routing remains unchanged");
Check(MonacoLastWordFallback.IsMonacoClassPair("native-edit-context", "monaco-editor focused") &&
      !MonacoLastWordFallback.IsMonacoClassPair("native-edit-context", "ProseMirror") &&
      !MonacoLastWordFallback.IsMonacoClassPair("other", "monaco-editor"),
    "Monaco fallback requires native edit context and Monaco parent");
Check(MonacoLastWordFallback.IsAccessibleDocumentText("Привет\r\nGhbdtn") &&
      !MonacoLastWordFallback.IsAccessibleDocumentText("Screen reader optimized mode is not available"),
    "Monaco fallback rejects accessibility service text");

Console.WriteLine($"{passed} regression checks passed.");

sealed class FakePhysicalKeyStateProvider : IPhysicalKeyStateProvider, IDisposable
{
    private readonly Dictionary<System.Windows.Forms.Keys, bool> _states = new();
    public void Set(System.Windows.Forms.Keys key, bool isDown) => _states[key] = isDown;
    public bool TryGetIsKeyDown(System.Windows.Forms.Keys key, out bool isDown) =>
        _states.TryGetValue(key, out isDown);
    public void Dispose() { }
}

