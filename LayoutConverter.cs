using System.Collections.Generic;
using System.Text;

namespace LayoutFixer;

public static class LayoutConverter
{
    // Standard Windows US / Russian / Hebrew physical-key mappings.
    // Spaces, newlines and characters not present in a mapping are preserved.
    private static readonly Dictionary<KeyboardLanguage, string> Maps = new()
    {
        [KeyboardLanguage.English] =
            "`1234567890-=qwertyuiop[]asdfghjkl;'zxcvbnm,./" +
            "~!@#$%^&*()_+QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>?",

        [KeyboardLanguage.Russian] =
            "ё1234567890-=йцукенгшщзхъфывапролджэячсмитьбю." +
            "Ё!\"№;%:?*()_+ЙЦУКЕНГШЩЗХЪФЫВАПРОЛДЖЭЯЧСМИТЬБЮ,",

        [KeyboardLanguage.Hebrew] =
            "/1234567890-=/'קראטוןםפ[]שדגכעיחלךף',זסבהנמצתץ." +
            "?!@#$%^&*()_+QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>?"
    };

    private static readonly object RecoveryLock = new();
    private static string? _lastLogicalHebrew;
    private static DateTime _lastHebrewConversionUtc;

    public static KeyboardLanguage DetectLanguage(string text, KeyboardLanguage fallback)
    {
        int english = 0;
        int russian = 0;
        int hebrew = 0;

        foreach (char c in text)
        {
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                english++;
            else if ((c >= '\u0400' && c <= '\u04FF') || c == 'Ё' || c == 'ё')
                russian++;
            else if (c >= '\u0590' && c <= '\u05FF')
                hebrew++;
        }

        int max = Math.Max(english, Math.Max(russian, hebrew));

        if (max == 0)
            return fallback;

        if (english == max)
            return KeyboardLanguage.English;
        if (russian == max)
            return KeyboardLanguage.Russian;

        return KeyboardLanguage.Hebrew;
    }

    public static string Convert(
        string text,
        KeyboardLanguage from,
        KeyboardLanguage to)
    {
        if (string.IsNullOrEmpty(text) || from == to)
            return text;

        string sourceText = RecoverLogicalHebrewIfNeeded(text, from);
        string source = Maps[from];
        string target = Maps[to];

        var result = new StringBuilder(sourceText.Length);

        foreach (char c in sourceText)
        {
            int index = source.IndexOf(c);
            result.Append(index >= 0 && index < target.Length ? target[index] : c);
        }

        string converted = result.ToString();
        RememberHebrewLogicalOrder(converted, to);
        return converted;
    }

    private static string RecoverLogicalHebrewIfNeeded(
        string text,
        KeyboardLanguage from)
    {
        if (from != KeyboardLanguage.Hebrew)
            return text;

        lock (RecoveryLock)
        {
            if (string.IsNullOrEmpty(_lastLogicalHebrew))
                return text;

            // UI Automation in some RTL/LTR editors (notably modern Notepad)
            // may return the visually arranged Hebrew text instead of its logical
            // character order. Only reuse the previous logical form for a short
            // time and only when the exact same characters are still present.
            if (DateTime.UtcNow - _lastHebrewConversionUtc > TimeSpan.FromSeconds(10))
            {
                _lastLogicalHebrew = null;
                return text;
            }

            if (!HaveSameCharacters(text, _lastLogicalHebrew))
                return text;

            return _lastLogicalHebrew;
        }
    }

    private static void RememberHebrewLogicalOrder(
        string converted,
        KeyboardLanguage to)
    {
        lock (RecoveryLock)
        {
            if (to == KeyboardLanguage.Hebrew)
            {
                _lastLogicalHebrew = converted;
                _lastHebrewConversionUtc = DateTime.UtcNow;
            }
            else
            {
                _lastLogicalHebrew = null;
            }
        }
    }

    private static bool HaveSameCharacters(string a, string b)
    {
        if (a.Length != b.Length)
            return false;

        var counts = new Dictionary<char, int>();

        foreach (char c in a)
        {
            counts.TryGetValue(c, out int count);
            counts[c] = count + 1;
        }

        foreach (char c in b)
        {
            if (!counts.TryGetValue(c, out int count) || count == 0)
                return false;

            if (count == 1)
                counts.Remove(c);
            else
                counts[c] = count - 1;
        }

        return counts.Count == 0;
    }
}
