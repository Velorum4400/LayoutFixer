using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LayoutFixer;

public sealed class SettingsShellForm : Form
{
    private readonly AppSettings _settings;
    private readonly ScannerInputService _scanner;

    private readonly Panel _sidebar = new();
    private readonly Panel _header = new();
    private readonly Panel _host = new();
    private readonly Panel _footer = new();
    private readonly Panel _correctionPage = new();
    private readonly Panel _generalPage = new();
    private readonly Panel _scannerPage = new();

    private readonly Label _pageTitle = new();
    private readonly Label _brand = new();
    private readonly Label _brandSub = new();
    private readonly PictureBox _logo = new();
    private readonly Button _navCorrection = new();
    private readonly Button _navGeneral = new();
    private readonly Button _navScanner = new();

    private readonly CheckBox _full = new();
    private readonly CheckBox _word = new();
    private readonly CheckBox _keepSelection = new();
    private readonly ModernButton _fullHotkey = new();
    private readonly ModernButton _wordHotkey = new();
    private readonly Label _fullLabel = new();
    private readonly Label _wordLabel = new();
    private readonly Label _keepLabel = new();
    private readonly Label _hotkeyHint = new();

    private readonly ComboBox _language = new();
    private readonly Label _languageLabel = new();
    private readonly CheckBox _startup = new();
    private readonly Label _layoutsTitle = new();
    private readonly Label _layoutsValue = new();
    private readonly Label _generalInfo = new();
    private readonly ModernButton _changeLog = new();
    private readonly ModernButton _clearLog = new();

    private readonly CheckBox _scannerEnabled = new();
    private readonly Label _scannerIntro = new();
    private readonly TextBox _scanBox = new();
    private readonly ModernButton _detectScanner = new();
    private readonly Label _deviceTitle = new();
    private readonly Label _deviceValue = new();
    private readonly Label _barcodeTitle = new();
    private readonly Label _barcodeValue = new();
    private readonly Label _scannerInfo = new();
    private readonly Label _scannerNote = new();

    private readonly ModernButton _defaults = new();
    private readonly ModernButton _save = new();

    private string _scannerDevicePath;
    private string _scannerVendorId;
    private string _scannerProductId;
    private string _scannerDisplayName;
    private string _lastBarcode = "";
    private bool _updatingLanguage;
    private int _activePage;

    public SettingsShellForm(AppSettings settings, ScannerInputService scanner)
    {
        _settings = settings;
        _scanner = scanner;
        _scannerDevicePath = settings.ScannerDevicePath;
        _scannerVendorId = settings.ScannerVendorId;
        _scannerProductId = settings.ScannerProductId;
        _scannerDisplayName = settings.ScannerDisplayName;

        UiText.Language = settings.Language;

        Text = $"{AppInfo.DisplayName} — {UiText.Get("settings")}";
        Icon = AppAssets.GetIcon();
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1180, 860);
        MinimumSize = new Size(1050, 800);
        BackColor = Color.FromArgb(244, 247, 252);
        Font = new Font("Segoe UI", 10F);

        BuildShell();
        BuildCorrectionPage();
        BuildGeneralPage();
        BuildScannerPage();

        _scanner.ScannerIdentified += OnScannerIdentified;
        _scanner.IdentificationProgress += OnIdentificationProgress;

        ApplyLanguage();
        ShowPage(0);
        Resize += (_, _) => LayoutPages();
        FormClosed += (_, _) => _scanner.CancelIdentification();
    }

    private void BuildShell()
    {
        _sidebar.Dock = DockStyle.Left;
        _sidebar.Width = 260;
        _sidebar.BackColor = Color.FromArgb(15, 38, 76);

        _logo.SetBounds(28, 26, 58, 58);
        _logo.SizeMode = PictureBoxSizeMode.Zoom;
        _logo.Image = AppAssets.GetLogo();
        _logo.BackColor = Color.Transparent;

        _brand.SetBounds(98, 28, 145, 32);
        _brand.Text = "LayoutFixer";
        _brand.Font = new Font("Segoe UI", 15F, FontStyle.Bold);
        _brand.ForeColor = Color.White;
        _brand.BackColor = Color.Transparent;

        _brandSub.SetBounds(99, 61, 145, 24);
        _brandSub.Text = $"v{AppInfo.Version}";
        _brandSub.ForeColor = Color.FromArgb(175, 199, 230);
        _brandSub.BackColor = Color.Transparent;

        ConfigureNavButton(_navCorrection, 122, (_, _) => ShowPage(0));
        ConfigureNavButton(_navGeneral, 178, (_, _) => ShowPage(1));
        ConfigureNavButton(_navScanner, 234, (_, _) => ShowPage(2));

        _sidebar.Controls.AddRange(new Control[]
        {
            _logo, _brand, _brandSub, _navCorrection, _navGeneral, _navScanner
        });

        _header.Dock = DockStyle.Top;
        _header.Height = 94;
        _header.BackColor = Color.White;

        _pageTitle.SetBounds(34, 27, 760, 46);
        _pageTitle.Font = new Font("Segoe UI", 21F, FontStyle.Bold);
        _pageTitle.ForeColor = Color.FromArgb(24, 42, 72);
        _header.Controls.Add(_pageTitle);

        _footer.Dock = DockStyle.Bottom;
        _footer.Height = 82;
        _footer.BackColor = Color.White;
        _footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(220, 226, 236));
            e.Graphics.DrawLine(pen, 0, 0, _footer.Width, 0);
        };

        _defaults.Width = 190;
        _defaults.Height = 44;
        _defaults.Primary = false;
        _defaults.Click += (_, _) => RestoreDefaults();

        _save.Width = 160;
        _save.Height = 44;
        _save.Primary = true;
        _save.Click += (_, _) => SaveSettings();

        _footer.Controls.AddRange(new Control[] { _defaults, _save });
        _footer.Resize += (_, _) =>
        {
            _save.SetBounds(_footer.ClientSize.Width - 26 - _save.Width, 19, _save.Width, _save.Height);
            _defaults.SetBounds(_save.Left - 14 - _defaults.Width, 19, _defaults.Width, _defaults.Height);
        };

        _host.Dock = DockStyle.Fill;
        _host.BackColor = Color.FromArgb(244, 247, 252);
        _host.Padding = new Padding(28, 24, 28, 24);

        Controls.Add(_host);
        Controls.Add(_footer);
        Controls.Add(_header);
        Controls.Add(_sidebar);
    }

    private void ConfigureNavButton(Button button, int top, EventHandler click)
    {
        button.SetBounds(18, top, 224, 46);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold);
        button.TextAlign = ContentAlignment.MiddleLeft;
        button.Padding = new Padding(16, 0, 0, 0);
        button.ForeColor = Color.FromArgb(210, 222, 240);
        button.BackColor = Color.FromArgb(15, 38, 76);
        button.Cursor = Cursors.Hand;
        button.Click += click;
    }

    private void BuildCorrectionPage()
    {
        CardPanel card = MakeCard();
        card.Dock = DockStyle.Fill;

        _full.SetBounds(34, 48, 24, 30);
        _full.Checked = _settings.FullTextEnabled;
        _fullLabel.SetBounds(72, 45, 560, 36);
        StyleMainLabel(_fullLabel);
        _fullHotkey.SetBounds(650, 40, 180, 42);
        _fullHotkey.Primary = false;
        _fullHotkey.Text = _settings.FullTextHotkey;
        _fullHotkey.Click += (_, _) => EditHotkey(_fullHotkey);

        _word.SetBounds(34, 116, 24, 30);
        _word.Checked = _settings.LastWordEnabled;
        _wordLabel.SetBounds(72, 108, 560, 48);
        StyleMainLabel(_wordLabel);
        _wordHotkey.SetBounds(650, 108, 180, 42);
        _wordHotkey.Primary = false;
        _wordHotkey.Text = _settings.LastWordHotkey;
        _wordHotkey.Click += (_, _) => EditHotkey(_wordHotkey);

        _keepSelection.SetBounds(34, 190, 24, 30);
        _keepSelection.Checked = _settings.KeepSelectionAfterCorrection;
        _keepLabel.SetBounds(72, 181, 650, 58);
        StyleMainLabel(_keepLabel);

        _hotkeyHint.SetBounds(34, 276, 800, 76);
        _hotkeyHint.ForeColor = Color.FromArgb(93, 108, 130);
        _hotkeyHint.Font = new Font("Segoe UI", 10F);

        card.Controls.AddRange(new Control[]
        {
            _full, _fullLabel, _fullHotkey,
            _word, _wordLabel, _wordHotkey,
            _keepSelection, _keepLabel, _hotkeyHint
        });
        _correctionPage.Controls.Add(card);
    }

    private void BuildGeneralPage()
    {
        CardPanel card = MakeCard();
        card.Dock = DockStyle.Fill;

        _languageLabel.SetBounds(34, 34, 320, 28);
        StyleCaption(_languageLabel);
        _language.SetBounds(34, 68, 330, 36);
        _language.DropDownStyle = ComboBoxStyle.DropDownList;
        _language.Items.AddRange(new object[]
        {
            new LanguageItem("en"), new LanguageItem("ru"), new LanguageItem("he")
        });
        SelectLanguage(_settings.Language);
        _language.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingLanguage || _language.SelectedItem is not LanguageItem item)
                return;

            UiText.Language = item.Code;
            ApplyLanguage();
        };

        _startup.SetBounds(34, 132, 520, 34);
        _startup.Checked = _settings.StartWithWindows;

        _layoutsTitle.SetBounds(34, 208, 420, 28);
        StyleCaption(_layoutsTitle);
        _layoutsValue.SetBounds(34, 240, 740, 32);
        _layoutsValue.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold);
        _layoutsValue.ForeColor = Color.FromArgb(20, 112, 235);

        _generalInfo.SetBounds(34, 302, 760, 92);
        _generalInfo.ForeColor = Color.FromArgb(93, 108, 130);

        _changeLog.SetBounds(34, 432, 180, 42);
        _changeLog.Primary = false;
        _changeLog.Click += (_, _) =>
        {
            using var form = new ChangeLogForm(UiText.Language);
            form.ShowDialog(this);
        };

        _clearLog.SetBounds(228, 432, 180, 42);
        _clearLog.Primary = false;
        _clearLog.Click += (_, _) =>
        {
            using var form = new ClearLogForm();
            form.ShowDialog(this);
        };

        card.Controls.AddRange(new Control[]
        {
            _languageLabel, _language, _startup,
            _layoutsTitle, _layoutsValue, _generalInfo,
            _changeLog, _clearLog
        });
        _generalPage.Controls.Add(card);
    }

    private void BuildScannerPage()
    {
        CardPanel card = MakeCard();
        card.Dock = DockStyle.Fill;

        _scannerEnabled.SetBounds(34, 30, 560, 34);
        _scannerEnabled.Checked = _settings.ScannerEnabled;

        _scannerIntro.SetBounds(34, 82, 780, 58);
        _scannerIntro.ForeColor = Color.FromArgb(65, 82, 108);

        _scanBox.SetBounds(34, 158, 610, 38);
        _scanBox.ReadOnly = true;
        _scanBox.Font = new Font("Segoe UI", 11F);
        _scanBox.BackColor = Color.White;
        _scanBox.Click += (_, _) => BeginScannerDetection();
        _scanBox.Enter += (_, _) => BeginScannerDetection();

        _detectScanner.SetBounds(660, 156, 174, 42);
        _detectScanner.Primary = true;
        _detectScanner.Click += (_, _) => BeginScannerDetection();

        _deviceTitle.SetBounds(34, 228, 760, 27);
        StyleCaption(_deviceTitle);
        _deviceValue.SetBounds(34, 260, 800, 82);
        _deviceValue.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold);
        _deviceValue.ForeColor = Color.FromArgb(30, 65, 105);

        _barcodeTitle.SetBounds(34, 362, 760, 27);
        StyleCaption(_barcodeTitle);
        _barcodeValue.SetBounds(34, 394, 800, 36);
        _barcodeValue.Font = new Font("Consolas", 10.5F);
        _barcodeValue.ForeColor = Color.FromArgb(20, 112, 235);

        _scannerInfo.SetBounds(34, 456, 800, 86);
        _scannerInfo.ForeColor = Color.FromArgb(65, 82, 108);
        _scannerNote.SetBounds(34, 556, 800, 58);
        _scannerNote.ForeColor = Color.FromArgb(126, 86, 18);

        card.Controls.AddRange(new Control[]
        {
            _scannerEnabled, _scannerIntro, _scanBox, _detectScanner,
            _deviceTitle, _deviceValue, _barcodeTitle, _barcodeValue,
            _scannerInfo, _scannerNote
        });
        _scannerPage.Controls.Add(card);
    }

    private static CardPanel MakeCard() => new()
    {
        BackColor = Color.White,
        Padding = new Padding(22),
        CornerRadius = 18
    };

    private static void StyleMainLabel(Label label)
    {
        label.Font = new Font("Segoe UI", 11F);
        label.ForeColor = Color.FromArgb(28, 45, 72);
    }

    private static void StyleCaption(Label label)
    {
        label.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        label.ForeColor = Color.FromArgb(62, 78, 103);
    }

    private void ShowPage(int index)
    {
        _activePage = index;
        _host.Controls.Clear();
        Panel page = index switch
        {
            1 => _generalPage,
            2 => _scannerPage,
            _ => _correctionPage
        };
        page.Dock = DockStyle.Fill;
        _host.Controls.Add(page);

        StyleNavSelection(_navCorrection, index == 0);
        StyleNavSelection(_navGeneral, index == 1);
        StyleNavSelection(_navScanner, index == 2);
        UpdatePageTitle();
        LayoutPages();
    }

    private static void StyleNavSelection(Button button, bool selected)
    {
        button.BackColor = selected ? Color.FromArgb(28, 76, 137) : Color.FromArgb(15, 38, 76);
        button.ForeColor = selected ? Color.White : Color.FromArgb(210, 222, 240);
    }

    private void LayoutPages()
    {
        _header.Left = _sidebar.Width;
        _header.Width = ClientSize.Width - _sidebar.Width;
        _footer.Left = _sidebar.Width;
        _footer.Width = ClientSize.Width - _sidebar.Width;
        _host.Left = _sidebar.Width;
        _host.Width = ClientSize.Width - _sidebar.Width;

        int available = Math.Max(680, _host.ClientSize.Width - 56);
        _fullHotkey.Left = Math.Max(580, available - 210);
        _wordHotkey.Left = _fullHotkey.Left;
        _fullLabel.Width = Math.Max(420, _fullHotkey.Left - 94);
        _wordLabel.Width = _fullLabel.Width;
        _keepLabel.Width = Math.Max(560, available - 90);
        _hotkeyHint.Width = Math.Max(620, available - 70);

        _scannerIntro.Width = Math.Max(650, available - 70);
        _scanBox.Width = Math.Max(430, available - 280);
        _detectScanner.Left = _scanBox.Right + 16;
        _deviceValue.Width = Math.Max(650, available - 70);
        _barcodeValue.Width = _deviceValue.Width;
        _scannerInfo.Width = _deviceValue.Width;
        _scannerNote.Width = _deviceValue.Width;
    }

    private void BeginScannerDetection()
    {
        _lastBarcode = "";
        _scanBox.Text = UiText.Get("scanner_waiting");
        _barcodeValue.Text = "—";
        ScannerDiagnosticLog.Write("Scanner detection requested from Settings UI.");
        _scanner.BeginIdentification();
    }

    private void OnIdentificationProgress(string barcode)
    {
        if (!_scanner.IdentificationActive)
            return;

        _scanBox.Text = string.IsNullOrEmpty(barcode) ? UiText.Get("scanner_waiting") : barcode;
    }

    private void OnScannerIdentified(ScannerDeviceInfo device, string barcode)
    {
        _scannerDevicePath = device.DevicePath;
        _scannerVendorId = device.VendorId;
        _scannerProductId = device.ProductId;
        _scannerDisplayName = device.DisplayName;
        _lastBarcode = barcode;
        _scannerEnabled.Checked = true;

        _scanBox.Text = barcode;
        _barcodeValue.Text = barcode;
        UpdateScannerDeviceText();
        ScannerDiagnosticLog.Write(
            $"Settings UI accepted scanner: {device.DisplayName}; barcode='{barcode}'");
    }

    private void UpdateScannerDeviceText()
    {
        if (string.IsNullOrWhiteSpace(_scannerDevicePath) &&
            string.IsNullOrWhiteSpace(_scannerVendorId))
        {
            _deviceValue.Text = UiText.Get("scanner_not_configured");
            return;
        }

        string name = string.IsNullOrWhiteSpace(_scannerDisplayName)
            ? "HID keyboard device"
            : _scannerDisplayName;

        string ids = string.IsNullOrWhiteSpace(_scannerVendorId)
            ? ""
            : $"\r\nVID_{_scannerVendorId} / PID_{_scannerProductId}";

        _deviceValue.Text = name + ids;
    }

    private void EditHotkey(ModernButton target)
    {
        using var editor = new HotkeyEditorForm(UiText.Language);
        if (editor.ShowDialog(this) == DialogResult.OK)
            target.Text = editor.Hotkey;
    }

    private void SaveSettings()
    {
        string full = _fullHotkey.Text;
        string word = _wordHotkey.Text;
        if (!HotkeyDefinition.IsValid(full) || !HotkeyDefinition.IsValid(word))
        {
            MessageBox.Show(this, UiText.Get("invalid_hotkey"), AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (HotkeyDefinition.Parse(full).SetEquals(HotkeyDefinition.Parse(word)))
        {
            MessageBox.Show(this, UiText.Get("duplicate_hotkey"), AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _settings.FullTextEnabled = _full.Checked;
        _settings.LastWordEnabled = _word.Checked;
        _settings.KeepSelectionAfterCorrection = _keepSelection.Checked;
        _settings.FullTextHotkey = full;
        _settings.LastWordHotkey = word;
        _settings.StartWithWindows = _startup.Checked;
        _settings.Language = (_language.SelectedItem as LanguageItem)?.Code ?? "en";

        _settings.ScannerEnabled = _scannerEnabled.Checked;
        _settings.ScannerDevicePath = _scannerDevicePath;
        _settings.ScannerVendorId = _scannerVendorId;
        _settings.ScannerProductId = _scannerProductId;
        _settings.ScannerDisplayName = _scannerDisplayName;
        _settings.ScannerMinimumLength = 3;

        UiText.Language = _settings.Language;
        StartupManager.SetEnabled(_settings.StartWithWindows);
        _settings.Save();
        _scanner.ApplySettings(_settings);

        ScannerDiagnosticLog.Write(
            $"Settings saved. scannerEnabled={_settings.ScannerEnabled}, device='{_settings.ScannerDisplayName}', VID={_settings.ScannerVendorId}, PID={_settings.ScannerProductId}");

        DialogResult = DialogResult.OK;
        Close();
    }

    private void RestoreDefaults()
    {
        DialogResult answer = MessageBox.Show(
            this,
            UiText.Get("defaults_confirm"),
            AppInfo.Name,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (answer != DialogResult.Yes)
            return;

        AppSettings defaults = AppSettings.CreateDefault();
        _full.Checked = defaults.FullTextEnabled;
        _word.Checked = defaults.LastWordEnabled;
        _keepSelection.Checked = defaults.KeepSelectionAfterCorrection;
        _fullHotkey.Text = defaults.FullTextHotkey;
        _wordHotkey.Text = defaults.LastWordHotkey;
        _startup.Checked = defaults.StartWithWindows;
        _scannerEnabled.Checked = defaults.ScannerEnabled;
        _scannerDevicePath = "";
        _scannerVendorId = "";
        _scannerProductId = "";
        _scannerDisplayName = "";
        _lastBarcode = "";
        _scanBox.Text = UiText.Get("scanner_scan_here");
        _barcodeValue.Text = "—";
        UpdateScannerDeviceText();

        UiText.Language = defaults.Language;
        SelectLanguage(defaults.Language);
        ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        Text = $"{AppInfo.DisplayName} — {UiText.Get("settings")}";
        _navCorrection.Text = UiText.Get("correction_section");
        _navGeneral.Text = UiText.Get("preferences_section");
        _navScanner.Text = UiText.Get("scanner_section");

        _fullLabel.Text = UiText.Get("full_text");
        _wordLabel.Text = UiText.Get("selection_word");
        _keepLabel.Text = UiText.Get("keep_selection");
        _hotkeyHint.Text = UiText.Get("hotkey_hint");

        _languageLabel.Text = UiText.Get("language");
        _startup.Text = UiText.Get("startup");
        _layoutsTitle.Text = UiText.Get("installed_layouts");
        _layoutsValue.Text = string.Join(", ", KeyboardLayout.AvailableLanguages.Select(KeyboardLayout.ShortName));
        _generalInfo.Text = UiText.Get("supported_info");
        _changeLog.Text = UiText.Get("changelog");
        _clearLog.Text = UiText.Get("clear_log");

        _scannerEnabled.Text = UiText.Get("scanner_enable");
        _scannerIntro.Text = UiText.Get("scanner_intro");
        if (!_scanner.IdentificationActive && string.IsNullOrWhiteSpace(_lastBarcode))
            _scanBox.Text = UiText.Get("scanner_scan_here");
        _detectScanner.Text = UiText.Get("scanner_start_detect");
        _deviceTitle.Text = UiText.Get("scanner_device");
        _barcodeTitle.Text = UiText.Get("scanner_last_code");
        _scannerInfo.Text = UiText.Get("scanner_output_info");
        _scannerNote.Text = UiText.Get("scanner_hid_note");
        _barcodeValue.Text = string.IsNullOrWhiteSpace(_lastBarcode) ? "—" : _lastBarcode;
        UpdateScannerDeviceText();

        _defaults.Text = UiText.Get("defaults");
        _save.Text = UiText.Get("save");

        bool rtl = UiText.IsRtl;
        foreach (Panel page in new[] { _correctionPage, _generalPage, _scannerPage })
            page.RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;

        _updatingLanguage = true;
        try
        {
            string selected = (_language.SelectedItem as LanguageItem)?.Code ?? UiText.Language;
            _language.BeginUpdate();
            _language.Items.Clear();
            _language.Items.AddRange(new object[]
            {
                new LanguageItem("en"), new LanguageItem("ru"), new LanguageItem("he")
            });
            SelectLanguage(selected);
            _language.EndUpdate();
        }
        finally
        {
            _updatingLanguage = false;
        }

        UpdatePageTitle();
    }

    private void UpdatePageTitle()
    {
        _pageTitle.Text = _activePage switch
        {
            1 => UiText.Get("preferences_section"),
            2 => UiText.Get("scanner_section"),
            _ => UiText.Get("correction_section")
        };
    }

    private void SelectLanguage(string code)
    {
        for (int i = 0; i < _language.Items.Count; i++)
        {
            if (_language.Items[i] is LanguageItem item && item.Code == code)
            {
                _language.SelectedIndex = i;
                return;
            }
        }

        if (_language.Items.Count > 0)
            _language.SelectedIndex = 0;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _scanner.ScannerIdentified -= OnScannerIdentified;
            _scanner.IdentificationProgress -= OnIdentificationProgress;
            _scanner.CancelIdentification();
            _logo.Image?.Dispose();
            Icon?.Dispose();
        }

        base.Dispose(disposing);
    }

    private sealed class LanguageItem
    {
        public string Code { get; }
        public LanguageItem(string code) => Code = code;

        public override string ToString() => Code switch
        {
            "ru" => UiText.Get("russian"),
            "he" => UiText.Get("hebrew"),
            _ => UiText.Get("english")
        };
    }
}
