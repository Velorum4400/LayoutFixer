using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace LayoutFixer;

public sealed class ChangeLogForm : Form
{
    private readonly RichTextBox _changes;
    private readonly string _language;
    private readonly HashSet<string> _listedVersions = new(StringComparer.OrdinalIgnoreCase);

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
            "v1.0.6",
            new[]
            {
                T(
                    "Added a Clear log button with confirmation and success/error result states.",
                    "Добавлена кнопка «Очистить лог» с подтверждением и сообщением об успешном результате или ошибке.",
                    "נוסף כפתור לניקוי הלוג עם אישור והודעת הצלחה או שגיאה."),
                T(
                    "Errors while clearing diagnostic.log are written to error.log.",
                    "Ошибки при очистке diagnostic.log записываются в error.log.",
                    "שגיאות בעת ניקוי diagnostic.log נרשמות אל error.log.")
            },
            new[]
            {
                T(
                    "Increased the minimum Settings window width so the long correction option is not clipped.",
                    "Увеличена минимальная ширина окна настроек, чтобы длинная настройка исправления текста не обрезалась.",
                    "הוגדל הרוחב המינימלי של חלון ההגדרות כדי שטקסט אפשרות התיקון הארוכה לא ייחתך."),
                T(
                    "Made the Hebrew header identical to the English header and applied right-to-left direction directly to Hebrew text controls.",
                    "Верхний блок Hebrew-интерфейса сделан идентичным английскому, а направление справа налево применяется непосредственно к текстовым элементам.",
                    "הכותרת העליונה בממשק העברי זהה כעת לאנגלית, וכיוון מימין לשמאל מוחל ישירות על רכיבי הטקסט."),
                T(
                    "Added a fallback so the current application version is always represented in the change log.",
                    "Добавлена страховка: текущая версия приложения всегда будет присутствовать в списке изменений.",
                    "נוספה הגנה כך שגרסת היישום הנוכחית תמיד תופיע ביומן השינויים.")
            });

        AddVersion(
            "v1.0.5",
            Array.Empty<string>(),
            new[]
            {
                T(
                    "Increased the minimum Settings window height to prevent the available-languages information from being clipped.",
                    "Увеличена минимальная высота окна настроек, чтобы информация о доступных языках не обрезалась.",
                    "הוגדל הגובה המינימלי של חלון ההגדרות כדי שמידע על השפות הזמינות לא ייחתך."),
                T(
                    "The tagline under LayoutFixer now always stays in English.",
                    "Фраза под LayoutFixer теперь всегда остаётся на английском.",
                    "שורת המשנה מתחת ל-LayoutFixer נשארת תמיד באנגלית."),
                T(
                    "Adjusted Hebrew Settings layout: header position preserved, correction controls mirrored as requested, and Hebrew labels aligned to the right.",
                    "Скорректирован интерфейс на иврите: верхний блок сохраняет положение, элементы исправления текста переставлены, а подписи выровнены вправо.",
                    "ממשק ההגדרות בעברית הותאם: מיקום הכותרת נשמר, רכיבי תיקון הטקסט הוחלפו והכיתובים מיושרים לימין."),
                T(
                    "Changed the Hebrew Installed layouts label to Installed languages.",
                    "Подпись «Установленные раскладки» на иврите заменена на «Установленные языки».",
                    "הכיתוב הוחלף ל-שפות מותקנות.")
            });

        AddVersion(
            "v1.0.4",
            Array.Empty<string>(),
            new[]
            {
                T(
                    "Added the missing changelog entries for versions 1.0.1 through 1.0.3.",
                    "Добавлены отсутствующие записи списка изменений для версий 1.0.1–1.0.3.",
                    "נוספו רשומות חסרות ביומן השינויים עבור גרסאות 1.0.1–1.0.3."),
                T(
                    "Fixed clipping of the LayoutFixer title and slightly increased the minimum Settings window height.",
                    "Исправлено обрезание заголовка LayoutFixer и немного увеличена минимальная высота окна настроек.",
                    "תוקן חיתוך הכותרת LayoutFixer והוגדל מעט הגובה המינימלי של חלון ההגדרות."),
                T(
                    "Fixed card border painting artifacts while resizing the Settings window.",
                    "Исправлены артефакты отрисовки границ карточек при изменении размера окна настроек.",
                    "תוקנו ארטיפקטים בציור גבולות הכרטיסים בעת שינוי גודל חלון ההגדרות.")
            });

        AddVersion(
            "v1.0.3",
            new[]
            {
                T(
                    "When converting to Hebrew, uppercase letter positions now remain uppercase English letters.",
                    "При преобразовании в иврит позиции заглавных букв теперь остаются заглавными английскими буквами.",
                    "בהמרה לעברית, מיקומי אותיות רישיות נשארים כעת אותיות אנגליות רישיות.")
            },
            new[]
            {
                T(
                    "Corrected the Hebrew keyboard mapping and increased space for several Settings labels and informational text.",
                    "Исправлена таблица соответствий ивритской раскладки и увеличено место для нескольких надписей и информационного текста в настройках.",
                    "תוקנה מפת המקלדת העברית והוגדל המקום עבור מספר כותרות וטקסט מידע בהגדרות.")
            });

        AddVersion(
            "v1.0.2",
            Array.Empty<string>(),
            new[]
            {
                T(
                    "Fixed mixed RTL/LTR Hebrew conversion order when UI Automation returns visually reordered text.",
                    "Исправлен порядок текста при преобразовании через иврит, когда UI Automation возвращает смешанный RTL/LTR-текст в визуальном порядке.",
                    "תוקן סדר הטקסט בהמרה דרך עברית כאשר UI Automation מחזיר טקסט RTL/LTR מעורב בסדר חזותי.")
            });

        AddVersion(
            "v1.0.1",
            new[]
            {
                T(
                    "Added global crash logging to %APPDATA%\\LayoutFixer\\crash.log.",
                    "Добавлено глобальное логирование сбоев в %APPDATA%\\LayoutFixer\\crash.log.",
                    "נוסף רישום קריסות גלובלי אל %APPDATA%\\LayoutFixer\\crash.log."),
                T(
                    "Added detailed diagnostics for interface language switching.",
                    "Добавлена подробная диагностика переключения языка интерфейса.",
                    "נוספה אבחנה מפורטת עבור החלפת שפת הממשק.")
            },
            new[]
            {
                T(
                    "Isolated diagnostic logging so logging failures cannot crash LayoutFixer.",
                    "Диагностическое логирование изолировано, чтобы его ошибка не могла завершить LayoutFixer.",
                    "רישום האבחון בודד כך שכשל ברישום לא יוכל להפיל את LayoutFixer.")
            });

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

        EnsureCurrentVersionListed();
        _changes.SelectionStart = 0;
        _changes.SelectionLength = 0;
    }

    private string T(string en, string ru, string he) =>
        _language == "ru" ? ru :
        _language == "he" ? he :
        en;

    private void EnsureCurrentVersionListed()
    {
        string current = "v" + AppInfo.Version;
        if (_listedVersions.Contains(current))
            return;

        AddVersion(
            current,
            Array.Empty<string>(),
            new[]
            {
                T(
                    "This version is not yet described in detail in the built-in change log.",
                    "Для этой версии ещё не добавлено подробное описание изменений.",
                    "לגרסה זו עדיין לא נוסף תיאור מפורט ביומן השינויים.")
            });
    }

    private void AddVersion(
        string version,
        string[] features,
        string[] fixes)
    {
        _listedVersions.Add(version);

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
