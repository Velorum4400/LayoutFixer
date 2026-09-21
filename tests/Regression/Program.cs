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

Console.WriteLine($"{passed} regression checks passed.");
