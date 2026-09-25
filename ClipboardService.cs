using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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

internal static class ClipboardService
{
    public const int AccessTimeoutMilliseconds = 500;

    public static uint SequenceNumber => GetClipboardSequenceNumber();

    public static bool TryCapture(out ClipboardSnapshot snapshot)
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
        } while (wait.ElapsedMilliseconds < AccessTimeoutMilliseconds);

        snapshot = new ClipboardSnapshot();
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

    public static bool TrySetText(string text, out uint sequenceNumber)
    {
        bool success = NativeClipboard.TrySetText(text, AccessTimeoutMilliseconds);
        sequenceNumber = SequenceNumber;
        return success;
    }

    public static bool IsCurrentSequence(uint expected) => SequenceNumber == expected;

    public static ClipboardRestoreResult RestoreIfUnchanged(ClipboardSnapshot snapshot, uint expectedSequence)
    {
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
                return ClipboardRestoreResult.Restored;
            }
            catch (ExternalException) { Thread.Sleep(10); }
        } while (wait.ElapsedMilliseconds < AccessTimeoutMilliseconds);

        return ClipboardRestoreResult.Failed;
    }

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
}
