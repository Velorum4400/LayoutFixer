using System;
using System.Globalization;

namespace LayoutFixer;

internal readonly record struct LastWordSelectionAnalysis(
    int FragmentStart,
    int FragmentLength,
    int TrailingWhitespaceLength,
    bool BoundaryWhitespaceFound,
    bool HasFragment);

internal static class LastWordSelectionAnalyzer
{
    public static LastWordSelectionAnalysis Analyze(string selection)
    {
        int contentEnd = selection.Length;
        while (contentEnd > 0 && char.IsWhiteSpace(selection[contentEnd - 1]))
            contentEnd--;

        int fragmentStart = contentEnd;
        while (fragmentStart > 0 && !char.IsWhiteSpace(selection[fragmentStart - 1]))
            fragmentStart--;

        int fragmentLength = contentEnd - fragmentStart;
        bool hasFragment = fragmentLength > 0;
        bool boundaryWhitespace = hasFragment && fragmentStart > 0 &&
            char.IsWhiteSpace(selection[fragmentStart - 1]);

        return new LastWordSelectionAnalysis(fragmentStart, fragmentLength,
            selection.Length - contentEnd, boundaryWhitespace, hasFragment);
    }

    public static int CountTextElements(string value) =>
        value.Length == 0 ? 0 : StringInfo.ParseCombiningCharacters(value).Length;
}

