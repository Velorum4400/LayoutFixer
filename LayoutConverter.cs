using System;
using System.Text;

namespace LayoutFixer;

public static class LayoutConverter
{
    public static string Convert(string sourceText, KeyboardLayoutMap source, KeyboardLayoutMap target,
        out int unchangedCount)
    {
        unchangedCount = 0;
        if (string.IsNullOrEmpty(sourceText) || source.Layout.Handle == target.Layout.Handle)
            return sourceText;

        var result = new StringBuilder(sourceText.Length);
        for (int index = 0; index < sourceText.Length;)
        {
            char character = sourceText[index];
            if (character is ' ' or '\t' or '\r' or '\n')
            {
                result.Append(character);
                index++;
                continue;
            }

            if (!source.TryGetPreferredCombinationAt(sourceText, index, out KeyCombination combination,
                    out int matchedLength) ||
                !TryConvertCombination(target, combination, sourceText.Substring(index, matchedLength), out string? converted))
            {
                result.Append(character);
                unchangedCount++;
                index++;
                continue;
            }
            result.Append(converted);
            index += matchedLength;
        }
        return result.ToString();
    }

    private static bool TryConvertCombination(KeyboardLayoutMap target, KeyCombination combination,
        string sourceText, out string converted)
    {
        if (!target.TryGetOutput(combination, out converted!)) return false;
        // Some layouts without letter case (notably Hebrew) expose a Latin character for
        // Shift+letter. Preserve Shift for layouts which produce their own character, but
        // retry the same physical key without Shift when the shifted result simply echoes
        // the source character.
        if ((target.Layout.LanguageId & 0x03ff) == 0x0d &&
            (combination.Modifiers & KeyModifiers.Shift) != 0 &&
            sourceText.Length == 1 && sourceText[0] is >= 'A' and <= 'Z' &&
            string.Equals(converted, sourceText, StringComparison.Ordinal))
        {
            var unshifted = new KeyCombination(combination.ScanCode, combination.Modifiers & ~KeyModifiers.Shift);
            if (target.TryGetOutput(unshifted, out string fallback) &&
                !string.Equals(fallback, sourceText, StringComparison.Ordinal))
                converted = fallback;
        }
        return true;
    }
}

