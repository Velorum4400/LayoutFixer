
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace LayoutFixer;

internal static class ChromiumNativeProbe
{
    private const uint WM_GETTEXT = 0x000D, WM_GETTEXTLENGTH = 0x000E, EM_GETSEL = 0x00B0;
    private const uint SMTO_ABORTIFHUNG = 0x0002, TimeoutMs = 250;
    private const int MaxChildren = 64, MaxParentDepth = 12, MaxPreview = 128, MaxMessageTargets = 12;

    public static void Run(TextReplacementOperation operation, IntPtr target, IntPtr focus, uint threadId)
    {
        var total = Stopwatch.StartNew();
        Log(operation, "CHROMIUM NATIVE PROBE BEGIN");
        var metadata = Stopwatch.StartNew();
        LogWindow(operation, "SOURCE", focus, 0);
        Log(operation, $"ChromiumProbe root=0x{GetAncestor(focus, 2).ToInt64():X} rootOwner=0x{GetAncestor(focus, 3).ToInt64():X}");
        Log(operation, $"ChromiumProbe metadataElapsedMs={Ms(metadata)}");

        var parents = Stopwatch.StartNew();
        IntPtr current = focus;
        for (int depth = 0; depth < MaxParentDepth && current != IntPtr.Zero; depth++)
        {
            LogWindow(operation, "PARENT", current, depth);
            IntPtr parent = GetParent(current);
            if (parent == current) break;
            current = parent;
        }
        Log(operation, $"ChromiumProbe parentsElapsedMs={Ms(parents)}");

        var childrenTimer = Stopwatch.StartNew();
        var children = new List<IntPtr>();
        var handle = GCHandle.Alloc(children);
        bool completed;
        try { completed = EnumChildWindows(focus, CollectChild, GCHandle.ToIntPtr(handle)); }
        finally { handle.Free(); }
        bool truncated = children.Count >= MaxChildren || !completed;
        int editCount = 0, richEditCount = 0, rendererCount = 0, messageTargets = 0;
        foreach (IntPtr child in children)
        {
            string className = ClassName(child);
            LogWindow(operation, "CHILD", child, DepthFrom(child, focus));
            bool edit = string.Equals(className, "Edit", StringComparison.Ordinal);
            bool rich = string.Equals(className, "RichEditD2DPT", StringComparison.Ordinal);
            bool renderer = className.Contains("Chrome_RenderWidgetHostHWND", StringComparison.Ordinal);
            if (edit) { editCount++; Log(operation, $"ChromiumProbe candidate hwnd=0x{child.ToInt64():X} class=\"{className}\" reason=KnownEditClass"); }
            if (rich) { richEditCount++; Log(operation, $"ChromiumProbe candidate hwnd=0x{child.ToInt64():X} class=\"{className}\" reason=KnownRichEditClass"); }
            if (renderer) rendererCount++;
            if (messageTargets < MaxMessageTargets && (edit || rich || renderer || className.StartsWith("Chrome_", StringComparison.Ordinal)))
            {
                ProbeMessages(operation, child, className);
                messageTargets++;
            }
        }
        if (truncated) Log(operation, $"ChromiumProbe enumerationTruncated=True limit={MaxChildren}");
        Log(operation, $"ChromiumProbe childrenElapsedMs={Ms(childrenTimer)}");

        var gtiTimer = Stopwatch.StartNew();
        LogGuiThreadInfo(operation, threadId);
        Log(operation, $"ChromiumProbe gtiElapsedMs={Ms(gtiTimer)}");

        var messagesTimer = Stopwatch.StartNew();
        ProbeMessages(operation, focus, ClassName(focus));
        Log(operation, $"ChromiumProbe messageElapsedMs={Ms(messagesTimer)}");
        Log(operation, "CHROMIUM NATIVE PROBE SUMMARY");
        Log(operation, $"ChromiumProbe foreground=0x{GetForegroundWindow().ToInt64():X} focus=0x{focus.ToInt64():X} childCount={children.Count} nativeEditCount={editCount} nativeRichEditCount={richEditCount} rendererHwndCount={rendererCount} nativeTextCandidateFound={(editCount + richEditCount > 0)}");
        Log(operation, $"ChromiumProbe PERF metadata={Ms(metadata)} parents={Ms(parents)} children={Ms(childrenTimer)} messages={Ms(messagesTimer)} total={Ms(total)}");
        Log(operation, $"CHROMIUM NATIVE PROBE END durationMs={Ms(total)}");
    }

    private static bool CollectChild(IntPtr hwnd, IntPtr data)
    {
        var list = (List<IntPtr>)GCHandle.FromIntPtr(data).Target!;
        if (list.Count >= MaxChildren) return false;
        list.Add(hwnd); return true;
    }

    private static void ProbeMessages(TextReplacementOperation operation, IntPtr hwnd, string className)
    {
        if (!SendTimeout(hwnd, WM_GETTEXTLENGTH, UIntPtr.Zero, IntPtr.Zero, out UIntPtr length))
        { Log(operation, $"ChromiumProbe WM_GETTEXTLENGTH hwnd=0x{hwnd.ToInt64():X} class=\"{className}\" timeout=True"); return; }
        ulong count = length.ToUInt64();
        Log(operation, $"ChromiumProbe WM_GETTEXTLENGTH hwnd=0x{hwnd.ToInt64():X} class=\"{className}\" returnValue={count}");
        if (count <= 4096)
        {
            var text = new StringBuilder((int)count + 1);
            if (SendTimeout(hwnd, WM_GETTEXT, (UIntPtr)(uint)text.Capacity, text, out UIntPtr copied))
                Log(operation, $"ChromiumProbe WM_GETTEXT hwnd=0x{hwnd.ToInt64():X} copiedChars={copied.ToUInt64()} preview=\"{Escape(text.ToString())}\"");
            else Log(operation, $"ChromiumProbe WM_GETTEXT hwnd=0x{hwnd.ToInt64():X} timeout=True");
        }
        IntPtr a = Marshal.AllocHGlobal(4), b = Marshal.AllocHGlobal(4);
        try
        {
            if (SendTimeout(hwnd, EM_GETSEL, a, b, out UIntPtr raw))
                Log(operation, $"ChromiumProbe EM_GETSEL hwnd=0x{hwnd.ToInt64():X} rawResult={raw.ToUInt64()} start={Marshal.ReadInt32(a)} end={Marshal.ReadInt32(b)} semanticValidity=Unknown");
            else Log(operation, $"ChromiumProbe EM_GETSEL hwnd=0x{hwnd.ToInt64():X} timeout=True semanticValidity=Unknown");
        }
        finally { Marshal.FreeHGlobal(a); Marshal.FreeHGlobal(b); }
    }

    private static void LogGuiThreadInfo(TextReplacementOperation operation, uint tid)
    {
        var info = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>() };
        if (!GetGUIThreadInfo(tid, ref info)) { Log(operation, "ChromiumProbe GTI success=False"); return; }
        Log(operation, $"ChromiumProbe GTI hwndActive=0x{info.hwndActive.ToInt64():X} class=\"{ClassName(info.hwndActive)}\"");
        Log(operation, $"ChromiumProbe GTI hwndFocus=0x{info.hwndFocus.ToInt64():X} class=\"{ClassName(info.hwndFocus)}\"");
        Log(operation, $"ChromiumProbe GTI hwndCapture=0x{info.hwndCapture.ToInt64():X} hwndMenuOwner=0x{info.hwndMenuOwner.ToInt64():X} hwndMoveSize=0x{info.hwndMoveSize.ToInt64():X} hwndCaret=0x{info.hwndCaret.ToInt64():X} rcCaret={info.rcCaret.Left},{info.rcCaret.Top},{info.rcCaret.Right},{info.rcCaret.Bottom}");
    }

    private static void LogWindow(TextReplacementOperation operation, string kind, IntPtr hwnd, int depth)
    {
        uint pid = 0; uint tid = hwnd == IntPtr.Zero ? 0 : GetWindowThreadProcessId(hwnd, out pid);
        GetWindowRect(hwnd, out RECT rect); GetClientRect(hwnd, out RECT client);
        Log(operation, $"ChromiumProbe {kind} depth={depth} hwnd=0x{hwnd.ToInt64():X} class=\"{ClassName(hwnd)}\" pid={pid} tid={tid} parent=0x{GetParent(hwnd).ToInt64():X} visible={IsWindowVisible(hwnd)} enabled={IsWindowEnabled(hwnd)} style=0x{GetWindowLongPtr(hwnd, -16).ToInt64():X} exStyle=0x{GetWindowLongPtr(hwnd, -20).ToInt64():X} rect={rect.Left},{rect.Top},{rect.Right},{rect.Bottom} client={client.Left},{client.Top},{client.Right},{client.Bottom}");
    }

    private static int DepthFrom(IntPtr hwnd, IntPtr root) { int depth = 1; while (hwnd != root && hwnd != IntPtr.Zero && depth < MaxParentDepth) { hwnd = GetParent(hwnd); depth++; } return depth; }
    private static string ClassName(IntPtr hwnd) { if (hwnd == IntPtr.Zero) return string.Empty; var b = new StringBuilder(256); return GetClassName(hwnd, b, b.Capacity) == 0 ? string.Empty : b.ToString(); }
    private static bool SendTimeout(IntPtr h, uint m, UIntPtr w, IntPtr l, out UIntPtr r) => SendMessageTimeout(h, m, w, l, SMTO_ABORTIFHUNG, TimeoutMs, out r);
    private static bool SendTimeout(IntPtr h, uint m, UIntPtr w, StringBuilder l, out UIntPtr r) => SendMessageTimeout(h, m, w, l, SMTO_ABORTIFHUNG, TimeoutMs, out r);
    private static bool SendTimeout(IntPtr h, uint m, IntPtr w, IntPtr l, out UIntPtr r) => SendMessageTimeout(h, m, w, l, SMTO_ABORTIFHUNG, TimeoutMs, out r);
    private static string Escape(string value) => value.Length > MaxPreview ? Escape(value[..MaxPreview]) + "…" : value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
    private static string Ms(Stopwatch s) => (s.ElapsedTicks * 1000.0 / Stopwatch.Frequency).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "ms";
    private static void Log(TextReplacementOperation o, string m) => DiagnosticLogStore.Write($"operation={o.Id:N} {m}");

    [StructLayout(LayoutKind.Sequential)] private struct GUITHREADINFO { public uint cbSize, flags; public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret; public RECT rcCaret; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    private delegate bool EnumChildProc(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr h);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumChildProc callback, IntPtr data);
    [DllImport("user32.dll", SetLastError=true)] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder b, int max);
    [DllImport("user32.dll", SetLastError=true)] private static extern bool GetGUIThreadInfo(uint tid, ref GUITHREADINFO info);
    [DllImport("user32.dll", CharSet=CharSet.Unicode, EntryPoint="SendMessageTimeoutW")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SendMessageTimeout(IntPtr h,uint m,UIntPtr w,IntPtr l,uint f,uint t,out UIntPtr r);
    [DllImport("user32.dll", CharSet=CharSet.Unicode, EntryPoint="SendMessageTimeoutW")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SendMessageTimeout(IntPtr h,uint m,UIntPtr w,[Out] StringBuilder l,uint f,uint t,out UIntPtr r);
    [DllImport("user32.dll", CharSet=CharSet.Unicode, EntryPoint="SendMessageTimeoutW")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SendMessageTimeout(IntPtr h,uint m,IntPtr w,IntPtr l,uint f,uint t,out UIntPtr r);
}

