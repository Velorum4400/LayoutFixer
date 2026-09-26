using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;

namespace LayoutFixer;

// Temporary passive instrumentation for investigating delayed Clipboard updates.
// It never changes Clipboard contents or the operation's sequence-number decision.
internal static class ClipboardDiagnostics
{
    public static readonly bool Enabled = true;
    private const int ObservationMilliseconds = 450;
    private const int PollMilliseconds = 5;
    private const uint CF_UNICODETEXT = 13;

    public static void ObserveAfterCopy(Stopwatch copyStopwatch, uint sequenceBeforeCopy,
        uint firstCopySequence, long firstCopyMilliseconds)
    {
        if (!Enabled)
            return;

        LogSnapshot(copyStopwatch, sequenceBeforeCopy, firstCopySequence, "first-change", firstCopyMilliseconds);
        uint previousSequence = firstCopySequence;
        while (copyStopwatch.ElapsedMilliseconds < ObservationMilliseconds)
        {
            uint currentSequence = ClipboardService.SequenceNumber;
            if (currentSequence != previousSequence)
            {
                LogSnapshot(copyStopwatch, previousSequence, currentSequence, "subsequent-change", null);
                previousSequence = currentSequence;
            }
            Thread.Sleep(PollMilliseconds);
        }
        DiagnosticLogStore.Write($"Clipboard diagnostic observation complete: duration={copyStopwatch.ElapsedMilliseconds} ms, finalSequence={previousSequence}");
    }

    private static void LogSnapshot(Stopwatch stopwatch, uint oldSequence, uint newSequence,
        string kind, long? observedAtMilliseconds)
    {
        long elapsed = observedAtMilliseconds ?? stopwatch.ElapsedMilliseconds;
        IntPtr owner = GetClipboardOwner();
        IntPtr openWindow = GetOpenClipboardWindow();
        string ownerInfo = DescribeWindow(owner);
        string openInfo = DescribeWindow(openWindow);
        string formatInfo = "unavailable";
        string textInfo = "unicodeText=False";

        try
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    var formats = new List<string>();
                    uint format = 0;
                    bool hasUnicode = false;
                    while ((format = EnumClipboardFormats(format)) != 0)
                    {
                        if (format == CF_UNICODETEXT)
                            hasUnicode = true;
                        formats.Add(FormatName(format));
                    }
                    formatInfo = "[" + string.Join(",", formats) + "]";
                    if (hasUnicode)
                    {
                        IntPtr data = GetClipboardData(CF_UNICODETEXT);
                        IntPtr pointer = data == IntPtr.Zero ? IntPtr.Zero : GlobalLock(data);
                        string text = pointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(pointer) ?? string.Empty;
                        if (pointer != IntPtr.Zero) GlobalUnlock(data);
                        textInfo = $"unicodeText=True,textLength={text.Length},textHash={Hash(text)}";
                    }
                    else
                        textInfo = "unicodeText=False";
                }
                finally { CloseClipboard(); }
            }
        }
        catch (ExternalException) { formatInfo = "unavailable(locked)"; }
        catch (Exception ex) { formatInfo = $"unavailable({ex.GetType().Name})"; }

        DiagnosticLogStore.Write($"Clipboard diagnostic {kind}: +{elapsed} ms seq {oldSequence} -> {newSequence}; owner={ownerInfo}; openClipboard={openInfo}; formats={formatInfo}; {textInfo}");
    }

    private static string DescribeWindow(IntPtr window)
    {
        if (window == IntPtr.Zero)
            return "HWND=0x0";
        uint processId;
        GetWindowThreadProcessId(window, out processId);
        string processName = "unknown";
        try { processName = Process.GetProcessById(unchecked((int)processId)).ProcessName + ".exe"; }
        catch { }
        return $"HWND=0x{window.ToInt64():X},PID={processId},process={processName}";
    }

    private static string FormatName(uint format)
    {
        if (format == CF_UNICODETEXT) return "CF_UNICODETEXT";
        var name = new StringBuilder(128);
        int length = GetClipboardFormatName(format, name, name.Capacity);
        return length > 0 ? name.ToString() : "CF_" + format;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(value)))[..16];

    [DllImport("user32.dll")] private static extern IntPtr GetClipboardOwner();
    [DllImport("user32.dll")] private static extern IntPtr GetOpenClipboardWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")] private static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClipboardFormatName(uint format, StringBuilder name, int maxCount);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr memory);
}

