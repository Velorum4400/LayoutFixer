using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace LayoutFixer;

public enum KeyboardLanguage
{
    English,
    Russian,
    Hebrew
}

public static class KeyboardLayout
{
    private static readonly KeyboardLanguage[] PreferredOrder =
    {
        KeyboardLanguage.English,
        KeyboardLanguage.Russian,
        KeyboardLanguage.Hebrew
    };

    private static readonly Dictionary<KeyboardLanguage, IntPtr> Installed = new();

    public static void Initialize()
    {
        Installed.Clear();

        int count = GetKeyboardLayoutList(0, null);
        if (count <= 0)
            return;

        var layouts = new IntPtr[count];
        int actual = GetKeyboardLayoutList(layouts.Length, layouts);

        for (int i = 0; i < actual; i++)
        {
            IntPtr hkl = layouts[i];

            if (!TryGetLanguage(hkl, out KeyboardLanguage language))
                continue;

            // Keep the first already-installed HKL we find for each supported language.
            // We never call LoadKeyboardLayout, so LayoutFixer cannot install/load
            // a missing layout by itself.
            if (!Installed.ContainsKey(language))
                Installed[language] = hkl;
        }
    }

    public static IReadOnlyList<KeyboardLanguage> AvailableLanguages =>
        PreferredOrder.Where(Installed.ContainsKey).ToArray();

    public static bool IsAvailable(KeyboardLanguage language) =>
        Installed.ContainsKey(language);

    public static bool TryGetInstalledHkl(
        KeyboardLanguage language,
        out IntPtr hkl) =>
        Installed.TryGetValue(language, out hkl);

    public static bool TryGetCorrectionTarget(
        KeyboardLanguage source,
        KeyboardLanguage? current,
        out KeyboardLanguage target) =>
        TryChooseCorrectionTarget(source, current, AvailableLanguages, out target);

    // A word made of Russian or Hebrew characters while that same layout is
    // active normally means it was typed under the wrong layout. English is
    // the intended target in that case; cycling directly to Hebrew/Russian
    // corrupts common cases such as "срфепзе" -> "chatgpt".
    private static bool TryChooseCorrectionTarget(
        KeyboardLanguage source,
        KeyboardLanguage? current,
        IReadOnlyList<KeyboardLanguage> available,
        out KeyboardLanguage target)
    {
        if (available.Count < 2)
        {
            target = source;
            return false;
        }

        if (current.HasValue && current.Value != source && available.Contains(current.Value))
        {
            target = current.Value;
            return true;
        }

        if (source != KeyboardLanguage.English && available.Contains(KeyboardLanguage.English))
        {
            target = KeyboardLanguage.English;
            return true;
        }

        int index = -1;

        for (int i = 0; i < available.Count; i++)
        {
            if (available[i] == source)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            target = available[0];
            return true;
        }

        target = available[(index + 1) % available.Count];
        return true;
    }

    public static string ShortName(KeyboardLanguage language) => language switch
    {
        KeyboardLanguage.English => "EN",
        KeyboardLanguage.Russian => "RU",
        KeyboardLanguage.Hebrew => "HE",
        _ => "?"
    };

    public static string DisplayName(KeyboardLanguage language) => language switch
    {
        KeyboardLanguage.English => "English",
        KeyboardLanguage.Russian => "Русский",
        KeyboardLanguage.Hebrew => "עברית",
        _ => "?"
    };

    public static bool TryGetLanguage(
        IntPtr hkl,
        out KeyboardLanguage language)
    {
        ushort langId = (ushort)((long)hkl & 0xFFFF);
        ushort primary = (ushort)(langId & 0x03FF);

        switch (primary)
        {
            case 0x09:
                language = KeyboardLanguage.English;
                return true;

            case 0x19:
                language = KeyboardLanguage.Russian;
                return true;

            case 0x0D:
                language = KeyboardLanguage.Hebrew;
                return true;

            default:
                language = default;
                return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetKeyboardLayoutList(
        int nBuff,
        [Out] IntPtr[]? lpList);
}
