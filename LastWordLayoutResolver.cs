using System;

namespace LayoutFixer;

internal enum FragmentLayoutDetection { Ambiguous, Mixed, English, Russian, Hebrew }

internal static class LastWordLayoutResolver
{
    private static readonly object Sync = new();
    private static LastWordLayoutContext? _context;
    internal static FragmentLayoutDetection Detect(string fragment)
    {
        bool latin = false, cyrillic = false, hebrew = false;
        foreach (char ch in fragment)
        {
            if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z')) latin = true;
            else if ((ch >= '\u0400' && ch <= '\u052F') || (ch >= '\u2DE0' && ch <= '\u2DFF') || (ch >= '\uA640' && ch <= '\uA69F')) cyrillic = true;
            else if (ch >= '\u0590' && ch <= '\u05FF') hebrew = true;
        }
        int scripts = (latin ? 1 : 0) + (cyrillic ? 1 : 0) + (hebrew ? 1 : 0);
        if (scripts == 0) return FragmentLayoutDetection.Ambiguous;
        if (scripts > 1) return FragmentLayoutDetection.Mixed;
        return latin ? FragmentLayoutDetection.English : cyrillic ? FragmentLayoutDetection.Russian : FragmentLayoutDetection.Hebrew;
    }

    internal static bool Apply(TextReplacementOperation operation, string fragment, int fragmentStart, int caretOffset, out string reason)
    {
        KeyboardLayoutInfo active = operation.SourceLayout;
        FragmentLayoutDetection detection = Detect(fragment);
        if (detection is FragmentLayoutDetection.Ambiguous or FragmentLayoutDetection.Mixed)
        {
            if (detection == FragmentLayoutDetection.Mixed && TryApplyContext(operation, fragment, fragmentStart, caretOffset, active, out reason))
                return true;
            reason = "ActiveKeyboardLayoutFallback";
            Log(operation, fragment, detection, active, active, operation.TargetLayout, reason);
            return false;
        }
        ushort languageId = detection switch { FragmentLayoutDetection.English => 0x09, FragmentLayoutDetection.Russian => 0x19, _ => 0x0d };
        if (!KeyboardLayoutService.TryGetByPrimaryLanguage(languageId, out KeyboardLayoutInfo source) ||
            !KeyboardLayoutService.TryGetNext(source, out KeyboardLayoutInfo target) ||
            !KeyboardLayoutService.TryGetMap(source, out KeyboardLayoutMap sourceMap) ||
            !KeyboardLayoutService.TryGetMap(target, out KeyboardLayoutMap targetMap))
        {
            reason = "ActiveKeyboardLayoutFallback";
            Log(operation, fragment, detection, active, active, operation.TargetLayout, reason);
            return false;
        }
        operation.SourceLayout = source; operation.TargetLayout = target; operation.SourceMap = sourceMap; operation.TargetMap = targetMap;
        reason = "FragmentScriptDetection";
        Log(operation, fragment, detection, active, source, target, reason);
        return true;
    }

    internal static void RecordVerifiedConversion(TextReplacementOperation operation, string sourceFragment,
        string convertedFragment, int fragmentStart, int caretOffset)
    {
        lock (Sync) _context = new LastWordLayoutContext(operation.TargetWindow, sourceFragment, convertedFragment,
            operation.SourceLayout, operation.TargetLayout, fragmentStart, caretOffset);
    }

    private static bool TryApplyContext(TextReplacementOperation operation, string fragment, int start, int caret,
        KeyboardLayoutInfo active, out string reason)
    {
        LastWordLayoutContext? context;
        lock (Sync) context = _context;
        if (context == null || context.TargetWindow != operation.TargetWindow || context.ConvertedFragment != fragment ||
            context.FragmentStart != start || context.CaretOffset != caret)
        {
            reason = "PreviousVerifiedConversionUnavailable";
            return false;
        }
        if (!KeyboardLayoutService.TryGetNext(context.TargetLayout, out KeyboardLayoutInfo target) ||
            !KeyboardLayoutService.TryGetMap(context.TargetLayout, out KeyboardLayoutMap sourceMap) ||
            !KeyboardLayoutService.TryGetMap(target, out KeyboardLayoutMap targetMap))
        { reason = "PreviousVerifiedConversionUnavailable"; return false; }
        operation.SourceLayout = context.TargetLayout; operation.TargetLayout = target;
        operation.SourceMap = sourceMap; operation.TargetMap = targetMap;
        reason = "PreviousVerifiedConversion";
        Log(operation, fragment, FragmentLayoutDetection.Mixed, active, context.TargetLayout, target, reason);
        return true;
    }

    private static void Log(TextReplacementOperation operation, string fragment, FragmentLayoutDetection detection,
        KeyboardLayoutInfo active, KeyboardLayoutInfo source, KeyboardLayoutInfo target, string reason) =>
        DiagnosticLogStore.Write($"operation={operation.Id:N} activeLayout={active.ShortName} fragmentLayoutDetection={detection} conversionSourceLayout={source.ShortName} conversionTargetLayout={target.ShortName} layoutSourceReason={reason} fragmentLength={fragment.Length}");

    private sealed record LastWordLayoutContext(IntPtr TargetWindow, string SourceFragment, string ConvertedFragment,
        KeyboardLayoutInfo SourceLayout, KeyboardLayoutInfo TargetLayout, int FragmentStart, int CaretOffset);
}

