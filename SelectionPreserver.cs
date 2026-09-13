using System;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace LayoutFixer;

public static class SelectionPreserver
{
    public sealed class SelectionSnapshot
    {
        internal TextPatternRange? Range { get; init; }
        public int Length { get; init; }

        public bool HasSelection => Length > 0;
    }

    public static SelectionSnapshot CaptureSelection()
    {
        try
        {
            AutomationElement? element = AutomationElement.FocusedElement;
            if (element == null ||
                !element.TryGetCurrentPattern(TextPattern.Pattern, out object? patternObj))
            {
                return new SelectionSnapshot();
            }

            var textPattern = (TextPattern)patternObj;
            TextPatternRange[] ranges = textPattern.GetSelection();
            if (ranges == null || ranges.Length == 0)
                return new SelectionSnapshot();

            string selected = ranges[0].GetText(-1);
            if (string.IsNullOrEmpty(selected))
                return new SelectionSnapshot();

            return new SelectionSnapshot
            {
                Range = ranges[0].Clone(),
                Length = selected.Length
            };
        }
        catch
        {
            return new SelectionSnapshot();
        }
    }

    public static bool RestoreSelection(SelectionSnapshot snapshot)
    {
        if (!snapshot.HasSelection)
            return false;

        // First try the exact range captured before correction. In controls where
        // UI Automation keeps TextPatternRange positions stable across replacement,
        // this restores the selection immediately with no polling delay.
        if (snapshot.Range != null)
        {
            try
            {
                snapshot.Range.Select();
                return true;
            }
            catch
            {
                // Some controls invalidate ranges after editing. Fall through to
                // the caret-based method below.
            }
        }

        // Fallback for controls that invalidate the old range. Most are ready on
        // the first attempt; only retry briefly when their UIA state lags behind.
        for (int attempt = 0; attempt < 4; attempt++)
        {
            if (TryRestorePreviousSelection(snapshot.Length))
                return true;

            if (attempt < 3)
                Thread.Sleep(6);
        }

        return false;
    }

    private static bool TryRestorePreviousSelection(int length)
    {
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
