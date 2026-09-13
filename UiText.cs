using System.Collections.Generic;

namespace LayoutFixer;

public static class UiText
{
    private static string _language = "en";

    public static string Language
    {
        get => _language;
        set => _language = value is "ru" or "he" ? value : "en";
    }

    public static bool IsRtl => Language == "he";

    public static string Get(string key)
    {
        if (Strings.TryGetValue(key, out var values))
        {
            if (values.TryGetValue(Language, out string? text))
                return text;

            if (values.TryGetValue("en", out text))
                return text;
        }

        return key;
    }

    private static readonly Dictionary<string, Dictionary<string, string>> Strings = new()
    {
        ["settings"] = L("Settings", "Настройки", "הגדרות"),
        ["language"] = L("Language:", "Язык:", "שפה:"),
        ["english"] = L("English", "Английский", "אנגלית"),
        ["russian"] = L("Russian", "Русский", "רוסית"),
        ["hebrew"] = L("Hebrew", "Иврит", "עברית"),
        ["startup"] = L("Start with Windows", "Запускать вместе с Windows", "הפעל עם Windows"),
        ["full_text"] = L("Correct all text", "Исправлять весь текст", "תקן את כל הטקסט"),
        ["selection_word"] = L(
            "Correct selected text or the last word",
            "Исправлять выделенный текст или последнее слово",
            "תקן טקסט מסומן או את המילה האחרונה"),
        ["keep_selection"] = L(
            "Keep corrected selected text selected after correction",
            "Оставлять исправленный выделенный текст выделенным после исправления",
            "השאר את הטקסט המסומן מסומן לאחר התיקון"),
        ["hotkey_hint"] = L(
            "To change a hotkey, click the box with the current hotkey. A combination can contain a maximum of 3 keys.",
            "Чтобы изменить хот кей, нажми на окно с действуещим хоткеем. В комбинации возможно максимум 3 кнопки",
            "כדי לשנות מקש קיצור, לחץ על החלון עם מקש הקיצור הנוכחי. צירוף יכול להכיל לכל היותר 3 מקשים"),
        ["supported_info"] = L(
            "Supported: English, Russian, Hebrew.\r\nLayoutFixer uses only keyboard layouts already installed in Windows.\r\nThe switching cycle is built only from available languages.",
            "Поддерживаются: English, Русский, עברית.\r\nLayoutFixer использует только раскладки, уже установленные в Windows.\r\nЦикл переключения строится только из доступных языков.",
            "נתמכות: אנגלית, רוסית, עברית.\r\nLayoutFixer משתמש רק בפריסות מקלדת שכבר מותקנות ב-Windows.\r\nמחזור ההחלפה נבנה רק מהשפות הזמינות."),
        ["available_layouts"] = L("Available layouts:", "Доступные раскладки:", "פריסות זמינות:"),
        ["version"] = L("Version:", "Версия:", "גרסה:"),
        ["changelog"] = L("Change log", "Список изменений", "רשימת שינויים"),
        ["clear_log"] = L("Clear log", "Очистить лог", "נקה לוג"),
        ["clear_log_confirm"] = L(
            "Do you really want to clear the log?",
            "Действительно вы хотите очистить лог?",
            "האם באמת ברצונך לנקות את הלוג?"),
        ["clear_log_success"] = L(
            "Log cleared successfully",
            "Лог очищен успешно",
            "הלוג נוקה בהצלחה"),
        ["clear_log_failed"] = L(
            "Failed to clear log",
            "Не удалось очистить лог",
            "ניקוי הלוג נכשל"),
        ["close"] = L("Close", "Закрыть", "סגור"),
        ["save"] = L("Save", "Сохранить", "שמור"),
        ["defaults"] = L("Restore defaults", "По умолчанию", "שחזר ברירות מחדל"),
        ["defaults_confirm"] = L(
            "Restore all settings to their default values?",
            "Вернуть все настройки к значениям по умолчанию?",
            "לשחזר את כל ההגדרות לערכי ברירת המחדל?"),
        ["yes"] = L("Yes", "Да", "כן"),
        ["no"] = L("No", "Нет", "לא"),
        ["invalid_hotkey"] = L(
            "Each hotkey must contain from 1 to 3 keys.",
            "Каждый хоткей должен содержать от 1 до 3 клавиш.",
            "כל מקש קיצור חייב להכיל בין מקש אחד ל-3 מקשים."),
        ["duplicate_hotkey"] = L(
            "The two actions cannot use the same hotkey.",
            "Хоткеи для двух действий не могут быть одинаковыми.",
            "לא ניתן להשתמש באותו מקש קיצור לשתי הפעולות."),
        ["new_hotkey"] = L("New hotkey", "Новый хоткей", "מקש קיצור חדש"),
        ["press_hotkey"] = L(
            "Press a new key or combination (up to 3 keys):",
            "Нажми новую клавишу или комбинацию (до 3 клавиш):",
            "לחץ על מקש או צירוף חדש (עד 3 מקשים):"),
        ["cancel"] = L("Cancel", "Отмена", "ביטול"),
        ["press_first"] = L(
            "Press from 1 to 3 keys first.",
            "Сначала нажми от 1 до 3 клавиш.",
            "תחילה לחץ על 1 עד 3 מקשים."),
        ["tray_full"] = L("Correct all text", "Исправить весь текст", "תקן את כל הטקסט"),
        ["tray_word"] = L(
            "Correct selection / last word",
            "Исправить выделение / последнее слово",
            "תקן בחירה / מילה אחרונה"),
        ["tray_settings"] = L("Settings...", "Настройки...", "הגדרות..."),
        ["tray_exit"] = L("Exit", "Выход", "יציאה"),
        ["tagline"] = L("Type in the right language", "Печатай на нужном языке", "הקלד בשפה הנכונה"),
        ["correction_section"] = L("Text correction", "Исправление текста", "תיקון טקסט"),
        ["preferences_section"] = L("Preferences", "Общие настройки", "העדפות"),
        ["installed_layouts"] = L("Installed layouts", "Установленные раскладки", "שפות מותקנות"),
        ["new_features"] = L("New features:", "Новые функции:", "תכונות חדשות:"),
        ["fixes"] = L("Fixes:", "Исправления:", "תיקונים:"),
        ["none"] = L("None.", "Нет.", "אין.")
    };

    private static Dictionary<string, string> L(string en, string ru, string he) =>
        new()
        {
            ["en"] = en,
            ["ru"] = ru,
            ["he"] = he
        };
}
