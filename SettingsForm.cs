using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LayoutFixer;

public sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;

    private GradientHeaderPanel _header = null!;
    private Panel _content = null!;
    private Panel _footer = null!;
    private CardPanel _correctionCard = null!;
    private CardPanel _preferencesCard = null!;

    private PictureBox _logo = null!;
    private Label _title = null!;
    private Label _tagline = null!;
    private Label _versionBadge = null!;
    private Label _correctionTitle = null!;
    private Label _preferencesTitle = null!;
    private Label _installedTitle = null!;

    private ComboBox _language = null!;
    private CheckBox _startup = null!;
    private CheckBox _full = null!;
    private CheckBox _word = null!;
    private CheckBox _keepSelection = null!;
    private ModernButton _fullHotkey = null!;
    private ModernButton _wordHotkey = null!;

    private SelectableLabel _languageLabel = null!;
    private SelectableLabel _fullLabel = null!;
    private SelectableLabel _wordLabel = null!;
    private SelectableLabel _keepSelectionLabel = null!;
    private SelectableLabel _hotkeyHelp = null!;
    private SelectableLabel _info = null!;
    private SelectableLabel _availableLayouts = null!;

    private ModernButton _changelog = null!;
    private ModernButton _clearLog = null!;
    private ModernButton _save = null!;
    private ModernButton _defaults = null!;
    private bool _updatingLanguage;

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;
        UiText.Language = _settings.Language;

        Text = AppInfo.DisplayName;
        Icon = AppAssets.GetIcon();
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1500, 790);
        MinimumSize = new Size(1500, 780);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(243, 247, 252);
        Font = new Font("Segoe UI", 10F);

        Build();
        ApplyLanguage();
        ResizeLayout();
        Resize += (_, _) => ResizeLayout();
    }

    private void Build()
    {
        _header = new GradientHeaderPanel { Dock = DockStyle.Top, Height = 156, RightToLeft = RightToLeft.No };
        _logo = new PictureBox { Left = 34, Top = 24, Width = 96, Height = 96, SizeMode = PictureBoxSizeMode.Zoom, Image = AppAssets.GetLogo(), BackColor = Color.Transparent, RightToLeft = RightToLeft.No };
        _title = new Label { Left = 151, Top = 18, Width = 650, Height = 72, Text = "LayoutFixer", Font = new Font("Segoe UI", 26F, FontStyle.Bold), ForeColor = Color.White, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft, RightToLeft = RightToLeft.No };
        _tagline = new Label { Left = 154, Top = 88, Width = 650, Height = 40, Text = "Type in the right language", Font = new Font("Segoe UI", 13F), ForeColor = Color.FromArgb(215, 230, 250), BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft, RightToLeft = RightToLeft.No };
        _versionBadge = new Label { Width = 112, Height = 42, Top = 49, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 12F, FontStyle.Bold), ForeColor = Color.White, BackColor = Color.FromArgb(20, 112, 235), RightToLeft = RightToLeft.No };
        _header.Controls.AddRange(new Control[] { _logo, _title, _tagline, _versionBadge });

        _content = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(243, 247, 252), Padding = new Padding(24, 24, 24, 16) };
        _correctionCard = new CardPanel();
        _preferencesCard = new CardPanel();
        BuildCorrectionCard();
        BuildPreferencesCard();
        _content.Controls.Add(_correctionCard);
        _content.Controls.Add(_preferencesCard);

        _footer = new Panel { Dock = DockStyle.Bottom, Height = 88, BackColor = Color.White, Padding = new Padding(24, 18, 24, 18) };
        _footer.Paint += (_, e) => { using var pen = new Pen(Color.FromArgb(222, 228, 237)); e.Graphics.DrawLine(pen, 0, 0, _footer.Width, 0); };
        _defaults = new ModernButton { Width = 200, Height = 46, Primary = false };
        _defaults.Click += (_, _) => RestoreDefaults();
        _save = new ModernButton { Width = 190, Height = 46, Primary = true };
        _save.Click += (_, _) => SaveSettings();
        _footer.Controls.Add(_defaults);
        _footer.Controls.Add(_save);
        Controls.Add(_content);
        Controls.Add(_footer);
        Controls.Add(_header);
    }

    private void BuildCorrectionCard()
    {
        _correctionTitle = new Label { Left = 26, Top = 16, Width = 360, Height = 46, Font = new Font("Segoe UI", 16F, FontStyle.Bold), ForeColor = Color.FromArgb(22, 40, 70), BackColor = Color.White, TextAlign = ContentAlignment.MiddleLeft };
        _full = new CheckBox { Left = 28, Top = 86, Width = 22, Height = 28, Checked = _settings.FullTextEnabled, BackColor = Color.White };
        _fullLabel = MakeSelectableLabel(58, 82, 350, 32);
        _fullHotkey = MakeHotkeyButton(_settings.FullTextHotkey);
        _word = new CheckBox { Left = 28, Top = 151, Width = 22, Height = 28, Checked = _settings.LastWordEnabled, BackColor = Color.White };
        _wordLabel = MakeSelectableLabel(58, 147, 350, 52);
        _wordHotkey = MakeHotkeyButton(_settings.LastWordHotkey);
        _keepSelection = new CheckBox { Left = 28, Top = 214, Width = 22, Height = 28, Checked = _settings.KeepSelectionAfterCorrection, BackColor = Color.White };
        _keepSelectionLabel = MakeSelectableLabel(58, 210, 460, 36);
        _hotkeyHelp = MakeSelectableLabel(28, 266, 500, 70);
        _hotkeyHelp.Multiline = true;
        _hotkeyHelp.Font = new Font("Segoe UI", 9.5F);
        _hotkeyHelp.ForeColor = Color.FromArgb(88, 101, 122);
        _correctionCard.Controls.AddRange(new Control[] { _correctionTitle, _full, _fullLabel, _fullHotkey, _word, _wordLabel, _wordHotkey, _keepSelection, _keepSelectionLabel, _hotkeyHelp });
    }

    private void BuildPreferencesCard()
    {
        _preferencesTitle = new Label { Left = 26, Top = 16, Width = 310, Height = 46, Font = new Font("Segoe UI", 16F, FontStyle.Bold), ForeColor = Color.FromArgb(22, 40, 70), BackColor = Color.White, TextAlign = ContentAlignment.MiddleLeft };
        _languageLabel = MakeSelectableLabel(28, 78, 120, 30);
        _language = new ComboBox { Left = 28, Top = 109, Width = 290, Height = 34, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 10F) };
        _language.Items.AddRange(new object[] { new LanguageItem("en"), new LanguageItem("ru"), new LanguageItem("he") });
        SelectLanguage(_settings.Language);
        _language.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingLanguage) return;
            if (_language.SelectedItem is LanguageItem item)
            {
                string oldLanguage = UiText.Language;
                CrashLogger.Write($"Language change requested: {oldLanguage} -> {item.Code}");
                try { UiText.Language = item.Code; ApplyLanguage(); CrashLogger.Write($"Language change completed: {oldLanguage} -> {item.Code}"); }
                catch (Exception ex) { CrashLogger.WriteException($"SettingsForm language change {oldLanguage} -> {item.Code}", ex); throw; }
            }
        };
        _startup = new CheckBox { Left = 28, Top = 168, Width = 310, Height = 32, Checked = _settings.StartWithWindows, BackColor = Color.White };
        _installedTitle = new Label { Left = 28, Top = 224, Width = 310, Height = 28, Font = new Font("Segoe UI", 10F, FontStyle.Bold), ForeColor = Color.FromArgb(40, 58, 86), BackColor = Color.White, TextAlign = ContentAlignment.MiddleLeft };
        _availableLayouts = MakeSelectableLabel(28, 256, 310, 32);
        _availableLayouts.ForeColor = Color.FromArgb(20, 112, 235);
        _availableLayouts.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        _info = MakeSelectableLabel(28, 300, 310, 118);
        _info.Multiline = true;
        _info.Font = new Font("Segoe UI", 9F);
        _info.ForeColor = Color.FromArgb(88, 101, 122);
        _changelog = new ModernButton { Left = 28, Width = 170, Height = 42, Primary = false };
        _changelog.Click += (_, _) => { using var form = new ChangeLogForm(UiText.Language); form.ShowDialog(this); };
        _clearLog = new ModernButton { Left = 210, Width = 170, Height = 42, Primary = false };
        _clearLog.Click += (_, _) => { using var form = new ClearLogForm(); form.ShowDialog(this); };
        _preferencesCard.Controls.AddRange(new Control[] { _preferencesTitle, _languageLabel, _language, _startup, _installedTitle, _availableLayouts, _info, _changelog, _clearLog });
    }

    private void ResizeLayout()
    {
        _versionBadge.Left = Math.Max(820, _header.ClientSize.Width - _versionBadge.Width - 34);
        int areaWidth = _content.ClientSize.Width - _content.Padding.Left - _content.Padding.Right;
        int areaHeight = _content.ClientSize.Height - _content.Padding.Top - _content.Padding.Bottom;
        int gap = 20;
        int rightWidth = Math.Max(350, (int)(areaWidth * 0.37));
        int leftWidth = areaWidth - rightWidth - gap;

        if (leftWidth < 520)
        {
            leftWidth = areaWidth; rightWidth = areaWidth;
            int stackedHeight = Math.Max(350, (areaHeight - gap) / 2);
            _correctionCard.SetBounds(_content.Padding.Left, _content.Padding.Top, areaWidth, stackedHeight);
            _preferencesCard.SetBounds(_content.Padding.Left, _content.Padding.Top + stackedHeight + gap, areaWidth, stackedHeight);
        }
        else
        {
            int leftX = _content.Padding.Left;
            int rightX = leftX + leftWidth + gap;
            if (UiText.IsRtl) { int tmp = leftX; leftX = _content.Padding.Left + rightWidth + gap; rightX = tmp; }
            _correctionCard.SetBounds(leftX, _content.Padding.Top, leftWidth, areaHeight);
            _preferencesCard.SetBounds(rightX, _content.Padding.Top, rightWidth, areaHeight);
        }

        int hotkeyWidth = 180;
        int textWidth = Math.Max(300, _correctionCard.ClientSize.Width - hotkeyWidth - 115);
        if (UiText.IsRtl)
        {
            int checkboxX = _correctionCard.ClientSize.Width - 28 - _full.Width;
            int labelRight = checkboxX - 8;
            int rtlTextWidth = Math.Max(300, labelRight - hotkeyWidth - 76);
            _full.SetBounds(checkboxX, 86, 22, 28);
            _word.SetBounds(checkboxX, 151, 22, 28);
            _keepSelection.SetBounds(checkboxX, 214, 22, 28);
            _fullHotkey.SetBounds(28, 76, hotkeyWidth, 42);
            _wordHotkey.SetBounds(28, 141, hotkeyWidth, 42);
            _fullLabel.SetBounds(labelRight - rtlTextWidth, 82, rtlTextWidth, 32);
            _wordLabel.SetBounds(labelRight - rtlTextWidth, 147, rtlTextWidth, 52);
            _keepSelectionLabel.SetBounds(labelRight - rtlTextWidth, 210, rtlTextWidth, 36);
            _correctionTitle.SetBounds(26, 16, Math.Max(200, _correctionCard.ClientSize.Width - 52), 46);
            _hotkeyHelp.Left = 28;
            _hotkeyHelp.Width = Math.Max(300, _correctionCard.ClientSize.Width - 56);
        }
        else
        {
            _full.SetBounds(28, 86, 22, 28);
            _word.SetBounds(28, 151, 22, 28);
            _keepSelection.SetBounds(28, 214, 22, 28);
            _fullHotkey.SetBounds(Math.Max(380, _correctionCard.ClientSize.Width - hotkeyWidth - 28), 76, hotkeyWidth, 42);
            _wordHotkey.SetBounds(Math.Max(380, _correctionCard.ClientSize.Width - hotkeyWidth - 28), 141, hotkeyWidth, 42);
            _fullLabel.SetBounds(58, 82, textWidth, 32);
            _wordLabel.SetBounds(58, 147, textWidth, 52);
            _keepSelectionLabel.SetBounds(58, 210, textWidth, 36);
            _correctionTitle.SetBounds(26, 16, 360, 46);
            _hotkeyHelp.Left = 28;
            _hotkeyHelp.Width = Math.Max(300, _correctionCard.ClientSize.Width - 56);
        }

        _language.Width = Math.Max(220, _preferencesCard.ClientSize.Width - 56);
        _startup.Width = Math.Max(220, _preferencesCard.ClientSize.Width - 56);
        _installedTitle.Width = Math.Max(220, _preferencesCard.ClientSize.Width - 56);
        _availableLayouts.Width = Math.Max(220, _preferencesCard.ClientSize.Width - 56);
        _info.Width = Math.Max(220, _preferencesCard.ClientSize.Width - 56);
        _info.Height = Math.Max(118, _preferencesCard.ClientSize.Height - 370);
        int actionTop = Math.Max(430, _preferencesCard.ClientSize.Height - 66);
        int actionWidth = Math.Max(145, (_preferencesCard.ClientSize.Width - 68) / 2);
        _changelog.SetBounds(28, actionTop, actionWidth, 42);
        _clearLog.SetBounds(40 + actionWidth, actionTop, actionWidth, 42);
        if (UiText.IsRtl)
        {
            _preferencesTitle.SetBounds(26, 16, Math.Max(200, _preferencesCard.ClientSize.Width - 52), 46);
            _languageLabel.Width = 120;
            _languageLabel.Left = Math.Max(28, _preferencesCard.ClientSize.Width - 28 - _languageLabel.Width);
        }
        else
        {
            _preferencesTitle.SetBounds(26, 16, 310, 46);
            _languageLabel.Left = 28;
            _languageLabel.Width = 120;
        }
        _save.Left = _footer.ClientSize.Width - _footer.Padding.Right - _save.Width;
        _save.Top = 20;
        _defaults.Left = _save.Left - 14 - _defaults.Width;
        _defaults.Top = 20;
    }

    private void ApplyLanguage()
    {
        CrashLogger.Write($"ApplyLanguage begin: language={UiText.Language}, rtl={UiText.IsRtl}");
        bool rtl = UiText.IsRtl;
        RightToLeft = RightToLeft.No;
        RightToLeftLayout = false;
        _header.RightToLeft = RightToLeft.No;
        _logo.RightToLeft = RightToLeft.No;
        _title.RightToLeft = RightToLeft.No;
        _tagline.RightToLeft = RightToLeft.No;
        _versionBadge.RightToLeft = RightToLeft.No;
        _title.TextAlign = ContentAlignment.MiddleLeft;
        _tagline.TextAlign = ContentAlignment.MiddleLeft;

        Text = $"{AppInfo.DisplayName} — {UiText.Get("settings")}";
        _tagline.Text = "Type in the right language";
        _versionBadge.Text = $"v{AppInfo.Version}";
        _correctionTitle.Text = UiText.Get("correction_section");
        _preferencesTitle.Text = UiText.Get("preferences_section");
        _languageLabel.Text = UiText.Get("language");
        _startup.Text = UiText.Get("startup");
        _fullLabel.Text = UiText.Get("full_text");
        _wordLabel.Text = UiText.Get("selection_word");
        _keepSelectionLabel.Text = UiText.Get("keep_selection");
        _hotkeyHelp.Text = UiText.Get("hotkey_hint");
        _installedTitle.Text = UiText.Get("installed_layouts");
        _info.Text = UiText.Get("supported_info");
        _availableLayouts.Text = string.Join("   •   ", KeyboardLayout.AvailableLanguages.Select(KeyboardLayout.DisplayName));
        _changelog.Text = UiText.Get("changelog");
        _clearLog.Text = UiText.Get("clear_log");
        _defaults.Text = UiText.Get("defaults");
        _save.Text = UiText.Get("save");

        RightToLeft textDirection = rtl ? RightToLeft.Yes : RightToLeft.No;
        HorizontalAlignment textAlignment = HorizontalAlignment.Left;
        ContentAlignment labelAlignment = ContentAlignment.MiddleLeft;

        _correctionTitle.RightToLeft = textDirection; _correctionTitle.TextAlign = labelAlignment;
        _preferencesTitle.RightToLeft = textDirection; _preferencesTitle.TextAlign = labelAlignment;
        _installedTitle.RightToLeft = textDirection; _installedTitle.TextAlign = labelAlignment;
        _languageLabel.RightToLeft = textDirection; _languageLabel.TextAlign = textAlignment;
        _fullLabel.RightToLeft = textDirection; _fullLabel.TextAlign = textAlignment;
        _wordLabel.RightToLeft = textDirection; _wordLabel.TextAlign = textAlignment;
        _keepSelectionLabel.RightToLeft = textDirection; _keepSelectionLabel.TextAlign = textAlignment;
        _hotkeyHelp.RightToLeft = textDirection; _hotkeyHelp.TextAlign = textAlignment;
        _availableLayouts.RightToLeft = textDirection; _availableLayouts.TextAlign = textAlignment;
        _info.RightToLeft = textDirection; _info.TextAlign = textAlignment;
        _language.RightToLeft = textDirection;
        _startup.RightToLeft = textDirection; _startup.TextAlign = labelAlignment;

        string selectedCode = (_language.SelectedItem as LanguageItem)?.Code ?? _settings.Language;
        _updatingLanguage = true;
        try
        {
            _language.BeginUpdate();
            _language.Items.Clear();
            _language.Items.AddRange(new object[] { new LanguageItem("en"), new LanguageItem("ru"), new LanguageItem("he") });
            SelectLanguage(selectedCode);
            _language.EndUpdate();
        }
        finally { _updatingLanguage = false; }
        ResizeLayout();
        CrashLogger.Write($"ApplyLanguage end: language={UiText.Language}");
    }

    private void RestoreDefaults()
    {
        DialogResult answer = MessageBox.Show(this, UiText.Get("defaults_confirm"), AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;
        AppSettings defaults = AppSettings.CreateDefault();
        _startup.Checked = defaults.StartWithWindows;
        _full.Checked = defaults.FullTextEnabled;
        _word.Checked = defaults.LastWordEnabled;
        _keepSelection.Checked = defaults.KeepSelectionAfterCorrection;
        _fullHotkey.Text = defaults.FullTextHotkey;
        _wordHotkey.Text = defaults.LastWordHotkey;
        UiText.Language = defaults.Language;
        SelectLanguage(defaults.Language);
        ApplyLanguage();
    }

    private SelectableLabel MakeSelectableLabel(int left, int top, int width, int height) => new()
    {
        Left = left, Top = top, Width = width, Height = height, BackColor = Color.White,
        ForeColor = Color.FromArgb(31, 45, 70), Font = new Font("Segoe UI", 10.5F)
    };

    private ModernButton MakeHotkeyButton(string selected)
    {
        var button = new ModernButton { Primary = false, Text = HotkeyDefinition.IsValid(selected) ? HotkeyDefinition.Format(HotkeyDefinition.Parse(selected)) : "" };
        button.Click += (_, _) => EditHotkey(button);
        return button;
    }

    private void EditHotkey(ModernButton target)
    {
        using var editor = new HotkeyEditorForm(UiText.Language);
        if (editor.ShowDialog(this) == DialogResult.OK) target.Text = editor.Hotkey;
    }

    private void SaveSettings()
    {
        string full = _fullHotkey.Text;
        string word = _wordHotkey.Text;
        if (!HotkeyDefinition.IsValid(full) || !HotkeyDefinition.IsValid(word))
        {
            MessageBox.Show(this, UiText.Get("invalid_hotkey"), AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning); return;
        }
        if (HotkeyDefinition.Parse(full).SetEquals(HotkeyDefinition.Parse(word)))
        {
            MessageBox.Show(this, UiText.Get("duplicate_hotkey"), AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning); return;
        }
        _settings.StartWithWindows = _startup.Checked;
        _settings.FullTextEnabled = _full.Checked;
        _settings.LastWordEnabled = _word.Checked;
        _settings.KeepSelectionAfterCorrection = _keepSelection.Checked;
        _settings.FullTextHotkey = full;
        _settings.LastWordHotkey = word;
        _settings.Language = (_language.SelectedItem as LanguageItem)?.Code ?? "en";
        UiText.Language = _settings.Language;
        StartupManager.SetEnabled(_settings.StartWithWindows);
        _settings.Save();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void SelectLanguage(string code)
    {
        for (int i = 0; i < _language.Items.Count; i++)
            if (_language.Items[i] is LanguageItem item && item.Code == code) { _language.SelectedIndex = i; return; }
        if (_language.Items.Count > 0) _language.SelectedIndex = 0;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _logo?.Image?.Dispose(); Icon?.Dispose(); }
        base.Dispose(disposing);
    }

    private sealed class LanguageItem
    {
        public string Code { get; }
        public LanguageItem(string code) { Code = code; }
        public override string ToString() => Code switch { "ru" => UiText.Get("russian"), "he" => UiText.Get("hebrew"), _ => UiText.Get("english") };
    }
}
