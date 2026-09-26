using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace LayoutFixer;

[Flags]
public enum KeyModifiers : byte
{
    None = 0,
    Shift = 1,
    AltGr = 2
}

public readonly record struct KeyCombination(uint ScanCode, KeyModifiers Modifiers);

public sealed class KeyboardLayoutMap
{
    private const uint MAPVK_VSC_TO_VK_EX = 3;
    private const uint TO_UNICODE_NO_STATE_CHANGE = 0x0004;

    // Standard ANSI keys plus the ISO extra key (0x56).  The latter is retained
    // so an ISO keyboard can be represented without making it the preferred
    // inverse mapping when both keys emit the same character.
    private static readonly uint[] ScanCodes =
    {
        0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D,
        0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x1B,
        0x1E, 0x1F, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x29,
        0x2B, 0x2C, 0x2D, 0x2E, 0x2F, 0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x56
    };

    private static readonly KeyModifiers[] ModifierStates =
    {
        KeyModifiers.None,
        KeyModifiers.Shift,
        KeyModifiers.AltGr,
        KeyModifiers.Shift | KeyModifiers.AltGr
    };

    private readonly Dictionary<KeyCombination, string> _direct;
    private readonly Dictionary<string, List<KeyCombination>> _reverse;
    private readonly string[] _reverseTextsByLength;

    public KeyboardLayoutInfo Layout { get; }
    public int EntryCount => _direct.Count;
    public int AmbiguousSymbolCount { get; }

    private KeyboardLayoutMap(
        KeyboardLayoutInfo layout,
        Dictionary<KeyCombination, string> direct,
        Dictionary<string, List<KeyCombination>> reverse)
    {
        Layout = layout;
        _direct = direct;
        _reverse = reverse;
        _reverseTextsByLength = reverse.Keys
            .OrderByDescending(text => text.Length)
            .ThenBy(text => text, StringComparer.Ordinal)
            .ToArray();
        AmbiguousSymbolCount = reverse.Count(pair => pair.Value.Count > 1);
    }

    public static KeyboardLayoutMap Build(KeyboardLayoutInfo layout)
    {
        var stopwatch = Stopwatch.StartNew();
        var direct = new Dictionary<KeyCombination, string>();
        var reverse = new Dictionary<string, List<KeyCombination>>(StringComparer.Ordinal);

        foreach (uint scanCode in ScanCodes)
        foreach (KeyModifiers modifiers in ModifierStates)
        {
            uint virtualKey = MapVirtualKeyEx(scanCode, MAPVK_VSC_TO_VK_EX, layout.Handle);
            if (virtualKey == 0)
                continue;

            var buffer = new StringBuilder(16);
            int count = ToUnicodeEx(virtualKey, scanCode, CreateKeyboardState(modifiers), buffer,
                buffer.Capacity, TO_UNICODE_NO_STATE_CHANGE, layout.Handle);
            if (count <= 0)
                continue; // A zero result has no text; a negative result is a dead key.

            string output = buffer.ToString(0, count);
            var combination = new KeyCombination(scanCode, modifiers);
            direct[combination] = output;
            if (!reverse.TryGetValue(output, out List<KeyCombination>? candidates))
            {
                candidates = new List<KeyCombination>();
                reverse.Add(output, candidates);
            }
            candidates.Add(combination);
        }

        foreach (List<KeyCombination> candidates in reverse.Values)
            candidates.Sort(CompareCombinations);

        var map = new KeyboardLayoutMap(layout, direct, reverse);
        DiagnosticLogStore.Write($"Keyboard layout map built: layout={layout.ShortName}, hkl=0x{layout.Handle.ToInt64():X}, entries={map.EntryCount}, ambiguous={map.AmbiguousSymbolCount}, elapsed={stopwatch.ElapsedMilliseconds} ms");
        return map;
    }

    public bool TryGetPreferredCombination(string text, out KeyCombination combination)
    {
        if (_reverse.TryGetValue(text, out List<KeyCombination>? candidates) && candidates.Count != 0)
        {
            combination = candidates[0];
            return true;
        }
        combination = default;
        return false;
    }

    public bool TryGetPreferredCombinationAt(string sourceText, int startIndex,
        out KeyCombination combination, out int matchedLength)
    {
        foreach (string text in _reverseTextsByLength)
        {
            if (text.Length <= sourceText.Length - startIndex &&
                sourceText.AsSpan(startIndex, text.Length).SequenceEqual(text.AsSpan()) &&
                TryGetPreferredCombination(text, out combination))
            {
                matchedLength = text.Length;
                return true;
            }
        }
        combination = default;
        matchedLength = 0;
        return false;
    }

    public bool TryGetOutput(KeyCombination combination, out string text) =>
        _direct.TryGetValue(combination, out text!);

    private static int CompareCombinations(KeyCombination left, KeyCombination right)
    {
        // The ANSI backslash key is intentionally preferred to ISO 0x56.
        uint leftRank = left.ScanCode == 0x56 ? uint.MaxValue : left.ScanCode;
        uint rightRank = right.ScanCode == 0x56 ? uint.MaxValue : right.ScanCode;
        int scanComparison = leftRank.CompareTo(rightRank);
        return scanComparison != 0 ? scanComparison : left.Modifiers.CompareTo(right.Modifiers);
    }

    private static byte[] CreateKeyboardState(KeyModifiers modifiers)
    {
        var state = new byte[256];
        if ((modifiers & KeyModifiers.Shift) != 0)
            state[0x10] = 0x80;
        if ((modifiers & KeyModifiers.AltGr) != 0)
        {
            state[0x11] = 0x80; // Ctrl + Alt is the Windows AltGr state.
            state[0x12] = 0x80;
        }
        return state;
    }

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyEx(uint code, uint mapType, IntPtr keyboardLayout);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint virtualKey, uint scanCode, byte[] keyboardState,
        StringBuilder buffer, int bufferSize, uint flags, IntPtr keyboardLayout);
}

