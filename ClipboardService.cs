using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace LayoutFixer;

internal enum ClipboardRestoreResult
{
    Restored,
    SkippedBecauseChanged,
    Failed
}

internal sealed class ClipboardSnapshot
{
    public DataObject? Data { get; init; }
    public bool HasData { get; init; }
    public int FormatCount { get; init; }
}

internal sealed class ClipboardCopyResult
{
    public string Text { get; init; } = string.Empty;
    public string TextHash { get; init; } = string.Empty;
    public uint LastSequence { get; init; }
    public int ChangeCount { get; init; }
    public long ElapsedMilliseconds { get; init; }
    public ClipboardSnapshot? NewExternalSnapshot { get; init; }
    public uint NewExternalSnapshotSequence { get; init; }
    public bool ClipboardContainsSourceText { get; init; }
}

internal static class ClipboardService
{
    public const int AccessTimeoutMilliseconds = 500;
    public const int ClipboardStablePeriodMilliseconds = 30;
    private const int PollMilliseconds = 5;

    public static uint SequenceNumber => GetClipboardSequenceNumber();

    public static bool TryCapture(out ClipboardSnapshot snapshot) =>
        TryCapture(out snapshot, AccessTimeoutMilliseconds);

    private static bool TryCapture(out ClipboardSnapshot snapshot, int timeoutMilliseconds)
    {
        var wait = Stopwatch.StartNew();
        do
        {
            try
            {
                IDataObject? source = Clipboard.GetDataObject();
                if (source == null)
                {
                    snapshot = new ClipboardSnapshot();
                    return true;
                }

                var copy = new DataObject();
                int copied = 0;
                foreach (string format in source.GetFormats(autoConvert: false))
                {
                    try
                    {
                        object? value = source.GetData(format, autoConvert: false);
                        if (value == null) continue;
                        if (value is Stream stream)
                        {
                            long oldPosition = stream.CanSeek ? stream.Position : 0;
                            if (stream.CanSeek) stream.Position = 0;
                            var clone = new MemoryStream();
                            stream.CopyTo(clone);
                            clone.Position = 0;
                            if (stream.CanSeek) stream.Position = oldPosition;
                            value = clone;
                        }
                        copy.SetData(format, autoConvert: false, value);
                        copied++;
                    }
                    catch (Exception ex)
                    {
                        DiagnosticLogStore.Write($"Clipboard snapshot skipped format '{format}': {ex.GetType().Name}");
                    }
                }

                snapshot = new ClipboardSnapshot
                {
                    Data = copied > 0 ? copy : null,
                    HasData = copied > 0,
                    FormatCount = copied
                };
                return true;
            }
            catch (ExternalException) { Thread.Sleep(10); }
        } while (wait.ElapsedMilliseconds < timeoutMilliseconds);

        snapshot = new ClipboardSnapshot();
        return false;
    }

    public static bool TryCaptureStable(out ClipboardSnapshot snapshot, out uint stableSequence,
        out long elapsedMilliseconds, int timeoutMilliseconds = AccessTimeoutMilliseconds)
    {
        var wait = Stopwatch.StartNew();
        do
        {
            uint before = SequenceNumber;
            int remaining = Math.Max(1, timeoutMilliseconds - (int)wait.ElapsedMilliseconds);
            if (TryCapture(out ClipboardSnapshot candidate, remaining))
            {
                uint after = SequenceNumber;
                if (before == after)
                {
                    snapshot = candidate;
                    stableSequence = after;
                    elapsedMilliseconds = wait.ElapsedMilliseconds;
                    return true;
                }
            }
            Thread.Sleep(PollMilliseconds);
        } while (wait.ElapsedMilliseconds < timeoutMilliseconds);

        snapshot = new ClipboardSnapshot();
        stableSequence = SequenceNumber;
        elapsedMilliseconds = wait.ElapsedMilliseconds;
        return false;
    }

    public static bool WaitForTextChange(uint previousSequence, out string text, out uint newSequence, out long waitMilliseconds)
    {
        var wait = Stopwatch.StartNew();
        do
        {
            uint sequence = SequenceNumber;
            if (sequence != previousSequence)
            {
                try
                {
                    text = Clipboard.ContainsText(TextDataFormat.UnicodeText)
                        ? Clipboard.GetText(TextDataFormat.UnicodeText)
                        : string.Empty;
                    newSequence = sequence;
                    waitMilliseconds = wait.ElapsedMilliseconds;
                    return true;
                }
                catch (ExternalException) { }
            }
            Thread.Sleep(10);
        } while (wait.ElapsedMilliseconds < AccessTimeoutMilliseconds);

        text = string.Empty;
        newSequence = SequenceNumber;
        waitMilliseconds = wait.ElapsedMilliseconds;
        return false;
    }

    public static bool WaitForStableCopy(uint sequenceBeforeCopy, Stopwatch copyTimer,
        out ClipboardCopyResult result)
    {
        uint lastSequence = sequenceBeforeCopy;
        long stableSince = copyTimer.ElapsedMilliseconds;
        string sourceText = string.Empty;
        string sourceHash = string.Empty;
        int changes = 0;
        bool sourceCaptured = false;
        ClipboardSnapshot? externalSnapshot = null;
        uint externalSnapshotSequence = 0;
        bool clipboardContainsSourceText = false;

        while (copyTimer.ElapsedMilliseconds < AccessTimeoutMilliseconds)
        {
            uint currentSequence = SequenceNumber;
            if (currentSequence != lastSequence)
            {
                changes++;
                string kind = sourceCaptured ? "subsequent-change" : "first-change";
                ClipboardDiagnostics.LogChange(copyTimer, lastSequence, currentSequence, kind);
                lastSequence = currentSequence;
                stableSince = copyTimer.ElapsedMilliseconds;

                int remaining = Math.Max(1, AccessTimeoutMilliseconds - (int)copyTimer.ElapsedMilliseconds);
                if (!TryReadUnicodeTextStable(out bool hasUnicodeText, out string observedText,
                        out uint observedSequence, remaining))
                    continue;
                if (observedSequence != currentSequence)
                {
                    changes++;
                    ClipboardDiagnostics.LogChange(copyTimer, currentSequence, observedSequence,
                        "change-during-read");
                }
                lastSequence = observedSequence;

                if (!sourceCaptured)
                {
                    if (!hasUnicodeText)
                        continue;
                    sourceText = observedText;
                    sourceHash = HashText(observedText);
                    sourceCaptured = true;
                    clipboardContainsSourceText = true;
                    stableSince = copyTimer.ElapsedMilliseconds;
                    continue;
                }

                string observedHash = hasUnicodeText ? HashText(observedText) : string.Empty;
                if (hasUnicodeText && observedText.Length == sourceText.Length &&
                    string.Equals(observedHash, sourceHash, StringComparison.Ordinal))
                {
                    DiagnosticLogStore.Write($"Clipboard copy continuation accepted: sequence={lastSequence}, textLength={observedText.Length}, textHash={observedHash}");
                    clipboardContainsSourceText = true;
                    stableSince = copyTimer.ElapsedMilliseconds;
                    continue;
                }

                remaining = Math.Max(1, AccessTimeoutMilliseconds - (int)copyTimer.ElapsedMilliseconds);
                if (!TryCaptureStable(out ClipboardSnapshot candidate, out uint candidateSequence,
                        out _, remaining))
                {
                    DiagnosticLogStore.Write("Clipboard content changed unexpectedly and a stable replacement snapshot could not be captured");
                    result = new ClipboardCopyResult { ElapsedMilliseconds = copyTimer.ElapsedMilliseconds };
                    return false;
                }
                externalSnapshot = candidate;
                externalSnapshotSequence = candidateSequence;
                clipboardContainsSourceText = false;
                lastSequence = candidateSequence;
                stableSince = copyTimer.ElapsedMilliseconds;
                DiagnosticLogStore.Write($"External Clipboard state captured during copy stabilization: sequence={candidateSequence}, unicodeText={hasUnicodeText}, textLength={(hasUnicodeText ? observedText.Length : 0)}, textHash={(hasUnicodeText ? observedHash : "none")}");
            }
            else if (sourceCaptured &&
                     copyTimer.ElapsedMilliseconds - stableSince >= ClipboardStablePeriodMilliseconds)
            {
                result = new ClipboardCopyResult
                {
                    Text = sourceText,
                    TextHash = sourceHash,
                    LastSequence = lastSequence,
                    ChangeCount = changes,
                    ElapsedMilliseconds = copyTimer.ElapsedMilliseconds,
                    NewExternalSnapshot = externalSnapshot,
                    NewExternalSnapshotSequence = externalSnapshotSequence,
                    ClipboardContainsSourceText = clipboardContainsSourceText
                };
                return true;
            }
            Thread.Sleep(PollMilliseconds);
        }

        result = new ClipboardCopyResult { ElapsedMilliseconds = copyTimer.ElapsedMilliseconds };
        return false;
    }

    public static bool TrySetText(string text, out uint sequenceNumber)
    {
        bool success = NativeClipboard.TrySetText(text, AccessTimeoutMilliseconds);
        sequenceNumber = SequenceNumber;
        return success;
    }

    public static bool IsCurrentSequence(uint expected) => SequenceNumber == expected;

    public static ClipboardRestoreResult RestoreIfUnchanged(ClipboardSnapshot snapshot, uint expectedSequence)
    {
        return RestoreIfUnchanged(snapshot, expectedSequence, out _);
    }

    public static ClipboardRestoreResult RestoreIfUnchanged(ClipboardSnapshot snapshot,
        uint expectedSequence, out uint resultingSequence)
    {
        resultingSequence = SequenceNumber;
        if (!IsCurrentSequence(expectedSequence))
            return ClipboardRestoreResult.SkippedBecauseChanged;

        var wait = Stopwatch.StartNew();
        do
        {
            if (!IsCurrentSequence(expectedSequence))
                return ClipboardRestoreResult.SkippedBecauseChanged;
            try
            {
                if (snapshot.HasData && snapshot.Data != null)
                    Clipboard.SetDataObject(snapshot.Data, copy: true);
                else
                    Clipboard.Clear();
                resultingSequence = SequenceNumber;
                return ClipboardRestoreResult.Restored;
            }
            catch (ExternalException) { Thread.Sleep(10); }
        } while (wait.ElapsedMilliseconds < AccessTimeoutMilliseconds);

        return ClipboardRestoreResult.Failed;
    }

    public static string HashText(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(text)))[..16];

    private static bool TryReadUnicodeTextStable(out bool hasUnicodeText, out string text,
        out uint stableSequence, int timeoutMilliseconds)
    {
        var wait = Stopwatch.StartNew();
        do
        {
            uint before = SequenceNumber;
            try
            {
                hasUnicodeText = Clipboard.ContainsText(TextDataFormat.UnicodeText);
                text = hasUnicodeText ? Clipboard.GetText(TextDataFormat.UnicodeText) : string.Empty;
                uint after = SequenceNumber;
                if (before == after)
                {
                    stableSequence = after;
                    return true;
                }
            }
            catch (ExternalException) { }
            Thread.Sleep(PollMilliseconds);
        } while (wait.ElapsedMilliseconds < timeoutMilliseconds);

        hasUnicodeText = false;
        text = string.Empty;
        stableSequence = SequenceNumber;
        return false;
    }

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
}

