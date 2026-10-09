using System;

namespace LayoutFixer;

// A no-op conversion can still represent a meaningful layout-cycle step, for example an
// uppercase Latin fragment sent from English to Hebrew. Do not select or rewrite text here.
internal static class LastWordLayoutOnlyCompletion
{
    internal static bool IsUnchanged(string source, string converted) =>
        string.Equals(source, converted, StringComparison.Ordinal);

    internal static bool TryComplete(TextReplacementOperation operation, string handlerName, out string result)
    {
        result = "LayoutOnlySwitchFailed";
        Log(operation, "ConversionUnchanged=True");
        Log(operation, "TextReplacementSkipped=True");
        Log(operation, $"LayoutSwitchRequested={operation.TargetLayout.ShortName}");
        LayoutSwitchVerificationResult switchResult = KeyboardLayoutService.SwitchLayoutAndVerify(
            operation.TargetWindow, operation.TargetLayout, out _, out _);
        bool succeeded = switchResult == LayoutSwitchVerificationResult.Success;
        Log(operation, $"LayoutSwitchSucceeded={succeeded}");
        if (!succeeded)
        {
            result = "LayoutOnlySwitch" + switchResult;
            Log(operation, $"OperationResult={result}");
            return false;
        }
        result = "LayoutOnlySuccess";
        Log(operation, "OperationResult=LayoutOnlySuccess");
        Log(operation, $"NATIVE LASTWORD: handler={handlerName} strategy=LayoutOnly result=Success");
        return true;
    }

    private static void Log(TextReplacementOperation operation, string message) =>
        DiagnosticLogStore.Write($"operation={operation.Id:N} {message}");
}

