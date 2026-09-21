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

        AddVersion("v1.3.8", Array.Empty<string>(), new[]
        {
            T(
                "Publish converted Unicode text directly to the Windows clipboard to avoid the slow OLE publication path observed with Chromium. Added clipboard publication timing.",
                "Исправленный Unicode-текст записывается напрямую в буфер Windows, обходя медленный путь OLE при работе с Chromium. Добавлен замер времени записи в буфер.",
                "טקסט Unicode מתוקן נכתב ישירות ללוח Windows כדי לעקוף את מסלול OLE האיטי ב-Chromium. נוסף מדד זמן לכתיבה ללוח."),
            T(
                "Replaced Clear log with Open log: two live tabs, separate clear buttons, START/END action boundaries, and scanner_diagnostic.log with migration of the old filename.",
                "Вместо «Очистить лог» добавлено «Открыть лог»: две обновляемые вкладки, отдельные кнопки очистки, границы действий START/END и scanner_diagnostic.log с переносом старого файла.",
                "נוסף פתח יומן במקום נקה יומן: שתי לשוניות מתעדכנות, ניקוי נפרד, גבולות START/END ושם scanner_diagnostic.log עם העברת הקובץ הישן.")
        });

        AddVersion("v1.3.7", Array.Empty<string>(), new[]
        {
            T(
                "Moved text correction to a dedicated STA worker so slow UI Automation and clipboard waits no longer block the tray interface and keyboard hooks. Corrections cannot overlap.",
                "Исправление текста перенесено в отдельный STA-поток: ожидание UI Automation и буфера обмена больше не блокирует интерфейс и клавиатурные перехватчики. Одновременные исправления исключены.",
                "תיקון הטקסט הועבר לתהליכון STA נפרד כדי שהמתנה ל-UI Automation וללוח לא תחסום את הממשק ואת וו המקלדת. תיקונים אינם מתבצעים במקביל."),
            T(
                "Reduced clipboard polling latency, removed full-document reads during last-word paste confirmation, and added per-stage TIMING diagnostics.",
                "Уменьшена задержка проверки буфера, убрано чтение всего документа при подтверждении вставки слова, добавлены замеры TIMING по этапам.",
                "קוצר זמן ההמתנה לבדיקת הלוח, הוסרה קריאת המסמך המלא באימות הדבקת מילה ונוסף אבחון TIMING לכל שלב.")
        });

        AddVersion("v1.3.6", Array.Empty<string>(), new[]
        {
            T(
                "Fixed last-word selection in Edit/RichEdit controls used by Notepad: read text and confirm logical selection offsets with native Windows messages, without relying on UIA or RTL keyboard navigation.",
                "Исправлено выделение последнего слова в полях Edit/RichEdit Блокнота: чтение текста и проверка логических границ выделения выполняются сообщениями Windows, без зависимости от UIA и клавиатурной навигации RTL.",
                "תוקנה בחירת המילה האחרונה בפקדי Edit/RichEdit של פנקס הרשימות: קריאת הטקסט ואימות גבולות הבחירה הלוגיים נעשים באמצעות הודעות Windows, ללא תלות ב-UIA או בניווט מקלדת RTL.")
        });

        AddVersion("v1.3.5", Array.Empty<string>(), new[]
        {
            T(
                "Last-word correction now verifies the actual selection through the clipboard. If selection is unreliable (including Hebrew with digits in ChatGPT/Chromium), it confirms the word-end caret and removes the original word with Backspace before pasting.",
                "Исправление последнего слова теперь проверяет фактическое выделение через буфер обмена. При ненадёжном выделении (включая иврит с цифрами в ChatGPT/Chromium) проверяется курсор в конце слова, затем исходное слово удаляется Backspace перед вставкой.",
                "תיקון המילה האחרונה מאמת כעת את הבחירה בפועל דרך הלוח. כשהבחירה אינה אמינה (כולל עברית עם ספרות ב-ChatGPT/Chromium), מיקום הסמן בסוף המילה מאומת והמילה המקורית נמחקת באמצעות Backspace לפני ההדבקה."),
            T(
                "Added backspace fallback diagnostics and guards against changed focus, unconfirmed caret positions and unsafe character counts. Confirmed selections retain the normal paste path.",
                "Добавлены диагностика fallback и защита при смене фокуса, неподтверждённом положении курсора и небезопасном подсчёте символов. Подтверждённое выделение использует обычную вставку.",
                "נוספו אבחון ל-fallback והגנות מפני שינוי מיקוד, מיקום סמן לא מאומת וספירת תווים לא בטוחה. בחירה מאומתת ממשיכה להשתמש בהדבקה הרגילה.")
        });

        AddVersion("v1.3.4", Array.Empty<string>(), new[]
        {
            T(
                "Improved last-word correction in Chromium/WebView editors such as the ChatGPT Windows app by using Ctrl+Shift+Left when UI Automation cannot create the word selection.",
                "Улучшено исправление последнего слова в Chromium/WebView-редакторах, включая приложение ChatGPT для Windows: если UI Automation не может выделить слово, используется Ctrl+Shift+Left.",
                "שופר תיקון המילה האחרונה בעורכי Chromium/WebView, כולל אפליקציית ChatGPT ל-Windows: כאשר UI Automation אינו יכול לבחור את המילה, נעשה שימוש ב-Ctrl+Shift+Left."),
            T(
                "Portable diagnostic.log now uses the portable data directory instead of %APPDATA%.",
                "В portable-версии diagnostic.log теперь записывается в portable-папку data вместо %APPDATA%.",
                "בגרסה הניידת diagnostic.log נכתב כעת לתיקיית data הניידת במקום %APPDATA%.")
        });

        AddVersion("v1.3.3", Array.Empty<string>(), new[]
        {
            T(
                "Added a RemoteApp-friendly scanner fallback that removes the original scanner text with Backspace before pasting the corrected barcode.",
                "Добавлен совместимый с RemoteApp fallback для сканера: исходный текст сканера удаляется Backspace перед вставкой исправленного штрихкода.",
                "נוסף fallback לסורק התואם ל-RemoteApp: טקסט הסורק המקורי נמחק באמצעות Backspace לפני הדבקת הברקוד המתוקן.")
        });

        AddVersion("v1.3.2", Array.Empty<string>(), new[]
        {
            T(
                "Improved compatibility with Honeywell Voyager 1250g keyboard-wedge scanners that can split one barcode around an intermediate Enter/navigation-key sequence.",
                "Улучшена совместимость со сканерами Honeywell Voyager 1250g в режиме keyboard wedge, которые могут разбивать один штрихкод промежуточной последовательностью Enter/навигационной клавиши.",
                "שופרה התאימות לסורקי Honeywell Voyager 1250g במצב keyboard wedge שעלולים לפצל ברקוד אחד סביב רצף ביניים של Enter ומקש ניווט."),
            T(
                "Enter/Tab is now treated as a short-lived candidate scan terminator; if the selected scanner continues sending characters, LayoutFixer keeps one barcode buffer instead of correcting two fragments separately.",
                "Enter/Tab теперь сначала считается кратковременным кандидатом на окончание скана; если выбранный сканер продолжает ввод, LayoutFixer сохраняет один буфер штрихкода вместо исправления двух частей по отдельности.",
                "Enter/Tab נחשב כעת זמנית כמועמד לסיום הסריקה; אם הסורק הנבחר ממשיך לשלוח תווים, LayoutFixer שומר מאגר ברקוד אחד במקום לתקן שני חלקים בנפרד."),
            T(
                "Suppresses the Honeywell VK_DOWN continuation control during that short scanner window so the caret does not move before the remaining barcode characters arrive.",
                "В коротком окне продолжения скана подавляется Honeywell VK_DOWN, чтобы курсор не сдвигался до прихода оставшихся символов штрихкода.",
                "בחלון ההמשך הקצר של הסריקה מדוכא VK_DOWN של Honeywell כדי שהסמן לא יזוז לפני הגעת שאר תווי הברקוד.")
        });

        AddVersion("v1.3.1", Array.Empty<string>(), new[]
        {
            T(
                "Clear log now clears both diagnostic.log and scaner_diagnostic.log.",
                "Очистка лога теперь очищает и diagnostic.log, и scaner_diagnostic.log.",
                "ניקוי הלוג מנקה כעת גם את diagnostic.log וגם את scaner_diagnostic.log.")
        });

        AddVersion("v1.3.0", Array.Empty<string>(), new[]
        {
            T(
                "Fixed Hebrew RTL alignment in General settings and Scanner configuration so text stays visually aligned to the right without mirroring control geometry.",
                "Исправлено RTL-выравнивание иврита на страницах Общих настроек и Конфигурации сканера: текст снова расположен справа без зеркального смещения самих элементов.",
                "תוקן יישור RTL בעברית בדפי ההגדרות הכלליות והגדרת הסורק כך שהטקסט נשאר מיושר לימין בלי להזיז את מבנה הפקדים."),
            T(
                "Corrected WinForms label alignment behavior for Hebrew by accounting for RightToLeft mirroring.",
                "Исправлено поведение WinForms Label для иврита с учётом зеркального применения TextAlign при RightToLeft.",
                "תוקנה התנהגות היישור של תוויות WinForms בעברית תוך התחשבות בהיפוך של TextAlign במצב RightToLeft."),
            T(
                "Kept the larger caption heights introduced earlier so Russian and Hebrew headings are not clipped vertically.",
                "Сохранены увеличенные высоты подписей, чтобы русские и ивритские заголовки не обрезались снизу.",
                "נשמרו גבהי הכותרות המוגדלים כדי למנוע חיתוך אנכי של טקסט ברוסית ובעברית.")
        });

        AddVersion("v1.2.2", Array.Empty<string>(), new[]
        {
            T(
                "Scanner correction is now scheduled after the low-level keyboard hook returns, preventing the original HID text from being appended after the corrected barcode.",
                "Исправление сканера теперь запускается только после выхода из low-level keyboard hook, чтобы исходный HID-текст не допечатывался после исправленного штрихкода.",
                "תיקון הסורק מתוזמן כעת לאחר היציאה מ-hook המקלדת כדי למנוע מהטקסט המקורי להתווסף אחרי הברקוד המתוקן."),
            T(
                "Changed the scanner drain delay to an asynchronous WinForms timer and added diagnostic messages for the post-hook replacement stage.",
                "Задержка обработки сканера перенесена на асинхронный WinForms timer и добавлена диагностика этапа замены после hook.",
                "השהיית הסורק הועברה לטיימר WinForms אסינכרוני ונוספה אבחנה לשלב ההחלפה לאחר ה-hook."),
            T(
                "Made the LayoutFixer brand label auto-sized and widened the Settings sidebar to prevent clipping at different DPI scales.",
                "Название LayoutFixer теперь автоматически подбирает размер, а боковая панель настроек расширена для устранения обрезания при разных DPI.",
                "תווית LayoutFixer כעת מותאמת אוטומטית והסרגל הצדדי הורחב כדי למנוע חיתוך ב-DPI שונה.")
        });

        AddVersion("v1.2.1", Array.Empty<string>(), new[]
        {
            T(
                "Restored text-correction hotkeys by fully separating the scanner subsystem from the low-level keyboard hook.",
                "Восстановлена работа хоткеев исправления текста: подсистема сканера полностью отделена от low-level keyboard hook.",
                "שוחזרה פעולת מקשי הקיצור לתיקון טקסט על ידי הפרדה מלאה של מערכת הסורק מ-hook המקלדת."),
            T(
                "Expanded the Selected device and USB HID information areas in Scanner Configuration so text is no longer clipped.",
                "Увеличены блоки Selected device и информации про USB HID в конфигурации сканера, чтобы текст больше не обрезался.",
                "הוגדלו אזורי המכשיר הנבחר והמידע על USB HID כדי למנוע חיתוך טקסט."),
            T(
                "Added scaner_diagnostic.log with scanner detection, Raw Input, barcode buffering and SendInput diagnostics.",
                "Добавлен scaner_diagnostic.log с диагностикой определения сканера, Raw Input, буфера штрихкода и SendInput.",
                "נוסף scaner_diagnostic.log עם אבחון זיהוי סורק, Raw Input, מאגר ברקוד ו-SendInput.")
        });

        AddVersion("v1.2.0", new[]
        {
            T(
                "Added universal USB HID barcode-scanner support with Scan to identify device binding.",
                "Добавлена универсальная поддержка USB HID баркод-сканеров с привязкой устройства через сканирование тестового штрихкода.",
                "נוספה תמיכה אוניברסלית בסורקי ברקוד USB HID עם זיהוי המכשיר באמצעות סריקת ברקוד."),
            T(
                "Scanner input is interpreted as US English independently of the currently active Windows keyboard layout.",
                "Ввод выбранного сканера интерпретируется как US English независимо от текущей раскладки Windows.",
                "קלט מהסורק הנבחר מפוענח כאנגלית US ללא תלות בפריסת Windows הפעילה."),
            T(
                "Redesigned Settings with a left navigation menu: Text correction, General settings and Scanner configuration.",
                "Переработано окно настроек: слева добавлено меню Исправление текста, Общие настройки и Конфигурация сканера.",
                "חלון ההגדרות עוצב מחדש עם תפריט צד לתיקון טקסט, הגדרות כלליות והגדרת סורק.")
        }, Array.Empty<string>());

        AddVersion("v1.1.0", new[]
        {
            T("Added a Windows installer built with Inno Setup.", "Добавлен установщик Windows на базе Inno Setup.", "נוסף מתקין Windows המבוסס על Inno Setup."),
            T("build.cmd now creates a versioned Setup EXE automatically.", "build.cmd теперь автоматически создаёт Setup EXE с номером версии.", "build.cmd יוצר כעת אוטומטית Setup EXE עם מספר גרסה."),
            T("Added a separate portable ZIP build with isolated portable settings.", "Добавлена отдельная portable ZIP-сборка с независимыми portable-настройками.", "נוספה בניית ZIP ניידת עם הגדרות נפרדות.")
        }, Array.Empty<string>());

        AddVersion("v1.0.13", Array.Empty<string>(), new[]
        {
            T("Fixed the regression where correcting a word could only select it without replacing it.", "Исправлена ошибка, из-за которой исправление слова могло только выделять его без замены.", "תוקנה תקלה שבה תיקון מילה היה עלול רק לסמן אותה בלי להחליף אותה."),
            T("Clipboard restoration is synchronous again and no longer races Ctrl+V handling.", "Восстановление буфера обмена снова синхронное и не опережает обработку Ctrl+V.", "שחזור הלוח שוב סינכרוני ואינו מקדים את Ctrl+V.")
        });

        AddVersion("v1.0.12", Array.Empty<string>(), new[]
        {
            T("Changed clipboard restoration to avoid pasting previous clipboard contents.", "Изменён порядок восстановления буфера, чтобы не вставлялось его старое содержимое.", "שונה סדר שחזור הלוח כדי למנוע הדבקת תוכן ישן."),
            T("Moved selection restoration into the correction process.", "Возврат выделения перенесён внутрь процесса исправления.", "שחזור הסימון הועבר לתהליך התיקון.")
        });

        AddVersion("v1.0.11", Array.Empty<string>(), new[]
        {
            T("Selection restoration now rebuilds a fresh UI Automation range from saved position and length.", "Возврат выделения теперь создаёт новый UI Automation диапазон по сохранённой позиции и длине.", "שחזור הסימון בונה טווח UI Automation חדש לפי מיקום ואורך שמורים."),
            T("Added more vertical space for the keep-selection option.", "Добавлено больше места для настройки сохранения выделения.", "נוסף מקום אנכי לאפשרות שמירת הסימון.")
        });

        AddVersion("v1.0.10", Array.Empty<string>(), new[]
        {
            T("Improved keep-selection wording and wrapping.", "Улучшены текст и перенос строк настройки сохранения выделения.", "שופר ניסוח וגלישת השורות של שמירת הסימון."),
            T("Reduced selection-restoration retry delays.", "Уменьшены задержки возврата выделения.", "קוצרו עיכובי שחזור הסימון.")
        });

        AddVersion("v1.0.9", new[]
        {
            T("Preserved ambiguous punctuation across Hebrew round-trips.", "Сохранена точная пунктуация при циклических преобразованиях через иврит.", "נשמרו סימני פיסוק דו-משמעיים במעברים דרך עברית."),
            T("Optimized layout conversion with precomputed character indexes.", "Оптимизировано преобразование раскладок заранее построенными индексами символов.", "שופרה המרת פריסות באמצעות אינדקסי תווים מוכנים מראש.")
        }, Array.Empty<string>());

        AddVersion("v1.0.8", new[]
        {
            T("Added an option to keep corrected selected text selected after correction.", "Добавлена настройка сохранения выделения после исправления.", "נוספה אפשרות להשאיר טקסט מתוקן מסומן.")
        }, Array.Empty<string>());

        AddVersion("v1.0.7", Array.Empty<string>(), new[]
        {
            T("Increased Settings width and refined Hebrew RTL alignment.", "Увеличена ширина настроек и улучшено RTL-выравнивание иврита.", "הוגדל רוחב ההגדרות ושופר יישור RTL בעברית.")
        });

        AddVersion("v1.0.6", new[]
        {
            T("Added Clear log with confirmation and error logging.", "Добавлена очистка лога с подтверждением и записью ошибок.", "נוסף ניקוי לוג עם אישור ורישום שגיאות.")
        }, Array.Empty<string>());

        AddVersion("v1.0.5", Array.Empty<string>(), new[]
        {
            T("Adjusted Settings height and Hebrew layout; tagline stays in English.", "Скорректированы высота настроек и Hebrew-интерфейс; tagline всегда остаётся английским.", "הותאמו גובה ההגדרות והממשק בעברית; שורת המשנה נשארת באנגלית.")
        });

        AddVersion("v1.0.4", Array.Empty<string>(), new[]
        {
            T("Fixed title clipping, resize artifacts and missing changelog entries.", "Исправлены обрезание заголовка, артефакты изменения размера и пропуски в changelog.", "תוקנו חיתוך כותרת, ארטיפקטים בשינוי גודל וחוסרים ביומן השינויים.")
        });

        AddVersion("v1.0.3", new[]
        {
            T("Improved Hebrew keyboard mapping and uppercase physical-key behavior.", "Улучшена Hebrew-карта клавиатуры и обработка позиций заглавных клавиш.", "שופרה מפת המקלדת בעברית והתנהגות מקשים גדולים.")
        }, Array.Empty<string>());

        AddVersion("v1.0.2", Array.Empty<string>(), new[]
        {
            T("Improved Hebrew mixed RTL/LTR recovery.", "Улучшено восстановление смешанного RTL/LTR текста на иврите.", "שופר שחזור טקסט עברי מעורב RTL/LTR.")
        });

        AddVersion("v1.0.1", Array.Empty<string>(), new[]
        {
            T("Added crash logging and language-change diagnostics.", "Добавлены crash log и диагностика смены языка.", "נוספו רישום קריסות ואבחון שינוי שפה.")
        });

        AddVersion("v1.0.0", new[]
        {
            T("Introduced the modern branded LayoutFixer Settings interface.", "Добавлен современный фирменный интерфейс настроек LayoutFixer.", "נוסף ממשק הגדרות מודרני וממותג של LayoutFixer.")
        }, Array.Empty<string>());

        AddVersion("v0.22", new[]
        {
            T("Added English, Russian and Hebrew UI localization and Restore defaults.", "Добавлена локализация интерфейса на English, Русский и עברית, а также сброс настроек.", "נוספו שפות ממשק אנגלית, רוסית ועברית ושחזור ברירות מחדל.")
        }, Array.Empty<string>());

        AddVersion("v0.21", Array.Empty<string>(), new[]
        {
            T("Made selectable settings text look like normal labels without visible caret or border.", "Выделяемый текст настроек сделан визуально обычными подписями без рамки и курсора.", "טקסט ניתן לבחירה עוצב כתווית רגילה ללא מסגרת או סמן.")
        });

        AddVersion("v0.20", new[]
        {
            T("Added formatted RichTextBox change log.", "Добавлен форматированный changelog на RichTextBox.", "נוסף יומן שינויים מעוצב ב-RichTextBox.")
        }, Array.Empty<string>());

        AddVersion("v0.19", new[]
        {
            T("Added settings migration, structured changelog and clipboard preservation.", "Добавлены миграция настроек, структурированный changelog и сохранение буфера обмена.", "נוספו העברת הגדרות, יומן שינויים מובנה ושמירת הלוח.")
        }, Array.Empty<string>());

        AddVersion("v0.18", Array.Empty<string>(), new[]
        {
            T("Fixed a C# variable-scope compile error.", "Исправлена ошибка компиляции C# области видимости переменной.", "תוקנה שגיאת קומפילציה בתחום משתנה ב-C#.")
        });

        AddVersion("v0.17", Array.Empty<string>(), new[]
        {
            T("Improved build error handling.", "Улучшена обработка ошибок сборки.", "שופרה טיפול בשגיאות בנייה.")
        });

        AddVersion("v0.16", new[]
        {
            T("Added selected-text-or-last-word correction and made Insert the default hotkey.", "Добавлено исправление выделенного текста или последнего слова; Insert стал хоткеем по умолчанию.", "נוסף תיקון טקסט מסומן או המילה האחרונה ו-Insert הפך לברירת המחדל.")
        }, Array.Empty<string>());

        AddVersion("v0.15", new[]
        {
            T("Added configurable 1–3 key hotkeys.", "Добавлены настраиваемые хоткеи из 1–3 клавиш.", "נוספו מקשי קיצור הניתנים להגדרה מ-1 עד 3 מקשים.")
        }, Array.Empty<string>());

        AddVersion("v0.14", new[]
        {
            T("Started the numbered release scheme and made Settings information selectable/copyable.", "Начата схема нумерованных версий; информационный текст настроек стал выделяемым и копируемым.", "החלה שיטת הגרסאות הממוספרות וטקסט המידע הפך לניתן לבחירה ולהעתקה.")
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
