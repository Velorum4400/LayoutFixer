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

        string source = Maps[from];
        string target = Maps[to];

        var result = new StringBuilder(text.Length);

        foreach (char c in text)
        {
            int index = source.IndexOf(c);
            result.Append(index >= 0 && index < target.Length ? target[index] : c);
        }

        return result.ToString();
    }
}
