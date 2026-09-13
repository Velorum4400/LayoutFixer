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

        AddVersion("v1.1.0", new[]
        {
            T(
                "Added a Windows installer built with Inno Setup.",
                "Добавлен установщик Windows на базе Inno Setup.",
                "נוסף מתקין Windows המבוסס על Inno Setup."),
            T(
                "build.cmd now publishes LayoutFixer and creates a versioned Setup EXE automatically.",
                "build.cmd теперь публикует LayoutFixer и автоматически создаёт установщик Setup EXE с номером версии.",
                "build.cmd מפרסם כעת את LayoutFixer ויוצר אוטומטית מתקין Setup EXE עם מספר הגרסה."),
            T(
                "The installer adds LayoutFixer to Installed apps and the Start menu, with an optional desktop shortcut and launch after setup.",
                "Установщик добавляет LayoutFixer в список установленных приложений и меню «Пуск», с дополнительным ярлыком на рабочем столе и запуском после установки.",
                "המתקין מוסיף את LayoutFixer לרשימת היישומים המותקנים ולתפריט התחל, עם קיצור דרך אופציונלי בשולחן העבודה והפעלה לאחר ההתקנה.")
        }, Array.Empty<string>());

        AddVersion("v1.0.13", Array.Empty<string>(), new[]
        {
            T(
                "Fixed the regression where correcting a word could only select it without replacing it.",
                "Исправлена ошибка, из-за которой исправление слова могло только выделять его без замены текста.",
                "תוקנה תקלה שבה תיקון מילה היה עלול רק לסמן אותה בלי להחליף את הטקסט."),
            T(
                "Paste completion is now confirmed from the text before the caret, including automatically selected last words.",
                "Подтверждение вставки теперь проверяет текст перед курсором, в том числе для автоматически выделенного последнего слова.",
                "אישור ההדבקה בודק כעת את הטקסט שלפני הסמן, כולל מילה אחרונה שסומנה אוטומטית."),
            T(
                "Clipboard restoration is synchronous again and cannot race the target application's Ctrl+V handling.",
                "Восстановление буфера обмена снова выполняется синхронно и больше не должно опережать обработку Ctrl+V приложением.",
                "שחזור הלוח מתבצע שוב באופן סינכרוני ואינו אמור להקדים את עיבוד Ctrl+V של היישום.")
        });

        AddVersion("v1.0.12", Array.Empty<string>(), new[]
        {
            T(
                "Changed clipboard restoration to avoid inserting the user's previous clipboard contents instead of corrected text.",
                "Изменён порядок восстановления буфера обмена, чтобы вместо исправленного текста не вставлялось старое содержимое буфера.",
                "שונה סדר שחזור הלוח כדי למנוע הדבקת התוכן הישן במקום הטקסט המתוקן."),
            T(
                "Moved selection restoration into the correction process.",
                "Возврат выделения перенесён внутрь процесса исправления.",
                "שחזור הסימון הועבר לתוך תהליך התיקון.")
        });

        AddVersion("v1.0.11", Array.Empty<string>(), new[]
        {
            T(
                "Selection restoration now rebuilds a fresh UI Automation range from the saved position and length.",
                "Возврат выделения теперь создаёт новый диапазон UI Automation по сохранённой позиции и длине.",
                "שחזור הסימון יוצר כעת טווח UI Automation חדש לפי המיקום והאורך שנשמרו."),
            T(
                "Added more vertical space for the keep-selection setting text.",
                "Добавлено дополнительное место по высоте для текста настройки сохранения выделения.",
                "נוסף מקום אנכי לטקסט אפשרות שמירת הסימון.")
        });

        AddVersion("v1.0.10", Array.Empty<string>(), new[]
        {
            T(
                "Expanded the keep-selection setting text with 'after correction' and enabled multiline wrapping.",
                "К настройке сохранения выделения добавлено «после исправления» и включён перенос на следующую строку.",
                "נוסף ניסוח לאחר התיקון לאפשרות שמירת הסימון ונוספה גלישת שורות."),
            T(
                "Reduced the retry delays used when restoring a corrected selection.",
                "Уменьшены задержки повторных попыток при возврате выделения после исправления.",
                "קוצרו זמני ההמתנה בניסיונות שחזור הסימון לאחר תיקון.")
        });

        AddVersion("v1.0.9", Array.Empty<string>(), new[]
        {
            T("Made round-trips through Hebrew preserve ambiguous punctuation exactly.", "Исправлена потеря пунктуации при циклическом преобразовании текста через иврит.", "תוקנה שמירת סימני הפיסוק במעברים מחזוריים דרך עברית."),
            T("Updated the keep-selection option text and enabled wrapping for long selectable labels.", "Уточнён текст настройки сохранения выделения и добавлен перенос длинных подписей.", "עודכן טקסט אפשרות שמירת הסימון ונוספה גלישת שורות לכיתובים ארוכים."),
            T("Optimized layout conversion with precomputed character indexes.", "Оптимизировано преобразование раскладок с помощью заранее построенных индексов символов.", "שופר ביצוע המרת הפריסות באמצעות אינדקסי תווים מוכנים מראש.")
        });

        AddVersion("v1.0.8", new[]
        {
            T("Added an option to keep corrected selected text selected after correction.", "Добавлена настройка, позволяющая оставлять исправленный выделенный текст выделенным после исправления.", "נוספה אפשרות להשאיר את הטקסט המתוקן מסומן לאחר התיקון.")
        }, new[]
        {
            T("Further increased the Settings window width and refined Hebrew text alignment.", "Дополнительно увеличена ширина окна настроек и уточнено выравнивание текста на иврите.", "הוגדל שוב רוחב חלון ההגדרות ושופר יישור הטקסט בעברית.")
        });

        AddVersion("v1.0.7", Array.Empty<string>(), new[]
        {
            T("Increased the minimum Settings width and corrected Hebrew right-to-left alignment.", "Увеличена минимальная ширина окна настроек и исправлено выравнивание Hebrew-интерфейса справа налево.", "הוגדל הרוחב המינימלי של ההגדרות ותוקן יישור הממשק העברי מימין לשמאל.")
        });

        AddVersion("v1.0.6", new[]
        {
            T("Added a Clear log button with confirmation and result states.", "Добавлена кнопка «Очистить лог» с подтверждением и сообщением о результате.", "נוסף כפתור לניקוי הלוג עם אישור והודעת תוצאה."),
            T("Errors while clearing diagnostic.log are written to error.log.", "Ошибки при очистке diagnostic.log записываются в error.log.", "שגיאות בעת ניקוי diagnostic.log נרשמות אל error.log.")
        }, new[]
        {
            T("Increased the minimum Settings window width.", "Увеличена минимальная ширина окна настроек.", "הוגדל הרוחב המינימלי של חלון ההגדרות."),
            T("Made the Hebrew header identical to the English header and refined RTL text controls.", "Верхний блок Hebrew-интерфейса сделан идентичным английскому и уточнено RTL-отображение текста.", "הכותרת העליונה בעברית הותאמה לאנגלית ושופר כיוון הטקסט RTL.")
        });

        AddVersion("v1.0.5", Array.Empty<string>(), new[]
        {
            T("Increased the minimum Settings height to prevent information text from being clipped.", "Увеличена минимальная высота окна настроек, чтобы информационный текст не обрезался.", "הוגדל הגובה המינימלי של חלון ההגדרות כדי שטקסט המידע לא ייחתך."),
            T("The tagline under LayoutFixer now always stays in English.", "Фраза под LayoutFixer теперь всегда остаётся на английском.", "שורת המשנה מתחת ל-LayoutFixer נשארת תמיד באנגלית."),
            T("Adjusted the Hebrew settings layout and installed-languages label.", "Скорректирован интерфейс на иврите и подпись установленных языков.", "הותאם ממשק ההגדרות בעברית וכיתוב השפות המותקנות.")
        });

        AddVersion("v1.0.4", Array.Empty<string>(), new[]
        {
            T("Added missing changelog entries for versions 1.0.1 through 1.0.3.", "Добавлены отсутствующие записи списка изменений для версий 1.0.1–1.0.3.", "נוספו רשומות חסרות עבור גרסאות 1.0.1–1.0.3."),
            T("Fixed clipping of the LayoutFixer title and increased the minimum Settings height.", "Исправлено обрезание заголовка LayoutFixer и увеличена минимальная высота окна настроек.", "תוקן חיתוך הכותרת LayoutFixer והוגדל הגובה המינימלי."),
            T("Fixed card border painting artifacts while resizing.", "Исправлены артефакты отрисовки карточек при изменении размера окна.", "תוקנו ארטיפקטים בציור הכרטיסים בעת שינוי גודל החלון.")
        });

        AddVersion("v1.0.3", new[]
        {
            T("When converting to Hebrew, uppercase letter positions remain uppercase English letters.", "При преобразовании в иврит позиции заглавных букв остаются заглавными английскими буквами.", "בהמרה לעברית, מיקומי אותיות רישיות נשארים אותיות אנגליות רישיות.")
        }, new[]
        {
            T("Corrected the Hebrew keyboard mapping and increased space for Settings labels.", "Исправлена таблица соответствий ивритской раскладки и увеличено место для подписей настроек.", "תוקנה מפת המקלדת העברית והוגדל המקום לכיתובי ההגדרות.")
        });

        AddVersion("v1.0.2", Array.Empty<string>(), new[]
        {
            T("Fixed mixed RTL/LTR Hebrew conversion order returned by UI Automation.", "Исправлен порядок смешанного RTL/LTR-текста при преобразовании через иврит.", "תוקן סדר טקסט RTL/LTR מעורב בהמרה דרך עברית.")
        });

        AddVersion("v1.0.1", new[]
        {
            T("Added global crash logging to %APPDATA%\\LayoutFixer\\crash.log.", "Добавлено глобальное логирование сбоев в %APPDATA%\\LayoutFixer\\crash.log.", "נוסף רישום קריסות אל %APPDATA%\\LayoutFixer\\crash.log."),
            T("Added detailed diagnostics for interface language switching.", "Добавлена подробная диагностика переключения языка интерфейса.", "נוספה אבחנה מפורטת להחלפת שפת הממשק.")
        }, Array.Empty<string>());

        AddVersion("v1.0.0", new[]
        {
            T("Added the new LayoutFixer application icon.", "Добавлен новый логотип LayoutFixer.", "נוסף סמל חדש של LayoutFixer."),
            T("Redesigned the Settings window with a modern header, cards and action buttons.", "Обновлён интерфейс настроек: новый заголовок, карточки и кнопки действий.", "ממשק ההגדרות עוצב מחדש עם כותרת, כרטיסים וכפתורי פעולה.")
        }, new[]
        {
            T("Preserved the existing hotkey, clipboard, localization and layout behavior.", "Сохранена существующая логика хоткеев, буфера обмена, языков и раскладок.", "נשמרה ההתנהגות הקיימת של מקשי הקיצור, הלוח, השפות והפריסות.")
        });

        AddVersion("v0.22", new[]
        {
            T("Added interface language selection: English, Russian and Hebrew.", "Добавлена смена языка интерфейса: английский, русский и иврит.", "נוספה בחירת שפת ממשק: אנגלית, רוסית ועברית."),
            T("Added a Restore defaults button.", "Добавлена кнопка возврата к настройкам по умолчанию.", "נוסף כפתור לשחזור ברירות מחדל.")
        }, Array.Empty<string>());

        AddVersion("v0.21", Array.Empty<string>(), new[]
        {
            T("Removed visible borders and caret from selectable Settings text.", "Убраны видимые рамки и курсор у выделяемого текста настроек.", "הוסרו מסגרות וסמן גלוי מטקסט ההגדרות הניתן לסימון.")
        });

        AddVersion("v0.20", new[]
        {
            T("Redesigned the change log and made setting names selectable and copyable.", "Переработан список изменений, а названия настроек стали выделяемыми и копируемыми.", "עוצב מחדש יומן השינויים ושמות ההגדרות הפכו ניתנים לסימון ולהעתקה.")
        }, Array.Empty<string>());

        AddVersion("v0.19", new[]
        {
            T("Added migration from the old Ctrl+Alt default to Insert.", "Добавлена миграция старого стандартного хоткея Ctrl+Alt на Insert.", "נוספה העברה מברירת המחדל Ctrl+Alt ל-Insert."),
            T("Clipboard contents are preserved before correction and restored afterward.", "Буфер обмена сохраняется перед исправлением и восстанавливается после операции.", "תוכן הלוח נשמר לפני התיקון ומשוחזר לאחריו.")
        }, Array.Empty<string>());

        AddVersion("v0.18", Array.Empty<string>(), new[]
        {
            T("Fixed compiler error CS0136 in TextFixer.cs.", "Исправлена ошибка компиляции CS0136 в TextFixer.cs.", "תוקנה שגיאת הקומפילציה CS0136 ב-TextFixer.cs.")
        });

        AddVersion("v0.17", Array.Empty<string>(), new[]
        {
            T("The terminal remains open when a build fails.", "При ошибке сборки терминал остаётся открытым.", "המסוף נשאר פתוח כאשר הבנייה נכשלת.")
        });

        AddVersion("v0.16", new[]
        {
            T("Added correction of selected text with fallback to the last word.", "Добавлено исправление выделенного текста с переходом к последнему слову при отсутствии выделения.", "נוסף תיקון טקסט מסומן עם מעבר למילה האחרונה כשאין סימון."),
            T("Changed the default hotkey for this action to Insert.", "Хоткей по умолчанию для этой функции изменён на Insert.", "מקש ברירת המחדל לפעולה זו שונה ל-Insert."),
            T("Added the Change log button.", "Добавлена кнопка «Список изменений».", "נוסף כפתור רשימת שינויים.")
        }, Array.Empty<string>());

        AddVersion("v0.15", new[]
        {
            T("Added free hotkey editing with combinations of up to 3 keys.", "Добавлен свободный редактор хоткеев с комбинациями до 3 клавиш.", "נוסף עורך מקשי קיצור עם צירופים של עד 3 מקשים.")
        }, Array.Empty<string>());

        AddVersion("v0.14", new[]
        {
            T("Started the v0.14, v0.15, v0.16... version scheme.", "Начата схема версий v0.14, v0.15, v0.16 и далее.", "החלה שיטת הגרסאות v0.14, v0.15, v0.16 וכן הלאה."),
            T("Settings information text became selectable and copyable.", "Информационный текст в настройках можно выделять и копировать.", "ניתן לסמן ולהעתיק טקסט מידע בהגדרות.")
        }, Array.Empty<string>());

        EnsureCurrentVersionListed();
        _changes.SelectionStart = 0;
        _changes.SelectionLength = 0;
    }

    private string T(string en, string ru, string he) =>
        _language == "ru" ? ru : _language == "he" ? he : en;

    private void EnsureCurrentVersionListed()
    {
        string current = "v" + AppInfo.Version;
        if (_listedVersions.Contains(current))
            return;

        AddVersion(current, Array.Empty<string>(), new[]
        {
            T(
                "This version exists but its detailed change-log entry has not been added yet.",
                "Эта версия существует, но подробная запись списка изменений для неё ещё не добавлена.",
                "גרסה זו קיימת אך טרם נוספה עבורה רשומה מפורטת ביומן השינויים.")
        });
    }

    private void AddVersion(string version, string[] features, string[] fixes)
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
        _changes.SelectionFont = new Font("Segoe UI", size, FontStyle.Bold);
        _changes.SelectionIndent = 18;
        _changes.SelectionRightIndent = 12;
        _changes.AppendText(text);
    }

    private void AppendNormal(string text)
    {
        _changes.SelectionStart = _changes.TextLength;
        _changes.SelectionLength = 0;
        _changes.SelectionFont = new Font("Segoe UI", 10F, FontStyle.Regular);
        _changes.SelectionIndent = 18;
        _changes.SelectionRightIndent = 12;
        _changes.AppendText(text);
    }

    private void AppendSeparator()
    {
        _changes.SelectionStart = _changes.TextLength;
        _changes.SelectionLength = 0;
        _changes.SelectionFont = new Font("Segoe UI", 9F, FontStyle.Regular);
        _changes.SelectionIndent = 18;
        _changes.SelectionRightIndent = 12;
        _changes.AppendText("────────────────────────────────────────────────────────");
    }
}
