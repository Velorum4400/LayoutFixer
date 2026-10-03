using System;
using System.Collections.Generic;

namespace LayoutFixer;

// Kept in memory so ordinary keyboard activity does not create disk-log noise.
internal readonly record struct HotkeyRecognitionDiagnostic(
    DateTime TimestampUtc,
    string Source,
    string EventName,
    string Key,
    bool Injected,
    bool? First,
    string PressedBefore,
    string PressedAfter,
    string Configured,
    string PendingBefore,
    string PendingAfter,
    string SuppressedBefore,
    string SuppressedAfter,
    bool? SetEquals,
    bool? PendingOverlapsPressed,
    string Decision);

internal static class HotkeyDiagnosticBuffer
{
    private const int Capacity = 180;
    private static readonly object Sync = new();
    private static readonly HotkeyRecognitionDiagnostic[] Events = new HotkeyRecognitionDiagnostic[Capacity];
    private static int _next;
    private static int _count;

    public static void Record(HotkeyRecognitionDiagnostic diagnostic)
    {
        lock (Sync)
        {
            Events[_next] = diagnostic;
            _next = (_next + 1) % Capacity;
            if (_count < Capacity)
                _count++;
        }
    }

    public static void RecordDispatch(string stage) => Record(new HotkeyRecognitionDiagnostic(
        DateTime.UtcNow, "Dispatch", "stage", "-", false, null, "-", "-", "-", "-", "-",
        "-", "-", null, null, stage));

    public static string FormatTail(int maximumEvents)
    {
        HotkeyRecognitionDiagnostic[] snapshot;
        lock (Sync)
        {
            int count = Math.Min(_count, maximumEvents);
            snapshot = new HotkeyRecognitionDiagnostic[count];
            int first = (_next - count + Capacity) % Capacity;
            for (int index = 0; index < count; index++)
                snapshot[index] = Events[(first + index) % Capacity];
        }

        var lines = new List<string> { $"HOTKEY RECOGNITION BUFFER: count={snapshot.Length}" };
        foreach (HotkeyRecognitionDiagnostic item in snapshot)
        {
            lines.Add($"timestampUtc={item.TimestampUtc:O}, source={item.Source}, event={item.EventName}, key={item.Key}, injected={item.Injected}, first={Format(item.First)}, pressedBefore={item.PressedBefore}, pressedAfter={item.PressedAfter}, configured={item.Configured}, pendingBefore={item.PendingBefore}, pendingAfter={item.PendingAfter}, suppressedBefore={item.SuppressedBefore}, suppressedAfter={item.SuppressedAfter}, setEquals={Format(item.SetEquals)}, pendingOverlapsPressed={Format(item.PendingOverlapsPressed)}, decision={item.Decision}");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string Format(bool? value) => value is null ? "n/a" : value.Value.ToString();
}

