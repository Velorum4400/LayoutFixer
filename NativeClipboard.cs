using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace LayoutFixer;

internal static class NativeClipboard
{
    // Publish eagerly rendered Unicode data without OLE's SetDataObject/flush
    // path, which can wait for the previous Chromium clipboard owner.
    public static bool TrySetText(string text, int timeoutMilliseconds = 500)
    {
        byte[] bytes = Encoding.Unicode.GetBytes(text + "\0");
        IntPtr memory = GlobalAlloc(0x0002, (UIntPtr)bytes.Length);
        if (memory == IntPtr.Zero) return false;
        var owner = new NativeWindow();
        bool opened = false;
        try
        {
            IntPtr pointer = GlobalLock(memory);
            if (pointer == IntPtr.Zero) return false;
            try { Marshal.Copy(bytes, 0, pointer, bytes.Length); }
            finally { GlobalUnlock(memory); }
            owner.CreateHandle(new CreateParams());
            var wait = Stopwatch.StartNew();
            while (!(opened = OpenClipboard(owner.Handle)))
            {
                if (wait.ElapsedMilliseconds >= timeoutMilliseconds) return false;
                Thread.Sleep(10);
            }
            if (!EmptyClipboard() || SetClipboardData(13, memory) == IntPtr.Zero) return false;
            memory = IntPtr.Zero; // Windows now owns the allocation.
            return true;
        }
        finally
        {
            if (opened) CloseClipboard();
            owner.DestroyHandle();
            if (memory != IntPtr.Zero) GlobalFree(memory);
        }
    }
    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr data);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr size);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr memory);
}
