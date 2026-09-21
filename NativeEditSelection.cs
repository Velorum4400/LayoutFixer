using System;
using System.Runtime.InteropServices;
using System.Text;

namespace LayoutFixer;

// Edit/RichEdit use logical character offsets even in RTL paragraphs. These
// system messages are marshalled by Windows across process boundaries.
internal static class NativeEditSelection
{
    public static bool TrySelect(IntPtr hwnd, out string? text, out string diagnostic)
    {
        text = null;
        var name = new StringBuilder(256);
        GetClassName(hwnd, name, name.Capacity);
        string className = name.ToString();
        bool richEdit = className.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase) ||
            className.Contains(".RichEdit", StringComparison.OrdinalIgnoreCase);
        bool edit = className.Equals("Edit", StringComparison.OrdinalIgnoreCase) ||
            className.StartsWith("WindowsForms10.EDIT.", StringComparison.OrdinalIgnoreCase);
        diagnostic = $"Native last-word: class={className}";
        if (!edit && !richEdit)
            return false;

        int length = (int)SendMessage(hwnd, 0x000E, IntPtr.Zero, IntPtr.Zero); // WM_GETTEXTLENGTH
        if (length < 0 || length > 2_000_000)
            return false;
        var buffer = new StringBuilder(length + 1);
        SendMessageText(hwnd, 0x000D, (IntPtr)buffer.Capacity, buffer); // WM_GETTEXT
        string document = buffer.ToString();
        // RichEdit character positions count a paragraph break as one CR.
        if (richEdit) document = document.Replace("\r\n", "\r");
        SendMessageSelection(hwnd, 0x00B0, out int start, out int end); // EM_GETSEL
        if (start < 0 || end < start || end > document.Length)
        {
            diagnostic += $", invalid selection={start}..{end}, textLength={document.Length}";
            return false;
        }
        if (start == end)
        {
            while (end > 0 && char.IsWhiteSpace(document[end - 1])) end--;
            start = end;
            while (start > 0 && !char.IsWhiteSpace(document[start - 1])) start--;
        }
        if (start == end)
        {
            diagnostic += ", no word before caret";
            return true; // Handled: do not select a different word with arrows.
        }
        SendMessage(hwnd, 0x00B1, (IntPtr)start, (IntPtr)end); // EM_SETSEL
        SendMessageSelection(hwnd, 0x00B0, out int actualStart, out int actualEnd);
        if (actualStart != start || actualEnd != end)
        {
            diagnostic += ", selection not confirmed";
            return true; // Do not let a later fallback expand a partial selection.
        }
        text = document.Substring(start, end - start);
        diagnostic += $", selection confirmed, length={text.Length}";
        return true;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageText(IntPtr hwnd, uint message, IntPtr count, StringBuilder text);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessageSelection(IntPtr hwnd, uint message, out int start, out int end);
}
