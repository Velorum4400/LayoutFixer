using System.Reflection;
using LayoutFixer;

int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine("PASS: " + name);
    passed++;
}

Check(LayoutConverter.Convert("руддщ", KeyboardLanguage.Russian, KeyboardLanguage.English) == "hello",
    "ordinary Russian conversion");
Check(LayoutConverter.Convert("יקךךם", KeyboardLanguage.Hebrew, KeyboardLanguage.English) == "hello",
    "ordinary Hebrew conversion");

string mixedRussian = "В ролике авторы Gamer's Nexus показали модели LG и Wi-Fi";
string mixedHebrew = LayoutConverter.Convert(mixedRussian, KeyboardLanguage.Russian, KeyboardLanguage.Hebrew);
string mixedEnglish = LayoutConverter.Convert(mixedHebrew, KeyboardLanguage.Hebrew, KeyboardLanguage.English);
Check(LayoutConverter.Convert(mixedEnglish, KeyboardLanguage.English, KeyboardLanguage.Russian) == mixedRussian,
    "layout cycle preserves embedded English names");

var chooseTarget = typeof(KeyboardLayout).GetMethod("TryChooseCorrectionTarget",
    BindingFlags.NonPublic | BindingFlags.Static)!;
var layouts = (IReadOnlyList<KeyboardLanguage>)new[]
    { KeyboardLanguage.English, KeyboardLanguage.Russian, KeyboardLanguage.Hebrew };
object?[] russianTarget = { KeyboardLanguage.Russian, KeyboardLanguage.Russian, layouts, null };
Check((bool)chooseTarget.Invoke(null, russianTarget)! &&
      (KeyboardLanguage)russianTarget[3]! == KeyboardLanguage.English,
    "Russian text under Russian layout targets English");

Check(typeof(TextFixer).GetMethod("TryFixAllText") != null, "full-text correction entry point remains available");
Check(typeof(TextFixer).GetMethod("TryFix") == null, "selection and last-word correction entry point removed");
Check(typeof(TextFixer).Assembly.GetType("LayoutFixer.SelectionPreserver") == null,
    "selection preservation removed");
Check(typeof(TextFixer).Assembly.GetType("LayoutFixer.NativeEditSelection") == null,
    "last-word native selection removed");
Check(typeof(TextFixer).Assembly.GetType("LayoutFixer.ScannerInputService") == null,
    "scanner service removed");
Check(typeof(AppSettings).GetProperty("KeepSelectionAfterCorrection") == null,
    "keep-selection setting removed");
Check(typeof(AppSettings).GetProperty("ScannerEnabled") == null,
    "scanner settings removed");
Check(typeof(AppSettings).GetProperty("LastWordEnabled") == null,
    "last-word settings removed");

Exception? nativeFailure = null;
var nativeThread = new Thread(() =>
{
    try
    {
        var capture = typeof(TextFixer).GetMethod("CaptureClipboardSnapshot",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var restore = typeof(TextFixer).GetMethod("RestoreClipboardSnapshot",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        object snapshot = capture.Invoke(null, null)!;
        try
        {
            var publish = typeof(TextFixer).Assembly.GetType("LayoutFixer.NativeClipboard")!
                .GetMethod("TrySetText")!;
            Check((bool)publish.Invoke(null, new object[] { "тест שלום 123" })!,
                "native Unicode clipboard publish");
            Check(System.Windows.Forms.Clipboard.GetText() == "тест שלום 123",
                "native clipboard survives owner window disposal");
        }
        finally { restore.Invoke(null, new[] { snapshot }); }

        using var viewer = new LogViewerForm();
        viewer.CreateControl();
        Check(!viewer.Controls.OfType<System.Windows.Forms.TabControl>().Any(),
            "scanner log tab removed");
        Check(viewer.Controls.OfType<System.Windows.Forms.TextBox>().Count() == 1,
            "diagnostic log remains available");

        using var settings = new SettingsShellForm(new AppSettings());
        settings.CreateControl();
        string unavailable = UiText.Get("temporarily_unavailable");
        var wordNotice = (System.Windows.Forms.Label)typeof(SettingsShellForm)
            .GetField("_wordUnavailable", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
        var scannerNotice = (System.Windows.Forms.Label)typeof(SettingsShellForm)
            .GetField("_scannerUnavailable", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
        Check(wordNotice.Text == unavailable && scannerNotice.Text == unavailable,
            "disabled features show temporarily unavailable");
    }
    catch (Exception ex) { nativeFailure = ex; }
});
nativeThread.SetApartmentState(ApartmentState.STA);
nativeThread.Start();
nativeThread.Join();
if (nativeFailure != null) throw nativeFailure;

var worker = typeof(TextFixer).Assembly.GetType("LayoutFixer.CorrectionWorker")!;
var run = worker.GetMethod("TryRun")!;
var busy = worker.GetProperty("IsBusy")!;
using var entered = new ManualResetEventSlim();
using var release = new ManualResetEventSlim();
Action blocked = () => { entered.Set(); release.Wait(TimeSpan.FromSeconds(5)); };
Check((bool)run.Invoke(null, new object[] { blocked })!, "worker accepts correction");
Check(entered.Wait(TimeSpan.FromSeconds(2)), "worker starts");
Check(!(bool)run.Invoke(null, new object[] { (Action)(() => { }) })!, "overlapping correction rejected");
release.Set();
Check(SpinWait.SpinUntil(() => !(bool)busy.GetValue(null)!, 2000), "worker releases guard");

Console.WriteLine($"{passed} regression checks passed.");
