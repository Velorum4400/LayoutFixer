using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;

namespace LayoutFixer;

public sealed record KeyboardLayoutInfo(IntPtr Handle, ushort LanguageId, string DisplayName, string ShortName);
public sealed record KeyboardLayoutDiagnostic(uint TargetThreadId, uint LayoutFixerThreadId,
    IntPtr TargetThreadLayout, IntPtr LayoutFixerThreadLayout);
public sealed record LayoutResolutionDiagnostic(IntPtr TargetWindow, uint TargetProcessId,
    uint TargetThreadId, uint CurrentProcessId, uint CurrentThreadId, IntPtr TargetThreadLayout,
    IntPtr CurrentThreadLayout, string CachedLayouts);
public sealed record GuiInputContextDiagnostic(IntPtr FocusWindow, uint FocusProcessId,
    uint FocusThreadId, IntPtr FocusThreadLayout, IntPtr CaretWindow, uint CaretProcessId,
    uint CaretThreadId, IntPtr CaretThreadLayout, bool QuerySucceeded);
public enum LayoutSwitchVerificationResult { Success, TargetWindowChanged, RequestFailed, VerificationFailed, Timeout }

public static class KeyboardLayoutService
{
    private const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
    private static readonly object Sync = new();
    private static KeyboardLayoutInfo[] _layouts = Array.Empty<KeyboardLayoutInfo>();
    private static Dictionary<IntPtr, KeyboardLayoutMap> _maps = new();

    public static IReadOnlyList<KeyboardLayoutInfo> Layouts
    {
        get { lock (Sync) return _layouts.ToArray(); }
    }

    public static bool RefreshLayouts()
    {
        try
        {
            int count = GetKeyboardLayoutList(0, null);
            if (count <= 0)
                return false;

            var handles = new IntPtr[count];
            int actual = GetKeyboardLayoutList(handles.Length, handles);
            if (actual <= 0)
                return false;

            KeyboardLayoutInfo[] refreshed = handles.Take(actual)
                .Where(handle => handle != IntPtr.Zero)
                .Distinct()
                .Select(CreateInfo)
                .ToArray();
            if (refreshed.Length == 0)
                return false;

            Dictionary<IntPtr, KeyboardLayoutMap> existingMaps;
            lock (Sync) existingMaps = new Dictionary<IntPtr, KeyboardLayoutMap>(_maps);
            foreach (KeyboardLayoutInfo layout in refreshed)
                if (!existingMaps.ContainsKey(layout.Handle))
                    existingMaps[layout.Handle] = KeyboardLayoutMap.Build(layout);

            lock (Sync)
            {
                _layouts = refreshed;
                _maps = existingMaps;
            }
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLogStore.Write($"RefreshLayouts failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public static bool TryGetCurrentAndNext(
        IntPtr targetWindow,
        out KeyboardLayoutInfo source,
        out KeyboardLayoutInfo target)
    {
        source = default!;
        target = default!;
        IntPtr currentHandle = GetLayoutForWindow(targetWindow);
        if (currentHandle == IntPtr.Zero)
            return false;

        KeyboardLayoutInfo[] snapshot;
        lock (Sync) snapshot = _layouts.ToArray();
        int index = FindLayout(snapshot, currentHandle);
        if (index < 0)
        {
            if (!RefreshLayouts())
                return false;
            lock (Sync) snapshot = _layouts.ToArray();
            index = FindLayout(snapshot, currentHandle);
        }

        if (index < 0)
            return false;

        source = snapshot[index];
        return TryGetNext(source, out target);
    }

    public static bool TryGetNext(KeyboardLayoutInfo source, out KeyboardLayoutInfo target)
    {
        KeyboardLayoutInfo[] snapshot;
        lock (Sync) snapshot = _layouts.ToArray();
        int index = FindLayout(snapshot, source.Handle);
        if (index < 0 || snapshot.Length < 2)
        {
            target = default!;
            return false;
        }
        target = snapshot[(index + 1) % snapshot.Length];
        return true;
    }

    public static bool TryGetEnglish(out KeyboardLayoutInfo english)
    {
        KeyboardLayoutInfo[] snapshot;
        lock (Sync) snapshot = _layouts.ToArray();
        english = snapshot.FirstOrDefault(layout => (layout.LanguageId & 0x03ff) == 0x09)!;
        return english != null;
    }

    public static bool TryGetByPrimaryLanguage(ushort languageId, out KeyboardLayoutInfo layout)
    {
        KeyboardLayoutInfo[] snapshot;
        lock (Sync) snapshot = _layouts.ToArray();
        layout = snapshot.FirstOrDefault(item => (item.LanguageId & 0x03ff) == languageId)!;
        return layout != null;
    }

    public static bool TryGetCurrentLayout(IntPtr targetWindow, out KeyboardLayoutInfo current)
    {
        current = default!;
        IntPtr handle = GetLayoutForWindow(targetWindow);
        if (handle == IntPtr.Zero) return false;
        KeyboardLayoutInfo[] snapshot;
        lock (Sync) snapshot = _layouts.ToArray();
        int index = FindLayout(snapshot, handle);
        if (index < 0 && RefreshLayouts()) { lock (Sync) snapshot = _layouts.ToArray(); index = FindLayout(snapshot, handle); }
        if (index < 0) return false;
        current = snapshot[index];
        return true;
    }

    public static KeyboardLayoutDiagnostic GetLayoutDiagnostic(IntPtr targetWindow)
    {
        uint targetThreadId = targetWindow == IntPtr.Zero ? 0 : GetWindowThreadProcessId(targetWindow, IntPtr.Zero);
        uint layoutFixerThreadId = GetCurrentThreadId();
        return new KeyboardLayoutDiagnostic(targetThreadId, layoutFixerThreadId,
            GetKeyboardLayout(targetThreadId), GetKeyboardLayout(layoutFixerThreadId));
    }

    public static LayoutResolutionDiagnostic GetResolutionDiagnostic(IntPtr targetWindow)
    {
        uint targetProcessId = 0;
        uint targetThreadId = targetWindow == IntPtr.Zero ? 0 :
            GetWindowThreadProcessIdForDiagnostic(targetWindow, out targetProcessId);
        if (targetWindow == IntPtr.Zero) targetProcessId = 0;
        uint currentThreadId = GetCurrentThreadId();
        KeyboardLayoutInfo[] snapshot;
        lock (Sync) snapshot = _layouts.ToArray();
        string cached = string.Join(",", snapshot.Select(layout =>
            $"{layout.ShortName}:0x{layout.Handle.ToInt64():X}"));
        return new LayoutResolutionDiagnostic(targetWindow, targetProcessId, targetThreadId,
            (uint)System.Diagnostics.Process.GetCurrentProcess().Id, currentThreadId,
            GetKeyboardLayout(targetThreadId), GetKeyboardLayout(currentThreadId), cached);
    }

    public static GuiInputContextDiagnostic GetGuiInputContextDiagnostic(uint targetThreadId)
    {
        var info = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>() };
        if (targetThreadId == 0 || !GetGUIThreadInfo(targetThreadId, ref info))
            return new GuiInputContextDiagnostic(IntPtr.Zero, 0, 0, IntPtr.Zero,
                IntPtr.Zero, 0, 0, IntPtr.Zero, false);
        uint focusProcessId = 0, caretProcessId = 0;
        uint focusThreadId = info.hwndFocus == IntPtr.Zero ? 0 :
            GetWindowThreadProcessIdForDiagnostic(info.hwndFocus, out focusProcessId);
        uint caretThreadId = info.hwndCaret == IntPtr.Zero ? 0 :
            GetWindowThreadProcessIdForDiagnostic(info.hwndCaret, out caretProcessId);
        return new GuiInputContextDiagnostic(info.hwndFocus, focusProcessId, focusThreadId,
            GetKeyboardLayout(focusThreadId), info.hwndCaret, caretProcessId, caretThreadId,
            GetKeyboardLayout(caretThreadId), true);
    }

    public static bool TryGetMap(KeyboardLayoutInfo layout, out KeyboardLayoutMap map)
    {
        lock (Sync)
            return _maps.TryGetValue(layout.Handle, out map!);
    }

    public static bool SwitchLayout(IntPtr targetWindow, KeyboardLayoutInfo target)
    {
        if (targetWindow == IntPtr.Zero || target.Handle == IntPtr.Zero)
            return false;

        IntPtr inputContextWindow = GetInputContextWindow(targetWindow);
        // The focused editor owns the active input context in applications such as
        // modern Notepad. Request the change there first, then notify the top-level
        // window as a compatibility fallback for traditional applications.
        bool sent = inputContextWindow != IntPtr.Zero &&
            PostMessage(inputContextWindow, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, target.Handle);
        if (inputContextWindow != targetWindow)
            sent = PostMessage(targetWindow, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, target.Handle) || sent;
        return sent;
    }

    public static LayoutSwitchVerificationResult SwitchLayoutAndVerify(IntPtr targetWindow, KeyboardLayoutInfo target,
        out IntPtr observedLayout, out long elapsedMilliseconds)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        observedLayout = IntPtr.Zero;
        if (targetWindow == IntPtr.Zero || GetForegroundWindow() != targetWindow || !IsWindow(targetWindow))
        { elapsedMilliseconds = stopwatch.ElapsedMilliseconds; return LayoutSwitchVerificationResult.TargetWindowChanged; }
        if (!SwitchLayout(targetWindow, target))
        { elapsedMilliseconds = stopwatch.ElapsedMilliseconds; return LayoutSwitchVerificationResult.RequestFailed; }
        while (stopwatch.ElapsedMilliseconds <= 120)
        {
            if (GetForegroundWindow() != targetWindow || !IsWindow(targetWindow))
            { elapsedMilliseconds = stopwatch.ElapsedMilliseconds; return LayoutSwitchVerificationResult.TargetWindowChanged; }
            observedLayout = GetLayoutForWindow(targetWindow);
            if (observedLayout == target.Handle)
            { elapsedMilliseconds = stopwatch.ElapsedMilliseconds; return LayoutSwitchVerificationResult.Success; }
            Thread.Sleep(5);
        }
        elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
        return observedLayout == IntPtr.Zero ? LayoutSwitchVerificationResult.VerificationFailed : LayoutSwitchVerificationResult.Timeout;
    }

    private static int FindLayout(IReadOnlyList<KeyboardLayoutInfo> layouts, IntPtr handle)
    {
        for (int i = 0; i < layouts.Count; i++)
            if (layouts[i].Handle == handle)
                return i;
        return -1;
    }

    private static IntPtr GetLayoutForWindow(IntPtr window)
    {
        if (window == IntPtr.Zero)
            return IntPtr.Zero;
        IntPtr inputContextWindow = GetInputContextWindow(window);
        uint threadId = GetWindowThreadProcessId(inputContextWindow, IntPtr.Zero);
        return GetKeyboardLayout(threadId);
    }

    private static KeyboardLayoutInfo CreateInfo(IntPtr handle)
    {
        ushort languageId = unchecked((ushort)(handle.ToInt64() & 0xffff));
        try
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(languageId);
            return new KeyboardLayoutInfo(handle, languageId, culture.EnglishName,
                culture.TwoLetterISOLanguageName.ToUpperInvariant());
        }
        catch
        {
            return new KeyboardLayoutInfo(handle, languageId, $"0x{languageId:X4}", $"{languageId:X4}");
        }
    }

    private static IntPtr GetInputContextWindow(IntPtr foreground)
    {
        uint threadId = GetWindowThreadProcessId(foreground, IntPtr.Zero);
        var info = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>() };
        if (!GetGUIThreadInfo(threadId, ref info)) return foreground;
        if (info.hwndFocus != IntPtr.Zero) return info.hwndFocus;
        if (info.hwndCaret != IntPtr.Zero) return info.hwndCaret;
        return foreground;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public uint cbSize, flags;
        public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public RECT rcCaret;
    }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int left, top, right, bottom; }

    [DllImport("user32.dll")] private static extern int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[]? lpList);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);
    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    private static extern uint GetWindowThreadProcessIdForDiagnostic(IntPtr hWnd, out uint processId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint idThread);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO info);
}

