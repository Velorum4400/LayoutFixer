using System;
using System.Drawing;
using System.Windows.Forms;

namespace LayoutFixer;

public sealed class ChangeLogForm : Form
{
    private readonly RichTextBox _changes;
    private readonly string _language;

    public ChangeLogForm(string language)
    {
        _language = language;
        UiText.Language = language;

        Text = $"{AppInfo.DisplayName} — {UiText.Get("changelog")}";
        Icon = AppAssets.GetIcon();
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(820, 650);
        MinimumSize = new Size(650, 480);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        RightToLeft = UiText.IsRtl ? RightToLeft.Yes : RightToLeft.No;
        RightToLeftLayout = UiText.IsRtl;

        _changes = new RichTextBox
        {
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            WordWrap = true,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 10F),
            BackColor = SystemColors.Window,
            DetectUrls = false,
            RightToLeft = UiText.IsRtl ? RightToLeft.Yes : RightToLeft.No
        };

        Controls.Add(_changes);

        AddVersion(
            "v1.0.0",
            new[]
            {
                T(
                    "Added the new LayoutFixer application icon to the EXE, shortcuts, windows and tray.",
                    "Добавлен новый логотип LayoutFixer для EXE, ярлыков, окон и системного трея.",
                    "נוסף סמל חדש של LayoutFixer לקובץ EXE, לקיצורי דרך, לחלונות ולמגש המערכת."),
                T(
                    "Redesigned the Settings window with a modern header, cards and updated action buttons.",
                    "Полностью обновлён интерфейс настроек: новый заголовок, карточки и современные кнопки действий.",
                    "ממשק ההגדרות עוצב מחדש עם כותרת מודרנית, כרטיסים וכפתורי פעולה מעודכנים.")
            },
            new[]
            {
                T(
                    "Preserved the existing hotkey, clipboard, localization and keyboard-layout behavior in the new interface.",
                    "В новом интерфейсе сохранена существующая логика хоткеев, буфера обмена, языков и раскладок.",
                    "בממשק החדש נשמרה ההתנהגות הקיימת של מקשי הקיצור, הלוח, השפות ופריסות המקלדת.")
            });

        AddVersion(
            "v0.22",
            new[]
            {
                T(
                    "Added interface language selection: English, Russian and Hebrew.",
                    "Добавлена смена языка интерфейса: английский, русский и иврит.",
                    "נוספה אפשרות לבחור שפת ממשק: אנגלית, רוסית ועברית."),
                T(
                    "Added a Restore defaults button next to Save.",
                    "Добавлена кнопка возврата к настройкам по умолчанию рядом с кнопкой сохранения.",
                    "נוסף כפתור לשחזור הגדרות ברירת המחדל ליד כפתור השמירה.")
            },
            new[]
            {
                T(
                    "Updated the hotkey editing hint and increased the space reserved for it.",
                    "Обновлена подсказка изменения хоткея и увеличено место под текст.",
                    "עודכנה ההנחיה לשינוי מקש קיצור והוגדל המקום המוקצה לטקסט.")
            });

        AddVersion(
            "v0.21",
            Array.Empty<string>(),
            new[]
            {
                T(
                    "Removed borders around text elements in Settings.",
                    "Убраны рамки вокруг текстовых элементов в настройках.",
                    "הוסרו המסגרות סביב רכיבי הטקסט בהגדרות."),
                T(
                    "The text caret is hidden when selecting or copying settings text.",
                    "Текстовый курсор больше не отображается при выделении и копировании текста настроек.",
                    "סמן הטקסט מוסתר בעת סימון או העתקת טקסט מההגדרות."),
                T(
                    "Added a small inset to the change log.",
                    "В списке изменений добавлен небольшой отступ текста.",
                    "נוספה הזחה קטנה ברשימת השינויים.")
            });

        AddVersion(
            "v0.20",
            new[]
            {
                T(
                    "Redesigned the change log.",
                    "Переработано оформление списка изменений.",
                    "עוצב מחדש מראה רשימת השינויים."),
                T(
                    "Setting names can be selected and copied.",
                    "Текст названий настроек теперь можно выделять и копировать.",
                    "ניתן לסמן ולהעתיק את שמות ההגדרות.")
            },
            new[]
            {
                T(
                    "Made the Settings window wider so the long option and hotkey remain on one row.",
                    "Окно настроек стало шире, чтобы название функции и поле хоткея помещались в одну строку.",
                    "חלון ההגדרות הורחב כדי שהאפשרות הארוכה ומקש הקיצור יישארו באותה שורה.")
            });

        AddVersion(
            "v0.19",
            new[]
            {
                T(
                    "Added migration from the old Ctrl+Alt default to Insert.",
                    "Добавлена миграция старого стандартного хоткея Ctrl+Alt на Insert.",
                    "נוספה העברה מברירת המחדל הישנה Ctrl+Alt ל-Insert."),
                T(
                    "Clipboard contents are preserved before correction and restored afterward.",
                    "Буфер обмена сохраняется перед исправлением текста и восстанавливается после операции.",
                    "תוכן הלוח נשמר לפני התיקון ומשוחזר לאחריו.")
            },
            new[]
            {
                T(
                    "Corrected text no longer remains in the clipboard.",
                    "Исправленный текст больше не остаётся в буфере обмена.",
                    "הטקסט המתוקן כבר לא נשאר בלוח.")
            });

        AddVersion(
            "v0.18",
            Array.Empty<string>(),
            new[]
            {
                T(
                    "Fixed compiler error CS0136 in TextFixer.cs.",
                    "Исправлена ошибка компиляции CS0136 в TextFixer.cs.",
                    "תוקנה שגיאת הקומפילציה CS0136 ב-TextFixer.cs.")
            });

        AddVersion(
            "v0.17",
            Array.Empty<string>(),
            new[]
            {
                T(
                    "The terminal now stays open when a build fails.",
                    "При ошибке сборки терминал остаётся открытым.",
                    "המסוף נשאר פתוח כאשר הבנייה נכשלת."),
                T(
                    "The terminal still closes automatically after a successful build.",
                    "При успешной сборке терминал закрывается автоматически.",
                    "המסוף עדיין נסגר אוטומטית לאחר בנייה מוצלחת.")
            });

        AddVersion(
            "v0.16",
            new[]
            {
                T(
                    "Added correction of selected text, falling back to the last word when no selection exists.",
                    "Добавлено исправление выделенного текста; если выделения нет, исправляется последнее слово.",
                    "נוסף תיקון של טקסט מסומן; אם אין סימון, מתוקנת המילה האחרונה."),
                T(
                    "Changed the default hotkey for this action to Insert.",
                    "Хоткей по умолчанию для этой функции изменён на Insert.",
                    "מקש ברירת המחדל לפעולה זו שונה ל-Insert."),
                T(
                    "Moved hotkey capture into a separate small window.",
                    "Редактор хоткея перенесён в отдельное небольшое окно.",
                    "לכידת מקש הקיצור הועברה לחלון קטן ונפרד."),
                T(
                    "Added the Change log button.",
                    "Добавлена кнопка «Список изменений».",
                    "נוסף כפתור רשימת השינויים.")
            },
            Array.Empty<string>());

        AddVersion(
            "v0.15",
            new[]
            {
                T(
                    "Added free hotkey editing with combinations of up to 3 keys.",
                    "Добавлен свободный редактор хоткеев с комбинациями до 3 клавиш.",
                    "נוסף עורך מקשי קיצור חופשי עם צירופים של עד 3 מקשים.")
            },
            new[]
            {
                T(
                    "Prevented assigning the same hotkey to both actions.",
                    "Запрещено назначать одинаковый хоткей двум действиям.",
                    "נמנעה הקצאת אותו מקש קיצור לשתי הפעולות.")
            });

        AddVersion(
            "v0.14",
            new[]
            {
                T(
                    "Started the v0.14, v0.15, v0.16... version scheme.",
                    "Начата схема версий v0.14, v0.15, v0.16 и далее.",
                    "החלה שיטת הגרסאות v0.14, v0.15, v0.16 וכן הלאה."),
                T(
                    "Settings information text became selectable and copyable.",
                    "Информационный текст в настройках можно выделять и копировать.",
                    "ניתן לסמן ולהעתיק טקסט מידע בהגדרות.")
            },
            new[]
            {
                T(
                    "Removed the tray notification option.",
                    "Удалена опция «Показывать уведомления в трее».",
                    "הוסרה אפשרות ההתראות במגש המערכת.")
            });

        _changes.SelectionStart = 0;
        _changes.SelectionLength = 0;
    }

    private string T(string en, string ru, string he) =>
        _language == "ru" ? ru :
        _language == "he" ? he :
        en;

    private void AddVersion(
        string version,
        string[] features,
        string[] fixes)
    {
        if (_changes.TextLength > 0)
        {
            AppendNormal("\r\n");
            AppendSeparator();
            AppendNormal("\r\n\r\n");
        }

        AppendBold(version + ":", 15F);
        AppendNormal("\r\n\r\n");

        AppendBold(UiText.Get("new_features"), 10F);
        AppendNormal("\r\n");

        if (features.Length == 0)
            AppendNormal(UiText.Get("none") + "\r\n");
        else
            foreach (string item in features)
                AppendNormal("• " + item + "\r\n");

        AppendNormal("\r\n");
        AppendBold(UiText.Get("fixes"), 10F);
        AppendNormal("\r\n");

        if (fixes.Length == 0)
            AppendNormal(UiText.Get("none") + "\r\n");
        else
            foreach (string item in fixes)
                AppendNormal("• " + item + "\r\n");
    }

    private void AppendBold(string text, float size)
    {
        _changes.SelectionStart = _changes.TextLength;
        _changes.SelectionLength = 0;
        _changes.SelectionFont =
            new Font("Segoe UI", size, FontStyle.Bold);
        _changes.SelectionIndent = 18;
        _changes.SelectionRightIndent = 12;
        _changes.AppendText(text);
    }

    private void AppendNormal(string text)
    {
        _changes.SelectionStart = _changes.TextLength;
        _changes.SelectionLength = 0;
        _changes.SelectionFont =
            new Font("Segoe UI", 10F, FontStyle.Regular);
        _changes.SelectionIndent = 18;
        _changes.SelectionRightIndent = 12;
        _changes.AppendText(text);
    }

    private void AppendSeparator()
    {
        _changes.SelectionStart = _changes.TextLength;
        _changes.SelectionLength = 0;
        _changes.SelectionFont =
            new Font("Segoe UI", 9F, FontStyle.Regular);
        _changes.SelectionIndent = 18;
        _changes.SelectionRightIndent = 12;
        _changes.AppendText(
            "────────────────────────────────────────────────────────");
    }
}
