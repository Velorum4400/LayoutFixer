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
                !target.TryGetOutput(combination, out string? converted))
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
}

