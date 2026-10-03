
using System;
using System.Windows.Automation;

namespace LayoutFixer;

// Capability-only probe. It intentionally never calls SetValue, Select, or any mutation API.
internal static class UiaWriteProbe
{
    public static void Run(TextReplacementOperation operation, AutomationElement selected,
        bool valueAvailable, ValuePattern? valuePattern, bool textAvailable, bool text2Available,
        bool legacyAvailable, bool selectionReadable, bool lastWordReadable)
    {
        Log(operation, "UIA WRITE PROBE BEGIN");
        bool valueReadOnly = true;
        if (valueAvailable && valuePattern != null)
        {
            try { valueReadOnly = valuePattern.Current.IsReadOnly; }
            catch { Log(operation, "UiaWriteProbe ValuePatternIsReadOnly=Unavailable"); }
        }
        bool wholeValueWritable = valueAvailable && !valueReadOnly;
        // Standard UIA TextPatternRange exposes read/navigation/selection APIs, but no API
        // for replacing the characters represented by a range.
        bool rangeTextWritable = false;
        bool selectionWritable = false;
        string classification = wholeValueWritable ? "WholeValueReplacementOnly" :
            selectionReadable ? "SelectionOnly" : "ReadOnly";
        Log(operation, $"UiaWriteProbe selectedControlType=\"{selected.Current.ControlType.ProgrammaticName}\"");
        Log(operation, $"UiaWriteProbe ValuePattern={valueAvailable} ValuePatternIsReadOnly={valueReadOnly} ValuePatternCanSetWholeValue={wholeValueWritable}");
        Log(operation, $"UiaWriteProbe TextPattern={textAvailable} TextPattern2={text2Available} LegacyIAccessible={legacyAvailable}");
        Log(operation, $"UiaWriteProbe selectionWritable={selectionWritable} reason=NotAttemptedDiagnosticOnly");
        Log(operation, $"UiaWriteProbe rangeTextWritable={rangeTextWritable} reason=UIATextPatternRangeIsReadOnly");
        Log(operation, $"UiaWriteProbe wholeValueWritable={wholeValueWritable}");
        Log(operation, "UIA WRITE PROBE SUMMARY");
        Log(operation, $"UiaWriteProbe directReplacementAvailable=False classification={classification} lastWordReadable={lastWordReadable} replacementAttempted=False reason=NoWritableAccessibilityRangeInterface");
        Log(operation, "UIA WRITE PROBE END");
    }

    private static void Log(TextReplacementOperation operation, string message) =>
        DiagnosticLogStore.Write($"operation={operation.Id:N} {message}");
}

