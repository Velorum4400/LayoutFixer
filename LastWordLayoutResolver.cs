using System;

namespace LayoutFixer;

internal enum FragmentLayoutDetection { Ambiguous, Mixed, English, Russian, Hebrew }

internal static class LastWordLayoutResolver
{
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

    internal static bool Apply(TextReplacementOperation operation, string fragment, out string reason)
    {
        KeyboardLayoutInfo active = operation.SourceLayout;
        FragmentLayoutDetection detection = Detect(fragment);
        if (detection is FragmentLayoutDetection.Ambiguous or FragmentLayoutDetection.Mixed)
        {
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

    private static void Log(TextReplacementOperation operation, string fragment, FragmentLayoutDetection detection,
        KeyboardLayoutInfo active, KeyboardLayoutInfo source, KeyboardLayoutInfo target, string reason) =>
        DiagnosticLogStore.Write($"operation={operation.Id:N} activeLayout={active.ShortName} fragmentLayoutDetection={detection} conversionSourceLayout={source.ShortName} conversionTargetLayout={target.ShortName} layoutSourceReason={reason} fragmentLength={fragment.Length}");
}

