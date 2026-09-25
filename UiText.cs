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
        ["temporarily_unavailable"] = L(
            "Temporarily unavailable",
            "Временно недоступно",
            "לא זמין זמנית"),
        ["hotkey_hint"] = L(
            "To change a hotkey, click the box with the current hotkey. A combination can contain a maximum of 3 keys.",
            "Чтобы изменить хот кей, нажми на окно с действуещим хоткеем. В комбинации возможно максимум 3 кнопки",
            "כדי לשנות מקש קיצור, לחץ על החלון עם מקש הקיצור הנוכחי. צירוף יכול להכיל לכל היותר 3 מקשים"),
        ["supported_info"] = L(
            "LayoutFixer uses keyboard layouts already installed in Windows.\r\nThe switching cycle follows their saved system order.",
            "LayoutFixer использует раскладки, уже установленные в Windows.\r\nЦикл переключения следует их сохранённому системному порядку.",
            "LayoutFixer משתמש בפריסות המקלדת שכבר מותקנות ב-Windows.\r\nמחזור ההחלפה פועל לפי הסדר השמור במערכת."),
        ["available_layouts"] = L("Available layouts:", "Доступные раскладки:", "פריסות זמינות:"),
        ["version"] = L("Version:", "Версия:", "גרסה:"),
        ["changelog"] = L("Change log", "Список изменений", "רשימת שינויים"),
        ["clear_log"] = L("Clear log", "Очистить лог", "נקה לוג"),
        ["open_log"] = L("Open log", "Открыть лог", "פתח יומן"),
        ["log_tail"] = L("Showing the last 512 KB of the log.", "Показаны последние 512 КБ лога.", "מוצגים 512 הקילובייט האחרונים של היומן."),
        ["log_read_failed"] = L("Could not read log", "Не удалось прочитать лог", "לא ניתן לקרוא את היומן"),
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
        ["preferences_section"] = L("General settings", "Общие настройки", "הגדרות כלליות"),
        ["scanner_section"] = L("Scanner configuration", "Конфигурация сканера", "הגדרת סורק"),
        ["installed_layouts"] = L("Installed layouts", "Установленные раскладки", "שפות מותקנות"),
        ["scanner_enable"] = L("Enable barcode scanner support", "Включить поддержку баркод-сканера", "הפעל תמיכה בסורק ברקוד"),
        ["scanner_intro"] = L(
            "Scan any barcode in the field below. LayoutFixer will detect which physical HID keyboard device sent it and save that device as the scanner.",
            "Отсканируй любой штрихкод в поле ниже. LayoutFixer определит физическое HID-устройство, с которого пришёл ввод, и сохранит его как сканер.",
            "סרוק ברקוד כלשהו בשדה למטה. LayoutFixer יזהה את התקן ה-HID הפיזי שממנו הגיע הקלט וישמור אותו כסורק."),
        ["scanner_scan_here"] = L("Click here, then scan a barcode", "Нажми сюда и отсканируй штрихкод", "לחץ כאן ואז סרוק ברקוד"),
        ["scanner_start_detect"] = L("Detect scanner", "Определить сканер", "זהה סורק"),
        ["scanner_waiting"] = L("Waiting for a scan...", "Ожидание сканирования...", "ממתין לסריקה..."),
        ["scanner_detected"] = L("Scanner detected", "Сканер определён", "הסורק זוהה"),
        ["scanner_not_configured"] = L("No scanner configured", "Сканер не настроен", "לא הוגדר סורק"),
        ["scanner_device"] = L("Selected device", "Выбранное устройство", "התקן נבחר"),
        ["scanner_last_code"] = L("Detected barcode", "Распознанный штрихкод", "ברקוד שזוהה"),
        ["scanner_output_info"] = L(
            "Scanner keystrokes are reconstructed as US English regardless of the active Windows keyboard layout. Enter or Tab sent by the scanner is preserved after the corrected barcode.",
            "Ввод сканера восстанавливается как US English независимо от активной раскладки Windows. Enter или Tab от сканера сохраняется после исправленного штрихкода.",
            "קלט הסורק משוחזר כאנגלית US ללא תלות בפריסת Windows הפעילה. Enter או Tab שנשלחים מהסורק נשמרים לאחר הברקוד המתוקן."),
        ["scanner_hid_note"] = L(
            "Works with scanners that Windows exposes as a USB HID keyboard.",
            "Работает со сканерами, которые Windows определяет как USB HID-клавиатуру.",
            "עובד עם סורקים ש-Windows מזהה כמקלדת USB HID."),
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
