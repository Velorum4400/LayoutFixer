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
            "/1234567890-=/'קראטוןםפ[]שדגכעיחלךף,זסבהנמצתץ." +
            "?!@#$%^&*()_+QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>?"
    };

    // Avoid string.IndexOf for every character. Some Hebrew characters are
    // duplicated in the physical-key map, so this dictionary intentionally
    // keeps the first occurrence for ordinary standalone Hebrew conversion.
    // Exact round-trips through Hebrew are handled separately below.
    private static readonly Dictionary<KeyboardLanguage, Dictionary<char, int>> IndexMaps =
        new()
        {
            [KeyboardLanguage.English] = BuildIndex(Maps[KeyboardLanguage.English]),
            [KeyboardLanguage.Russian] = BuildIndex(Maps[KeyboardLanguage.Russian]),
            [KeyboardLanguage.Hebrew] = BuildIndex(Maps[KeyboardLanguage.Hebrew])
        };

    private static readonly object RecoveryLock = new();
    private static string? _lastLogicalHebrew;
    private static string? _lastHebrewSourceText;
    private static KeyboardLanguage _lastHebrewSourceLanguage;
    private static DateTime _lastHebrewConversionUtc;
    // Keep the original logical text through a short sequence of corrections.
    // This preserves genuine foreign terms inside text that was converted only
    // for its surrounding keyboard-layout characters.
    private static string? _lastCorrectionOutput;
    private static string? _lastCorrectionOriginalText;
    private static KeyboardLanguage _lastCorrectionOriginalLanguage;
    private static DateTime _lastCorrectionUtc;

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

        if (TryRecoverOriginalText(text, out string? originalText, out KeyboardLanguage originalLanguage))
        {
            string recovered = ConvertCore(originalText!, originalLanguage, to);
            RememberCorrection(recovered, originalText!, originalLanguage);
            return recovered;
        }

        // Hebrew contains several characters that can represent more than one
        // physical key position. For example '?' may come from different shifted
        // keys. If this Hebrew text was produced by LayoutFixer moments ago, use
        // the original source text/key positions instead of trying to reverse an
        // inherently ambiguous Hebrew character map. This makes cycles such as
        // RU -> HE -> EN -> RU lossless, including punctuation.
        if (from == KeyboardLanguage.Hebrew &&
            TryRecoverPreHebrewSource(text, out string? priorText, out KeyboardLanguage priorLanguage))
        {
            string exact = ConvertCore(priorText!, priorLanguage, to);
            ClearHebrewRecovery();
            RememberCorrection(exact, priorText!, priorLanguage);
            return exact;
        }

        string sourceText = RecoverLogicalHebrewIfNeeded(text, from);
        string converted = ConvertCore(sourceText, from, to);

        if (to == KeyboardLanguage.Hebrew)
            RememberHebrewConversion(converted, text, from);
        else
            ClearHebrewRecovery();

        RememberCorrection(converted, text, from);

        return converted;
    }

    private static bool TryRecoverOriginalText(
        string text,
        out string? originalText,
        out KeyboardLanguage originalLanguage)
    {
        lock (RecoveryLock)
        {
            originalText = null;
            originalLanguage = KeyboardLanguage.English;
            if (DateTime.UtcNow - _lastCorrectionUtc > TimeSpan.FromSeconds(15))
            {
                _lastCorrectionOutput = null;
                _lastCorrectionOriginalText = null;
                return false;
            }
            if (!string.Equals(text, _lastCorrectionOutput, StringComparison.Ordinal) ||
                string.IsNullOrEmpty(_lastCorrectionOriginalText))
                return false;
            originalText = _lastCorrectionOriginalText;
            originalLanguage = _lastCorrectionOriginalLanguage;
            return true;
        }
    }

    private static void RememberCorrection(
        string output,
        string originalText,
        KeyboardLanguage originalLanguage)
    {
        lock (RecoveryLock)
        {
            _lastCorrectionOutput = output;
            _lastCorrectionOriginalText = originalText;
            _lastCorrectionOriginalLanguage = originalLanguage;
            _lastCorrectionUtc = DateTime.UtcNow;
        }
    }

    private static string ConvertCore(
        string text,
        KeyboardLanguage from,
        KeyboardLanguage to)
    {
        string target = Maps[to];
        string english = Maps[KeyboardLanguage.English];
        Dictionary<char, int> sourceIndex = IndexMaps[from];

        var result = new StringBuilder(text.Length);

        foreach (char c in text)
        {
            if (!sourceIndex.TryGetValue(c, out int index) || index >= target.Length)
            {
                result.Append(c);
                continue;
            }

            // Hebrew has no uppercase alphabet. When the source character came
            // from a shifted alphabetic key, keep that key as an English capital
            // instead of converting it to a Hebrew character/punctuation mark.
            if (to == KeyboardLanguage.Hebrew &&
                index < english.Length &&
                char.IsUpper(english[index]) &&
                char.IsLetter(english[index]))
            {
                result.Append(english[index]);
                continue;
            }

            result.Append(target[index]);
        }

        return result.ToString();
    }

    private static string RecoverLogicalHebrewIfNeeded(
        string text,
        KeyboardLanguage from)
    {
        if (from != KeyboardLanguage.Hebrew)
            return text;

        lock (RecoveryLock)
        {
            if (!IsRecentHebrewRecovery() || string.IsNullOrEmpty(_lastLogicalHebrew))
                return text;

            if (!HaveSameCharacters(text, _lastLogicalHebrew))
                return text;

            return _lastLogicalHebrew;
        }
    }

    private static bool TryRecoverPreHebrewSource(
        string text,
        out string? sourceText,
        out KeyboardLanguage sourceLanguage)
    {
        lock (RecoveryLock)
        {
            sourceText = null;
            sourceLanguage = KeyboardLanguage.English;

            if (!IsRecentHebrewRecovery() ||
                string.IsNullOrEmpty(_lastLogicalHebrew) ||
                string.IsNullOrEmpty(_lastHebrewSourceText) ||
                _lastHebrewSourceLanguage == KeyboardLanguage.Hebrew)
            {
                return false;
            }

            if (!HaveSameCharacters(text, _lastLogicalHebrew))
                return false;

            sourceText = _lastHebrewSourceText;
            sourceLanguage = _lastHebrewSourceLanguage;
            return true;
        }
    }

    private static void RememberHebrewConversion(
        string converted,
        string sourceText,
        KeyboardLanguage sourceLanguage)
    {
        lock (RecoveryLock)
        {
            _lastLogicalHebrew = converted;
            _lastHebrewSourceText = sourceText;
            _lastHebrewSourceLanguage = sourceLanguage;
            _lastHebrewConversionUtc = DateTime.UtcNow;
        }
    }

    private static bool IsRecentHebrewRecovery()
    {
        if (DateTime.UtcNow - _lastHebrewConversionUtc <= TimeSpan.FromSeconds(10))
            return true;

        _lastLogicalHebrew = null;
        _lastHebrewSourceText = null;
        return false;
    }

    private static void ClearHebrewRecovery()
    {
        lock (RecoveryLock)
        {
            _lastLogicalHebrew = null;
            _lastHebrewSourceText = null;
        }
    }

    private static Dictionary<char, int> BuildIndex(string map)
    {
        var result = new Dictionary<char, int>();

        for (int i = 0; i < map.Length; i++)
        {
            if (!result.ContainsKey(map[i]))
                result[map[i]] = i;
        }

        return result;
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
