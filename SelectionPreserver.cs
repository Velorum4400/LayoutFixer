using System;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace LayoutFixer;

public static class SelectionPreserver
{
    public sealed class SelectionSnapshot
    {
        public int StartOffset { get; init; }
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

            TextPatternRange selectedRange = ranges[0];
            string selected = selectedRange.GetText(-1);
            if (string.IsNullOrEmpty(selected))
                return new SelectionSnapshot();

            // Store the selection as a logical character offset from the start
            // of the current document. UIA ranges can become stale after text is
            // replaced, while offsets let us build a fresh valid range afterward.
            TextPatternRange prefix = textPattern.DocumentRange.Clone();
            prefix.MoveEndpointByRange(
                TextPatternRangeEndpoint.End,
                selectedRange,
                TextPatternRangeEndpoint.Start);

            string beforeSelection = prefix.GetText(-1);

            return new SelectionSnapshot
            {
                StartOffset = beforeSelection?.Length ?? 0,
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

        // Usually succeeds immediately. Retry only very briefly for controls
        // whose UI Automation tree updates a few milliseconds after Ctrl+V.
        for (int attempt = 0; attempt < 4; attempt++)
        {
            if (TryRestoreSelection(snapshot))
                return true;

            if (attempt < 3)
                Thread.Sleep(4);
        }

        return false;
    }

    private static bool TryRestoreSelection(SelectionSnapshot snapshot)
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
            TextPatternRange range = textPattern.DocumentRange.Clone();

            // Collapse a fresh range at the beginning of the document.
            range.MoveEndpointByRange(
                TextPatternRangeEndpoint.End,
                range,
                TextPatternRangeEndpoint.Start);

            if (snapshot.StartOffset > 0)
            {
                int moved = range.Move(
                    TextUnit.Character,
                    snapshot.StartOffset);

                if (moved != snapshot.StartOffset)
                    return false;
            }

            int extended = range.MoveEndpointByUnit(
                TextPatternRangeEndpoint.End,
                TextUnit.Character,
                snapshot.Length);

            if (extended != snapshot.Length)
                return false;

            range.Select();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
