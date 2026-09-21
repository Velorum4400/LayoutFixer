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
Console.WriteLine($"{passed} regression checks passed.");
