using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;

namespace LayoutFixer;

public sealed record KeyboardLayoutInfo(IntPtr Handle, ushort LanguageId, string DisplayName, string ShortName);

public static class KeyboardLayoutService
{
    private const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
    private static readonly object Sync = new();
    private static KeyboardLayoutInfo[] _layouts = Array.Empty<KeyboardLayoutInfo>();

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

            lock (Sync) _layouts = refreshed;
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

    public static bool SwitchLayout(IntPtr targetWindow, KeyboardLayoutInfo target)
    {
        if (targetWindow == IntPtr.Zero || target.Handle == IntPtr.Zero)
            return false;

        bool sent = PostMessage(targetWindow, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, target.Handle);
        IntPtr focus = GetFocusedWindow(targetWindow);
        if (focus != IntPtr.Zero && focus != targetWindow)
            sent = PostMessage(focus, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, target.Handle) || sent;
        return sent;
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
        uint threadId = GetWindowThreadProcessId(window, IntPtr.Zero);
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

    private static IntPtr GetFocusedWindow(IntPtr foreground)
    {
        uint threadId = GetWindowThreadProcessId(foreground, IntPtr.Zero);
        var info = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>() };
        return GetGUIThreadInfo(threadId, ref info) ? info.hwndFocus : IntPtr.Zero;
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
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint idThread);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO info);
}
