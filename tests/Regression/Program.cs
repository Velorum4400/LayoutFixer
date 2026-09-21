using System.Reflection;
using System.IO;
using LayoutFixer;

// No desktop input is sent by these tests. Real UIA/clipboard behavior needs
// the editor-specific checks documented in ../last-word-manual.md.
var canBackspace = typeof(TextFixer).GetMethod("CanBackspaceByLength",
    BindingFlags.NonPublic | BindingFlags.Static)!;
int passed = 0;

void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine("PASS: " + name);
    passed++;
}

foreach (string word in new[] { "4400הקךםרוצ", "הקךםרוצ4400", "руддщ", "Ёж№1", "hello-123!" })
    Check((bool)canBackspace.Invoke(null, new object[] { word })!, "plain keyboard text: " + word);

foreach (string word in new[] { "", "hello world", "hello\r\n", "a😀", "e\u0301", "ש\u05b8", "a\u200f", "a\u200d" })
    Check(!(bool)canBackspace.Invoke(null, new object[] { word })!, "reject unsafe deletion: " + word);

// The reported log is a quick EN -> HE -> EN cycle; seed the existing
// recovery cache that preserves logical ordering across bidi UIA reads.
LayoutConverter.Convert("velorum4400", KeyboardLanguage.English, KeyboardLanguage.Hebrew);
Check(LayoutConverter.Convert("4400הקךםרוצ", KeyboardLanguage.Hebrew, KeyboardLanguage.English)
    == "velorum4400", "reported Hebrew + digits conversion");
Check(LayoutConverter.Convert("руддщ", KeyboardLanguage.Russian, KeyboardLanguage.English)
    == "hello", "ordinary Russian conversion");
Check(typeof(TextFixer).GetMethod("SendUnicodeText", BindingFlags.NonPublic | BindingFlags.Static) != null,
    "direct Unicode replacement is available without clipboard publication");

string marker = Path.Combine(AppContext.BaseDirectory, AppRuntime.PortableMarkerFileName);
File.WriteAllText(marker, "regression test");
try
{
    Check(AppRuntime.GetDataPath("diagnostic.log") == Path.Combine(AppContext.BaseDirectory, "data", "diagnostic.log"),
        "portable diagnostic path");
}
finally { File.Delete(marker); }

Exception? nativeFailure = null;
var nativeThread = new Thread(() =>
{
    try
    {
        var select = typeof(TextFixer).Assembly.GetType("LayoutFixer.NativeEditSelection")!
            .GetMethod("TrySelect")!;
        foreach (System.Windows.Forms.TextBoxBase editor in new System.Windows.Forms.TextBoxBase[]
            { new System.Windows.Forms.TextBox { Multiline = true }, new System.Windows.Forms.RichTextBox() })
        using (editor)
        {
            editor.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            foreach (string prefix in new[] { "prefix ", "line one\r\n", new string('x', 70000) + " " })
            {
                editor.Text = prefix + "הקךםרוצ4400  suffix";
                int wordStart = editor.Text.IndexOf("הקךםרוצ4400", StringComparison.Ordinal);
                editor.Select(wordStart + 13, 0);
                object?[] args = { editor.Handle, null, null };
                Check((bool)select.Invoke(null, args)! && (string?)args[1] == "הקךםרוצ4400",
                    editor.GetType().Name + " RTL word after prefix length " + prefix.Length);
                editor.SelectedText = "velorum4400";
                Check(editor.Text.EndsWith("velorum4400  suffix", StringComparison.Ordinal),
                    "replacement preserves suffix and spaces");
                Check(editor.Text[..wordStart] == prefix.Replace("\r\n", "\n") || editor.Text[..wordStart] == prefix,
                    "replacement preserves prefix");
            }
            editor.Text = "first second";
            editor.Select(1, 3);
            object?[] selectedArgs = { editor.Handle, null, null };
            Check((bool)select.Invoke(null, selectedArgs)! && (string?)selectedArgs[1] == "irs",
                "existing selection preserved");
            editor.Select(0, 0);
            object?[] emptyArgs = { editor.Handle, null, null };
            Check((bool)select.Invoke(null, emptyArgs)! && emptyArgs[1] == null && editor.SelectionLength == 0,
                "no selection at document start");
        }
        using var button = new System.Windows.Forms.Button();
        object?[] unsupported = { button.Handle, null, null };
        Check(!(bool)select.Invoke(null, unsupported)!, "non-edit controls retain UIA fallback");
        // Exercise the real clipboard, restoring every captured format afterwards.
        var capture = typeof(TextFixer).GetMethod("CaptureClipboardSnapshot", BindingFlags.NonPublic | BindingFlags.Static)!;
        var restore = typeof(TextFixer).GetMethod("RestoreClipboardSnapshot", BindingFlags.NonPublic | BindingFlags.Static)!;
        object snapshot = capture.Invoke(null, null)!;
        try
        {
            var publish = typeof(TextFixer).Assembly.GetType("LayoutFixer.NativeClipboard")!.GetMethod("TrySetText")!;
            Check((bool)publish.Invoke(null, new object[] { "тест שלום 123" })!, "native Unicode clipboard publish");
            Check(System.Windows.Forms.Clipboard.GetText() == "тест שלום 123", "native clipboard survives owner window disposal");
        }
        finally { restore.Invoke(null, new[] { snapshot }); }

        File.WriteAllText(marker, "regression test");
        try
        {
            var logs = typeof(TextFixer).Assembly.GetType("LayoutFixer.DiagnosticLogStore")!;
            var write = logs.GetMethod("Write")!;
            var read = logs.GetMethod("Read")!;
            var clear = logs.GetMethod("Clear")!;
            clear.Invoke(null, new object[] { false });
            clear.Invoke(null, new object[] { true });
            write.Invoke(null, new object[] { false, "========== START ==========\r\nCorrection completed\r\n========== END ==========" });
            write.Invoke(null, new object[] { true, "========== START scanner ==========\r\nBarcode completed\r\n========== END scanner ==========" });
            UiText.Language = "ru";
            using var viewer = new LogViewerForm();
            viewer.CreateControl();
            var tabs = viewer.Controls.OfType<System.Windows.Forms.TabControl>().Single();
            Check(tabs.TabPages.Count == 2, "log viewer has two tabs");
            var refresh = typeof(LogViewerForm).GetMethod("RefreshLog", BindingFlags.Instance | BindingFlags.NonPublic)!;
            refresh.Invoke(viewer, new object[] { 0 });
            using (var bitmap = new System.Drawing.Bitmap(viewer.Width, viewer.Height))
            {
                tabs.CreateControl();
                foreach (System.Windows.Forms.Control child in tabs.TabPages[0].Controls) { var handle = child.Handle; }
                tabs.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, tabs.Width, tabs.Height));
                tabs.TabPages[0].PerformLayout();
                foreach (System.Windows.Forms.Control child in tabs.TabPages[0].Controls)
                    child.DrawToBitmap(bitmap, new System.Drawing.Rectangle(
                        tabs.TabPages[0].Left + child.Left, tabs.TabPages[0].Top + child.Top, child.Width, child.Height));
                bitmap.Save(Path.Combine(AppContext.BaseDirectory, "log-viewer.png"));
            }
            var click = typeof(System.Windows.Forms.Button).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!;
            click.Invoke(tabs.TabPages[0].Controls.OfType<System.Windows.Forms.Button>().Single(), new object[] { EventArgs.Empty });
            Check((string)read.Invoke(null, new object[] { false })! == "", "diagnostic tab button clears diagnostic only");
            Check(((string)read.Invoke(null, new object[] { true })!).Contains("Barcode completed"), "scanner log survives diagnostic clear");
            write.Invoke(null, new object[] { false, "keep diagnostic" });
            click.Invoke(tabs.TabPages[1].Controls.OfType<System.Windows.Forms.Button>().Single(), new object[] { EventArgs.Empty });
            Check((string)read.Invoke(null, new object[] { true })! == "", "clear scanner log only");
            Check(((string)read.Invoke(null, new object[] { false })!).Contains("keep diagnostic"), "diagnostic log survives scanner clear");
            File.Delete(AppRuntime.GetDataPath("scanner_diagnostic.log"));
            File.WriteAllText(AppRuntime.GetDataPath("scaner_diagnostic.log"), "legacy scanner history");
            Check(((string)read.Invoke(null, new object[] { true })!).Contains("legacy scanner history"), "legacy scanner log migrated");
            Check(!File.Exists(AppRuntime.GetDataPath("scaner_diagnostic.log")), "legacy filename retired");
            var inject = typeof(TextFixer).Assembly.GetType("LayoutFixer.ScannerTextInjector")!.GetMethod("ReplacePreviousText")!;
            inject.Invoke(null, new object?[] { "", 0, null });
            string skipped = (string)read.Invoke(null, new object[] { true })!;
            Check(skipped.Contains("START scanner replacement") && skipped.Contains("END scanner replacement"), "skipped scanner action has START and END");
        }
        finally { File.Delete(marker); }
    }
    catch (Exception ex) { nativeFailure = ex; }
});
nativeThread.SetApartmentState(ApartmentState.STA);
nativeThread.Start();
nativeThread.Join();
if (nativeFailure != null)
{
    Console.Error.WriteLine(nativeFailure);
    Environment.Exit(1);
}
var worker = typeof(TextFixer).Assembly.GetType("LayoutFixer.CorrectionWorker")!;
var run = worker.GetMethod("TryRun")!;
var busy = worker.GetProperty("IsBusy")!;
using var entered = new ManualResetEventSlim();
using var release = new ManualResetEventSlim();
int callingThread = Environment.CurrentManagedThreadId;
int correctionThread = callingThread;
ApartmentState apartment = ApartmentState.Unknown;
Action blocked = () =>
{
    correctionThread = Environment.CurrentManagedThreadId;
    apartment = Thread.CurrentThread.GetApartmentState();
    entered.Set();
    release.Wait(TimeSpan.FromSeconds(5));
};
Check((bool)run.Invoke(null, new object[] { blocked })!, "worker accepts correction without waiting for completion");
Check(entered.Wait(TimeSpan.FromSeconds(2)), "worker started");
Check(correctionThread != callingThread && apartment == ApartmentState.STA,
    "correction runs on separate clipboard-compatible STA thread");
Check(!(bool)run.Invoke(null, new object[] { (Action)(() => { }) })!, "overlapping correction rejected");
release.Set();
Check(SpinWait.SpinUntil(() => !(bool)busy.GetValue(null)!, 2000), "guard released after completion");
Check((bool)run.Invoke(null, new object[] { (Action)(() => throw new InvalidOperationException("test failure")) })!,
    "worker accepts next correction");
Check(SpinWait.SpinUntil(() => !(bool)busy.GetValue(null)!, 2000), "guard released after exception");
Console.WriteLine($"{passed} regression checks passed.");
