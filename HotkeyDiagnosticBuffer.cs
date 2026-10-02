using System;
using System.Collections.Generic;

namespace LayoutFixer;

internal readonly record struct HotkeyCallbackDiagnostic(
    DateTime TimestampUtc,
    string HookName,
    uint VirtualKey,
    int Message,
    uint Flags,
    bool Injected,
    int CallbackManagedThreadId,
    uint CallbackOsThreadId,
    IntPtr ForegroundWindow,
    uint ForegroundThreadId,
    uint ForegroundProcessId);

internal static class HotkeyDiagnosticBuffer
{
    private const int Capacity = 100;
    private static readonly object Sync = new();
    private static readonly HotkeyCallbackDiagnostic[] Events = new HotkeyCallbackDiagnostic[Capacity];
    private static int _next;
    private static int _count;

    public static void Record(HotkeyCallbackDiagnostic callback)
    {
        lock (Sync)
        {
            Events[_next] = callback;
            _next = (_next + 1) % Capacity;
            if (_count < Capacity)
                _count++;
        }
    }

    public static IReadOnlyList<HotkeyCallbackDiagnostic> Snapshot()
    {
        lock (Sync)
        {
            var snapshot = new HotkeyCallbackDiagnostic[_count];
            int first = (_next - _count + Capacity) % Capacity;
            for (int index = 0; index < _count; index++)
                snapshot[index] = Events[(first + index) % Capacity];
            return snapshot;
        }
    }
}

