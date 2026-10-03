using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace LayoutFixer;

// Deliberately narrow experiment: only the documented Win32 Edit control is supported.
internal static class NativeEditLastWordService
{
    private const uint EM_GETSEL = 0x00B0;
    private const uint EM_SETSEL = 0x00B1;
    private const uint EM_REPLACESEL = 0x00C2;
    private const uint WM_GETTEXT = 0x000D;
    private const uint WM_GETTEXTLENGTH = 0x000E;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint MessageTimeoutMilliseconds = 250;
    private const int MaxTextCharacters = 1_000_000;
    private const int MaximumLoggedTextCharacters = 512;

    public static bool TryReplace(TextReplacementOperation operation, Stopwatch total)
    {
        var timer = Stopwatch.StartNew();
        IntPtr target = operation.TargetWindow;
        Log(operation, $"NATIVE LASTWORD BEGIN targetWindow=0x{target.ToInt64():X}");

        if (target == IntPtr.Zero || GetForegroundWindow() != target || !IsWindow(target))
            return End(operation, "Failed", "stage=InitialTargetCheck reason=TargetChanged", timer);

        uint targetPid = 0;
        uint targetTid = GetWindowThreadProcessId(target, out targetPid);
        var focusTimer = Stopwatch.StartNew();
        if (!TryGetFocusedControl(targetTid, out IntPtr focusedControl))
            return End(operation, "Failed", "stage=GetFocusedControl reason=NoFocusedHwnd", timer);

        uint focusedPid = 0;
        uint focusedTid = GetWindowThreadProcessId(focusedControl, out focusedPid);
        string className = GetWindowClassName(focusedControl);
        bool supported = string.Equals(className, "Edit", StringComparison.Ordinal);
        Log(operation, $"NativeLastWord foregroundWindow=0x{GetForegroundWindow().ToInt64():X}");
        Log(operation, $"NativeLastWord targetPid={targetPid} targetTid={targetTid}");
        Log(operation, $"NativeLastWord focusedControl=0x{focusedControl.ToInt64():X}");
        Log(operation, $"NativeLastWord focusedPid={focusedPid} focusedTid={focusedTid}");
        Log(operation, $"NativeLastWord className=\"{Escape(className)}\"");
        Log(operation, $"NativeLastWord supported={supported}");
        if (focusedControl == IntPtr.Zero || !IsWindow(focusedControl) || focusedPid != targetPid)
            return End(operation, "Failed", "stage=GetFocusedControl reason=FocusedControlDoesNotMatchTarget", timer);
        if (!supported)
            return End(operation, "Unsupported", $"reason=FocusedControlClassNotSupported className=\"{Escape(className)}\"", timer);

        var selectionTimer = Stopwatch.StartNew();
        if (!TryGetSelection(focusedControl, out int selectionStart, out int selectionEnd))
            return End(operation, "Failed", "stage=EM_GETSEL reason=MessageFailed", timer);
        int caret = selectionEnd;
        Log(operation, $"NativeLastWord selection start={selectionStart} end={selectionEnd} caret={caret}");
        if (selectionStart != selectionEnd)
        {
            Log(operation, $"NativeLastWord existingSelection=True start={selectionStart} end={selectionEnd}");
            return End(operation, "NoWord", "reason=ExistingSelection", timer);
        }

        var textTimer = Stopwatch.StartNew();
        if (!TryGetText(operation, focusedControl, className, caret, out string text))
            return End(operation, "Failed", "stage=GetText reason=MessageFailed", timer);
        if (caret < 0 || caret > text.Length)
            return End(operation, "Failed", $"stage=GetText reason=CaretOutOfRange caret={caret} textLength={text.Length}", timer);
        Log(operation, $"NativeLastWord textLength={text.Length} caret={caret}");

        string beforeCaret = text[..caret];
        LastWordSelectionAnalysis analysis = LastWordSelectionAnalyzer.Analyze(beforeCaret);
        if (!analysis.HasFragment)
            return End(operation, "NoWord", "reason=NoFragmentBeforeCaret", timer);
        int wordStart = analysis.FragmentStart;
        int wordEnd = wordStart + analysis.FragmentLength;
        string fragment = beforeCaret.Substring(wordStart, analysis.FragmentLength);
        var convertTimer = Stopwatch.StartNew();
        string converted = LayoutConverter.Convert(fragment, operation.SourceMap, operation.TargetMap,
            out int unchangedCount);
        Log(operation, $"NativeLastWord wordRange start={wordStart} end={wordEnd}");
        Log(operation, $"NativeLastWord fragment=\"{Escape(fragment)}\"");
        Log(operation, $"NativeLastWord converted=\"{Escape(converted)}\" unchanged={unchangedCount}");

        if (GetForegroundWindow() != target || !IsWindow(focusedControl) ||
            !TryGetFocusedControl(targetTid, out IntPtr currentFocus) || currentFocus != focusedControl)
            return End(operation, "Failed", "stage=PreReplaceCheck reason=TargetChanged", timer);

        var replaceTimer = Stopwatch.StartNew();
        IntPtr setSelectionResult = SendMessage(focusedControl, EM_SETSEL, (IntPtr)wordStart, (IntPtr)wordEnd);
        Log(operation, $"NativeLastWord EM_SETSEL result=0x{setSelectionResult.ToInt64():X}");
        IntPtr replaceResult = SendMessageReplaceSel(focusedControl, EM_REPLACESEL, true, converted);
        Log(operation, $"NativeLastWord EM_REPLACESEL result=0x{replaceResult.ToInt64():X}");

        var verifyTimer = Stopwatch.StartNew();
        if (!TryGetText(operation, focusedControl, className, caret, out string afterText))
            return End(operation, "Failed", "stage=Verification reason=GetTextFailed", timer);
        bool replacementVerified = afterText.Length >= wordStart + converted.Length &&
            string.CompareOrdinal(afterText, wordStart, converted, 0, converted.Length) == 0;
        Log(operation, $"NativeLastWord verification success={replacementVerified}");
        if (!replacementVerified)
            return End(operation, "Failed", "stage=Verification reason=ReplacementNotObserved", timer);

        bool switched = KeyboardLayoutService.SwitchLayout(target, operation.TargetLayout);
        Log(operation, $"NativeLastWord layoutSwitch success={switched} target={operation.TargetLayout.DisplayName}");
        if (!switched)
            return End(operation, "Failed", "stage=LayoutSwitch reason=SwitchRequestFailed", timer);

        Log(operation, $"NativeLastWord PERF focus={Milliseconds(focusTimer)} selection={Milliseconds(selectionTimer)} getText={Milliseconds(textTimer)} convert={Milliseconds(convertTimer)} replace={Milliseconds(replaceTimer)} verify={Milliseconds(verifyTimer)} total={Milliseconds(total)}");
        return End(operation, "Success", "", timer);
    }

    private static bool TryGetFocusedControl(uint targetThreadId, out IntPtr focus)
    {
        focus = IntPtr.Zero;
        var info = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>() };
        if (targetThreadId == 0 || !GetGUIThreadInfo(targetThreadId, ref info))
            return false;
        focus = info.hwndFocus;
        return focus != IntPtr.Zero;
    }

    private static bool TryGetSelection(IntPtr edit, out int start, out int end)
    {
        IntPtr startMemory = Marshal.AllocHGlobal(sizeof(int));
        IntPtr endMemory = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            SendMessage(edit, EM_GETSEL, startMemory, endMemory);
            start = Marshal.ReadInt32(startMemory);
            end = Marshal.ReadInt32(endMemory);
            return start >= 0 && end >= 0;
        }
        finally
        {
            Marshal.FreeHGlobal(startMemory);
            Marshal.FreeHGlobal(endMemory);
        }
    }

    private static bool TryGetText(TextReplacementOperation operation, IntPtr edit, string className,
        int caret, out string text)
    {
        text = string.Empty;
        if (!SendMessageTimeout(edit, WM_GETTEXTLENGTH, UIntPtr.Zero, IntPtr.Zero,
                SMTO_ABORTIFHUNG, MessageTimeoutMilliseconds, out UIntPtr lengthResult))
        {
            Log(operation, $"NativeLastWord WM_GETTEXTLENGTH failed hwnd=0x{edit.ToInt64():X} className=\"{Escape(className)}\" timeoutMs={MessageTimeoutMilliseconds}");
            return false;
        }
        ulong reportedLength = lengthResult.ToUInt64();
        Log(operation, $"NativeLastWord WM_GETTEXTLENGTH result={reportedLength}");
        if (reportedLength > MaxTextCharacters)
        {
            Log(operation, $"NativeLastWord WM_GETTEXT failed hwnd=0x{edit.ToInt64():X} className=\"{Escape(className)}\" reason=TextTooLarge reportedLength={reportedLength}");
            return false;
        }
        if (reportedLength == 0 && caret > 0)
            Log(operation, $"NativeLastWord WARNING caret={caret} but WM_GETTEXTLENGTH=0");

        int requestedCapacity = checked((int)reportedLength + 1);
        var buffer = new StringBuilder(requestedCapacity);
        if (!SendMessageTimeout(edit, WM_GETTEXT, (UIntPtr)(uint)requestedCapacity, buffer,
                SMTO_ABORTIFHUNG, MessageTimeoutMilliseconds, out UIntPtr copiedResult))
        {
            Log(operation, $"NativeLastWord WM_GETTEXT failed hwnd=0x{edit.ToInt64():X} className=\"{Escape(className)}\" requestedCapacity={requestedCapacity} timeoutMs={MessageTimeoutMilliseconds}");
            return false;
        }
        ulong copied = copiedResult.ToUInt64();
        Log(operation, $"NativeLastWord WM_GETTEXT requestedCapacity={requestedCapacity} copiedChars={copied}");
        if (copied > (ulong)reportedLength || copied >= (ulong)requestedCapacity)
        {
            Log(operation, $"NativeLastWord WM_GETTEXT failed hwnd=0x{edit.ToInt64():X} className=\"{Escape(className)}\" reason=UnexpectedCopyLength returnValue={copied}");
            return false;
        }
        text = buffer.ToString();
        Log(operation, $"NativeLastWord text=\"{EscapeForLog(text)}\"");
        return true;
    }

    private static string GetWindowClassName(IntPtr window)
    {
        var buffer = new StringBuilder(256);
        return GetClassName(window, buffer, buffer.Capacity) == 0 ? string.Empty : buffer.ToString();
    }

    private static bool End(TextReplacementOperation operation, string result, string details, Stopwatch timer)
    {
        string suffix = string.IsNullOrEmpty(details) ? string.Empty : " " + details;
        Log(operation, $"NATIVE LASTWORD END result={result}{suffix} durationMs={Milliseconds(timer)}");
        return result == "Success";
    }

    private static string Milliseconds(Stopwatch timer) =>
        (timer.ElapsedTicks * 1000.0 / Stopwatch.Frequency).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "ms";

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\r", "\\r")
        .Replace("\n", "\\n").Replace("\t", "\\t");

    private static string EscapeForLog(string value) => Escape(value.Length <= MaximumLoggedTextCharacters
        ? value
        : value[..MaximumLoggedTextCharacters] + "…");

    private static void Log(TextReplacementOperation operation, string message) =>
        DiagnosticLogStore.Write($"operation={operation.Id:N} {message}");

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public uint cbSize, flags;
        public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public RECT rcCaret;
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetGUIThreadInfo(uint threadId, ref GUITHREADINFO info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int GetClassName(IntPtr window, StringBuilder className, int maxCount);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SendMessageTimeoutW")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SendMessageTimeout(IntPtr window,
        uint message, UIntPtr wParam, IntPtr lParam, uint flags, uint timeoutMilliseconds,
        out UIntPtr result);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SendMessageTimeoutW")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SendMessageTimeout(IntPtr window,
        uint message, UIntPtr wParam, [Out] StringBuilder lParam, uint flags, uint timeoutMilliseconds,
        out UIntPtr result);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")] private static extern IntPtr SendMessageReplaceSel(IntPtr window, uint message, [MarshalAs(UnmanagedType.Bool)] bool canUndo, [MarshalAs(UnmanagedType.LPWStr)] string replacement);
}

