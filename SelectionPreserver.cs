using System;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace LayoutFixer;

public static class SelectionPreserver
{
    public static int CaptureSelectedLength()
    {
        try
        {
            AutomationElement? element = AutomationElement.FocusedElement;
            if (element == null ||
                !element.TryGetCurrentPattern(TextPattern.Pattern, out object? patternObj))
            {
                return 0;
            }

            var textPattern = (TextPattern)patternObj;
            TextPatternRange[] ranges = textPattern.GetSelection();
            if (ranges == null || ranges.Length == 0)
                return 0;

            string selected = ranges[0].GetText(-1);
            return string.IsNullOrEmpty(selected) ? 0 : selected.Length;
        }
        catch
        {
            return 0;
        }
    }

    public static bool RestorePreviousSelection(int length)
    {
        if (length <= 0)
            return false;

        try
        {
            AutomationElement? element = AutomationElement.FocusedElement;
            if (element == null ||
                !element.TryGetCurrentPattern(TextPattern.Pattern, out object? patternObj))
            {
                return false;
            }

            var textPattern = (TextPattern)patternObj;
            TextPatternRange[] ranges = textPattern.GetSelection();
            if (ranges == null || ranges.Length == 0)
                return false;

            // Some editors keep the replacement selected after paste already.
            string currentSelection = ranges[0].GetText(-1);
            if (!string.IsNullOrEmpty(currentSelection))
                return true;

            TextPatternRange caret = ranges[0].Clone();
            caret.MoveEndpointByRange(
                TextPatternRangeEndpoint.End,
                caret,
                TextPatternRangeEndpoint.Start);

            int moved = caret.MoveEndpointByUnit(
                TextPatternRangeEndpoint.Start,
                TextUnit.Character,
                -length);

            if (moved == 0)
                return false;

            caret.Select();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
