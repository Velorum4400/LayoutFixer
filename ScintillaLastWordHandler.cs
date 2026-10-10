using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace LayoutFixer;

// Scintilla positions and text buffers are UTF-8 bytes. Because the editor is in another
// process, all buffers passed to Scintilla are allocated in that process.
internal static class ScintillaLastWordHandler
{
    private const uint SciGetLength = 2006;
    private const uint SciGetCurrentPos = 2008;
    private const uint SciGetAnchor = 2009;
    private const uint SciSetSel = 2160;
    private const uint SciGetTextRange = 2162;
    private const uint SciBeginUndoAction = 2078;
    private const uint SciEndUndoAction = 2079;
    private const uint SciGetCodePage = 2137;
    private const uint SciSetTargetStart = 2190;
    private const uint SciSetTargetEnd = 2192;
    private const uint SciReplaceTarget = 2194;
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessVmWrite = 0x0020;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint MemCommitReserve = 0x3000;
    private const uint MemRelease = 0x8000;
    private const uint PageReadWrite = 0x04;
    private const uint TimeoutMilliseconds = 250;
    private const int Utf8CodePage = 65001;
    private const int MaximumDocumentBytes = 1_000_000;
    private const int MaximumLoggedTextCharacters = 512;

    internal static bool TryReplace(TextReplacementOperation operation, IntPtr targetWindow, IntPtr scintillaWindow,
        uint processId, out string result)
    {
        result = "ScintillaFailed";
        var total = Stopwatch.StartNew();
        Log(operation, "SCINTILLA LASTWORD BEGIN");
        IntPtr process = IntPtr.Zero;
        bool undoStarted = false;
        try
        {
            if (!IsStillTarget(operation, targetWindow, scintillaWindow))
            { result = "ScintillaFocusChanged"; return false; }
            if (!HasCompatibleBitness(processId, out string bitnessReason))
            { result = bitnessReason; return false; }
            process = OpenProcess(ProcessVmOperation | ProcessVmRead | ProcessVmWrite | ProcessQueryInformation,
                false, processId);
            if (process == IntPtr.Zero)
            { result = "ScintillaProcessAccessDenied"; return false; }

            if (!TrySend(scintillaWindow, SciGetCodePage, IntPtr.Zero, IntPtr.Zero, out IntPtr codePageResult))
            { result = "ScintillaGetCodePageFailed"; return false; }
            int codePage = unchecked((int)codePageResult.ToInt64());
            if (!TrySend(scintillaWindow, SciGetLength, IntPtr.Zero, IntPtr.Zero, out IntPtr documentLengthResult))
            { result = "ScintillaGetLengthFailed"; return false; }
            int documentLength = checked((int)documentLengthResult.ToInt64());
            Log(operation, $"TargetHwnd=0x{scintillaWindow.ToInt64():X} ProcessId={processId} CodePage={codePage} DocumentLengthBytes={documentLength}");
            if (codePage != Utf8CodePage)
            { result = "ScintillaUnsupportedCodePage"; return false; }
            if (documentLength < 0 || documentLength > MaximumDocumentBytes)
            { result = "ScintillaDocumentLengthOutOfRange"; return false; }
            if (!TrySend(scintillaWindow, SciGetCurrentPos, IntPtr.Zero, IntPtr.Zero, out IntPtr caretResult) ||
                !TrySend(scintillaWindow, SciGetAnchor, IntPtr.Zero, IntPtr.Zero, out IntPtr anchorResult))
            { result = "ScintillaGetSelectionFailed"; return false; }
            int caret = checked((int)caretResult.ToInt64());
            int anchor = checked((int)anchorResult.ToInt64());
            Log(operation, $"CaretPositionBytes={caret} AnchorPositionBytes={anchor}");
            if (caret != anchor) { result = "ScintillaExistingSelection"; return false; }
            if (caret < 0 || caret > documentLength) { result = "ScintillaCaretOutOfRange"; return false; }

            if (!TryReadRange(process, scintillaWindow, 0, documentLength, out byte[] documentBytes, out string readReason))
            { result = readReason; return false; }
            string document = Encoding.UTF8.GetString(documentBytes);
            int caretCharacter = ByteOffsetToCharacterOffset(documentBytes, caret);
            if (caretCharacter < 0) { result = "ScintillaCaretNotUtf8Boundary"; return false; }
            if (!WindowsSearchLastWordHandler.TryResolveWordRange(document, caretCharacter,
                    out int wordStartCharacter, out int wordLengthCharacters, out string direction, out string rangeReason))
            { result = "ScintillaWordNotFound"; return false; }
            int wordEndCharacter = wordStartCharacter + wordLengthCharacters;
            int wordStartBytes = Encoding.UTF8.GetByteCount(document.AsSpan(0, wordStartCharacter));
            int wordEndBytes = Encoding.UTF8.GetByteCount(document.AsSpan(0, wordEndCharacter));
            string original = document.Substring(wordStartCharacter, wordLengthCharacters);
            Log(operation, $"TextDirection={direction} WordRangeReason={rangeReason} WordStartBytes={wordStartBytes} WordEndBytes={wordEndBytes}");
            LogText(operation, "OriginalText", original);

            string converted = LayoutConverter.Convert(original, operation.SourceMap, operation.TargetMap, out int unchanged);
            Log(operation, $"ConversionSource={operation.SourceLayout.ShortName} ConversionTarget={operation.TargetLayout.ShortName} unchanged={unchanged}");
            LogText(operation, "ConvertedText", converted);
            if (LastWordLayoutOnlyCompletion.IsUnchanged(original, converted))
                return LastWordLayoutOnlyCompletion.TryComplete(operation, "Scintilla", out result);
            if (!IsStillTarget(operation, targetWindow, scintillaWindow))
            { result = "ScintillaFocusChangedBeforeReplacement"; return false; }

            byte[] convertedBytes = Encoding.UTF8.GetBytes(converted);
            int expectedCaret = wordStartBytes + convertedBytes.Length;
            byte[] expected = new byte[documentBytes.Length - (wordEndBytes - wordStartBytes) + convertedBytes.Length];
            Buffer.BlockCopy(documentBytes, 0, expected, 0, wordStartBytes);
            Buffer.BlockCopy(convertedBytes, 0, expected, wordStartBytes, convertedBytes.Length);
            Buffer.BlockCopy(documentBytes, wordEndBytes, expected, wordStartBytes + convertedBytes.Length,
                documentBytes.Length - wordEndBytes);

            if (!TrySend(scintillaWindow, SciBeginUndoAction, IntPtr.Zero, IntPtr.Zero, out _))
            { result = "ScintillaBeginUndoFailed"; return false; }
            undoStarted = true;
            if (!TrySend(scintillaWindow, SciSetTargetStart, (IntPtr)wordStartBytes, IntPtr.Zero, out _) ||
                !TrySend(scintillaWindow, SciSetTargetEnd, (IntPtr)wordEndBytes, IntPtr.Zero, out _))
            { result = "ScintillaSetTargetFailed"; return false; }
            if (!TryWriteReplacement(process, scintillaWindow, convertedBytes, out string writeReason))
            { result = writeReason; return false; }
            if (!TrySend(scintillaWindow, SciSetSel, (IntPtr)expectedCaret, (IntPtr)expectedCaret, out _))
            { result = "ScintillaSetCaretFailed"; return false; }
            if (!TrySend(scintillaWindow, SciEndUndoAction, IntPtr.Zero, IntPtr.Zero, out _))
            { result = "ScintillaEndUndoFailed"; return false; }
            undoStarted = false;

            if (!TrySend(scintillaWindow, SciGetLength, IntPtr.Zero, IntPtr.Zero, out IntPtr finalLengthResult))
            { result = "ScintillaVerifyLengthFailed"; return false; }
            int finalLength = checked((int)finalLengthResult.ToInt64());
            if (!TryReadRange(process, scintillaWindow, 0, finalLength, out byte[] finalBytes, out readReason))
            { result = readReason; return false; }
            bool verified = finalBytes.AsSpan().SequenceEqual(expected);
            if (!TrySend(scintillaWindow, SciGetCurrentPos, IntPtr.Zero, IntPtr.Zero, out IntPtr finalCaretResult))
            { result = "ScintillaVerifyCaretFailed"; return false; }
            int finalCaret = checked((int)finalCaretResult.ToInt64());
            Log(operation, $"ReplacementVerified={verified} FinalCaretPositionBytes={finalCaret}");
            if (!verified || finalCaret != expectedCaret)
            { result = "ScintillaReplacementUnverified"; return false; }

            LastWordLayoutResolver.RecordVerifiedConversion(operation, original, converted, wordStartCharacter,
                wordStartCharacter + converted.Length);
            LayoutSwitchVerificationResult switchResult = KeyboardLayoutService.SwitchLayoutAndVerify(
                operation.TargetWindow, operation.TargetLayout, out _, out _);
            bool switched = switchResult == LayoutSwitchVerificationResult.Success;
            Log(operation, $"LayoutSwitchVerified={switched}");
            result = switched ? "ScintillaSuccess" : "ScintillaReplacementSucceededLayoutSwitchFailed";
            return true;
        }
        catch (OverflowException) { result = "ScintillaPositionOutOfRange"; return false; }
        catch (Exception ex) { result = "Scintilla" + ex.GetType().Name; return false; }
        finally
        {
            if (undoStarted) TrySend(scintillaWindow, SciEndUndoAction, IntPtr.Zero, IntPtr.Zero, out _);
            if (process != IntPtr.Zero) CloseHandle(process);
            Log(operation, $"SCINTILLA LASTWORD END result={result} durationMs={total.ElapsedMilliseconds}");
        }
    }

    private static bool IsStillTarget(TextReplacementOperation operation, IntPtr target, IntPtr focused) =>
        TextReplacementService.ForegroundWindow == target && IsWindow(focused) && GetFocusForWindow(target) == focused;

    private static IntPtr GetFocusForWindow(IntPtr target)
    {
        uint thread = GetWindowThreadProcessId(target, out _);
        var info = new GuiThreadInfo { cbSize = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        return thread != 0 && GetGUIThreadInfo(thread, ref info) ? info.hwndFocus : IntPtr.Zero;
    }

    private static bool HasCompatibleBitness(uint processId, out string reason)
    {
        reason = "ScintillaCrossBitnessUnsupported";
        IntPtr process = OpenProcess(ProcessQueryInformation, false, processId);
        if (process == IntPtr.Zero) { reason = "ScintillaProcessAccessDenied"; return false; }
        try
        {
            if (!IsWow64Process(GetCurrentProcess(), out bool currentWow64) || !IsWow64Process(process, out bool targetWow64))
            { reason = "ScintillaBitnessCheckFailed"; return false; }
            return currentWow64 == targetWow64;
        }
        finally { CloseHandle(process); }
    }

    private static bool TryReadRange(IntPtr process, IntPtr scintilla, int start, int end, out byte[] bytes, out string reason)
    {
        bytes = Array.Empty<byte>(); reason = "ScintillaGetTextRangeFailed";
        if (start < 0 || end < start) { reason = "ScintillaRangeOutOfBounds"; return false; }
        int capacity = checked(end - start + 1);
        IntPtr remoteText = IntPtr.Zero, remoteRange = IntPtr.Zero;
        try
        {
            remoteText = VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)(uint)capacity, MemCommitReserve, PageReadWrite);
            remoteRange = VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)(uint)Marshal.SizeOf<SciTextRange>(), MemCommitReserve, PageReadWrite);
            if (remoteText == IntPtr.Zero || remoteRange == IntPtr.Zero) { reason = "ScintillaRemoteAllocationFailed"; return false; }
            var range = new SciTextRange { cpMin = (IntPtr)start, cpMax = (IntPtr)end, lpstrText = remoteText };
            IntPtr localRange = Marshal.AllocHGlobal(Marshal.SizeOf<SciTextRange>());
            try
            {
                Marshal.StructureToPtr(range, localRange, false);
                byte[] rangeBytes = new byte[Marshal.SizeOf<SciTextRange>()];
                Marshal.Copy(localRange, rangeBytes, 0, rangeBytes.Length);
                if (!WriteProcessMemory(process, remoteRange, rangeBytes, rangeBytes.Length, out _)) { reason = "ScintillaWriteRangeFailed"; return false; }
            }
            finally { Marshal.FreeHGlobal(localRange); }
            if (!TrySend(scintilla, SciGetTextRange, IntPtr.Zero, remoteRange, out _)) return false;
            byte[] raw = new byte[capacity];
            if (!ReadProcessMemory(process, remoteText, raw, raw.Length, out IntPtr copied) || copied.ToInt64() < end - start)
            { reason = "ScintillaReadRangeFailed"; return false; }
            bytes = raw[..(end - start)];
            return true;
        }
        finally
        {
            if (remoteRange != IntPtr.Zero) VirtualFreeEx(process, remoteRange, UIntPtr.Zero, MemRelease);
            if (remoteText != IntPtr.Zero) VirtualFreeEx(process, remoteText, UIntPtr.Zero, MemRelease);
        }
    }

    private static bool TryWriteReplacement(IntPtr process, IntPtr scintilla, byte[] replacement, out string reason)
    {
        reason = "ScintillaReplaceTargetFailed";
        IntPtr remote = VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)(uint)(replacement.Length + 1), MemCommitReserve, PageReadWrite);
        if (remote == IntPtr.Zero) { reason = "ScintillaRemoteAllocationFailed"; return false; }
        try
        {
            byte[] nulTerminated = new byte[replacement.Length + 1];
            Buffer.BlockCopy(replacement, 0, nulTerminated, 0, replacement.Length);
            if (!WriteProcessMemory(process, remote, nulTerminated, nulTerminated.Length, out _))
            { reason = "ScintillaWriteReplacementFailed"; return false; }
            if (!TrySend(scintilla, SciReplaceTarget, (IntPtr)replacement.Length, remote, out IntPtr result)) return false;
            if (result.ToInt64() != replacement.Length) { reason = "ScintillaReplaceTargetLengthMismatch"; return false; }
            return true;
        }
        finally { VirtualFreeEx(process, remote, UIntPtr.Zero, MemRelease); }
    }

    private static int ByteOffsetToCharacterOffset(byte[] utf8, int byteOffset)
    {
        if (byteOffset < 0 || byteOffset > utf8.Length) return -1;
        try
        {
            var strict = new UTF8Encoding(false, true);
            return strict.GetString(utf8, 0, byteOffset).Length;
        }
        catch (DecoderFallbackException) { return -1; }
    }

    private static bool TrySend(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, out IntPtr result) =>
        SendMessageTimeout(window, message, wParam, lParam, SmtoAbortIfHung, TimeoutMilliseconds, out result);
    private static void LogText(TextReplacementOperation operation, string name, string value) =>
        Log(operation, $"{name}=\"{Escape(value.Length <= MaximumLoggedTextCharacters ? value : value[..MaximumLoggedTextCharacters] + "…")}\"");
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
    private static void Log(TextReplacementOperation operation, string message) => DiagnosticLogStore.Write($"operation={operation.Id:N} {message}");

    [StructLayout(LayoutKind.Sequential)] private struct SciTextRange { public IntPtr cpMin, cpMax, lpstrText; }
    [StructLayout(LayoutKind.Sequential)] private struct GuiThreadInfo
    { public uint cbSize, flags; public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret; public Rect rcCaret; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int left, top, right, bottom; }
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool IsWow64Process(IntPtr process, out bool wow64);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, UIntPtr size, uint allocationType, uint protect);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, UIntPtr size, uint freeType);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out IntPtr read);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out IntPtr written);
}

