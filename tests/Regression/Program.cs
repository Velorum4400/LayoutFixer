using System.Reflection;
using LayoutFixer;

int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine("PASS: " + name);
    passed++;
}

Check(KeyboardLayoutService.RefreshLayouts(), "installed layouts refresh");
IReadOnlyList<KeyboardLayoutInfo> layouts = KeyboardLayoutService.Layouts;
Check(layouts.Count > 0, "installed layout list retained");
Check(layouts.Select(x => x.Handle).Distinct().Count() == layouts.Count, "layout handles are unique");
Check(LayoutConverter.Convert("text \t\r\n", layouts[0], layouts[0]) == "text \t\r\n",
    "same-layout conversion preserves text");
if (layouts.Count > 1)
{
    Check(KeyboardLayoutService.TryGetNext(layouts[0], out KeyboardLayoutInfo second) &&
          second.Handle == layouts[1].Handle, "next layout follows saved order");
    Check(KeyboardLayoutService.TryGetNext(layouts[^1], out KeyboardLayoutInfo first) &&
          first.Handle == layouts[0].Handle, "last layout wraps to first");
    Check(LayoutConverter.Convert(" \t\r\n", layouts[0], layouts[1]) == " \t\r\n",
        "whitespace survives cross-layout conversion");
}
KeyboardLayoutInfo? english = layouts.FirstOrDefault(x => (x.LanguageId & 0x03ff) == 0x09);
KeyboardLayoutInfo? russian = layouts.FirstOrDefault(x => (x.LanguageId & 0x03ff) == 0x19);
KeyboardLayoutInfo? hebrew = layouts.FirstOrDefault(x => (x.LanguageId & 0x03ff) == 0x0d);
if (english != null && russian != null)
{
    Check(LayoutConverter.Convert("ghbdtn", english, russian) == "привет",
        "physical key conversion English to Russian");
    Check(LayoutConverter.Convert("#", english, russian) == "№",
        "Shift punctuation conversion English to Russian");
}
if (english != null && hebrew != null)
    Check(LayoutConverter.Convert("akuo", english, hebrew) == "שלום",
        "physical key conversion English to Hebrew");

Check(typeof(TextReplacementService).GetMethod("TryReplaceAllText") != null,
    "text replacement coordinator is available");
Check(typeof(ClipboardService).GetMethod("WaitForTextChange") != null,
    "clipboard sequence wait is isolated in ClipboardService");
Check(typeof(KeyboardInputService).GetMethod("SelectAll") != null,
    "SendInput chords are isolated in KeyboardInputService");
Check(typeof(HotkeyService).GetEvents().Any(x => x.Name == "Pressed"),
    "hotkey service exposes only the trigger event");
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

        using var viewer = new LogViewerForm();
        viewer.CreateControl();
        Check(!viewer.Controls.OfType<System.Windows.Forms.TabControl>().Any(), "scanner log tab remains removed");

        using var settings = new SettingsShellForm(new AppSettings());
        settings.CreateControl();
        string unavailable = UiText.Get("temporarily_unavailable");
        var wordNotice = (System.Windows.Forms.Label)typeof(SettingsShellForm)
            .GetField("_wordUnavailable", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
        var scannerNotice = (System.Windows.Forms.Label)typeof(SettingsShellForm)
            .GetField("_scannerUnavailable", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
        Check(wordNotice.Text == unavailable && scannerNotice.Text == unavailable,
            "deferred features remain marked unavailable");
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

Console.WriteLine($"{passed} regression checks passed.");
