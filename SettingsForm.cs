using System.Windows.Forms;

namespace SoloScreen;

/// <summary>设置界面。点右上角关闭只是关闭窗口，程序仍常驻托盘。</summary>
internal sealed class SettingsForm : Form
{
    /// <summary>点击“保存”并成功写入配置后触发（用于重新注册快捷键等）。</summary>
    public event Action? SettingsSaved;

    /// <summary>需要以新的权限级别重启程序时触发。</summary>
    public event Action? RestartRequested;

    /// <summary>点击"退出程序"时触发。</summary>
    public event Action? ExitRequested;

    /// <summary>切换界面语言后触发（用于刷新托盘菜单/提示）。</summary>
    public event Action? LanguageChanged;

    private readonly AppConfig _config;

    private Button _saveBtn = null!;
    private Button _minimizeBtn = null!;
    private Button _exitBtn = null!;
    private bool _suppressDirty;
    private TableLayoutPanel _table = null!;
    private FlowLayoutPanel _buttons = null!;

    private readonly ComboBox _screenCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _langCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox _hotkeyBox = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly TextBox _dirBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _patternBox = new() { Dock = DockStyle.Fill };
    private readonly Button _browseBtn = new() { Text = "浏览…", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Button _hotkeyBtn = new() { Text = "设置…", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label _langLabel = new() { Text = "显示语言：", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label _screenLabel = new() { Text = "截取屏幕：", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label _hotkeyLabel = new() { Text = "快捷键：", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label _dirLabel = new() { Text = "保存目录：", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label _patternLabel = new() { Text = "文件名：", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label _patternHint = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly Label _adminHint = new()
    {
        AutoSize = true,
        ForeColor = SystemColors.GrayText,
        Margin = new Padding(3, 6, 3, 6),
    };
    private readonly CheckBox _saveFileCheck = new() { Text = "截图时保存文件", AutoSize = true };
    private readonly CheckBox _copyCheck = new() { Text = "截图时复制到剪贴板", AutoSize = true };
    private readonly CheckBox _soundCheck = new() { Text = "截图时播放音效", AutoSize = true };
    private readonly CheckBox _notifyCheck = new() { Text = "截图后显示系统通知", AutoSize = true };
    private readonly CheckBox _autoStartCheck = new() { Text = "开机时自动启动", AutoSize = true };
    private readonly CheckBox _startMinimizedCheck = new() { Text = "启动时最小化到托盘", AutoSize = true };
    private readonly Label _permissionLabel = new()
    {
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(3, 9, 12, 3),
    };
    private readonly Button _elevateButton = new()
    {
        Text = "以管理员身份运行",
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 5, 3, 3),
    };

    private string _hotkey; // 存储格式，如 "Pause"、"Ctrl+Alt+S"

    private sealed record ScreenItem(string Device, string Display)
    {
        public override string ToString() => Display;
    }

    private sealed record LanguageItem(string Text, bool English)
    {
        public override string ToString() => Text;
    }

    public SettingsForm(AppConfig config)
    {
        _config = config;
        _hotkey = config.Hotkey;

        Icon = AppIcon.Get();
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 9.5f);
        ClientSize = new Size(592, 500); // 初始值，随后 AdjustSize 按内容动态调整

        BuildLayout();

        _saveFileCheck.CheckedChanged += (_, _) => UpdateSaveControls();
        _elevateButton.Click += (_, _) => OnElevateClicked();
        WireChangeTracking();

        // 语言下拉框：选项文本本身不翻译（"简体中文" / "English"）
        _langCombo.Items.Add(new LanguageItem("简体中文", false));
        _langCombo.Items.Add(new LanguageItem("English", true));
        _langCombo.SelectedIndex = Strings.English ? 1 : 0;
        _langCombo.SelectedIndexChanged += OnLanguageChanged;

        ApplyTexts();
        LoadValues();
        AdjustSize();
    }

    /// <summary>挂上"改了就激活保存按钮"的监听。</summary>
    private void WireChangeTracking()
    {
        _screenCombo.SelectedIndexChanged += (_, _) => MarkDirty();
        _hotkeyBox.TextChanged += (_, _) => MarkDirty();
        _dirBox.TextChanged += (_, _) => MarkDirty();
        _patternBox.TextChanged += (_, _) => MarkDirty();
        _saveFileCheck.CheckedChanged += (_, _) => MarkDirty();
        _copyCheck.CheckedChanged += (_, _) => MarkDirty();
        _soundCheck.CheckedChanged += (_, _) => MarkDirty();
        _notifyCheck.CheckedChanged += (_, _) => MarkDirty();
        _autoStartCheck.CheckedChanged += (_, _) => MarkDirty();
        _startMinimizedCheck.CheckedChanged += (_, _) => MarkDirty();
    }

    /// <summary>“截图时保存文件”取消勾选时，目录/文件名相关控件置灰。</summary>
    private void UpdateSaveControls()
    {
        bool on = _saveFileCheck.Checked;
        _dirBox.Enabled = on;
        _browseBtn.Enabled = on;
        _patternBox.Enabled = on;
    }

    /// <summary>把当前语言的文本赋给所有静态控件。</summary>
    private void ApplyTexts()
    {
        Text = Strings.AppTitle;
        _langLabel.Text = Strings.DisplayLanguage;
        _screenLabel.Text = Strings.CaptureScreen;
        _hotkeyLabel.Text = Strings.Hotkey;
        _dirLabel.Text = Strings.SaveDirectory;
        _patternLabel.Text = Strings.FileName;
        _patternHint.Text = Strings.PatternHint;
        _hotkeyBtn.Text = Strings.SetHotkeyBtn;
        _browseBtn.Text = Strings.BrowseBtn;
        _saveFileCheck.Text = Strings.SaveFileCheck;
        _copyCheck.Text = Strings.CopyCheck;
        _soundCheck.Text = Strings.SoundCheck;
        _notifyCheck.Text = Strings.NotifyCheck;
        _autoStartCheck.Text = Strings.AutoStartCheck;
        _startMinimizedCheck.Text = Strings.StartMinimizedCheck;
        _adminHint.Text = Strings.AdminHint;
        _elevateButton.Text = Strings.ElevateBtn;
        _exitBtn.Text = Strings.ExitBtn;
        _minimizeBtn.Text = Strings.MinimizeBtn;
        _saveBtn.Text = Strings.SaveBtn;
        UpdatePermissionUi();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 底部按钮

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            Padding = new Padding(14),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // 行 0：显示语言
        table.Controls.Add(_langLabel, 0, 0);
        table.Controls.Add(_langCombo, 1, 0);
        table.SetColumnSpan(_langCombo, 2);

        // 行 1：屏幕
        table.Controls.Add(_screenLabel, 0, 1);
        table.Controls.Add(_screenCombo, 1, 1);
        table.SetColumnSpan(_screenCombo, 2);

        // 行 2：快捷键
        table.Controls.Add(_hotkeyLabel, 0, 2);
        table.Controls.Add(_hotkeyBox, 1, 2);
        _hotkeyBtn.Click += (_, _) => OnSetHotkey();
        table.Controls.Add(_hotkeyBtn, 2, 2);

        // 行 3：保存目录
        table.Controls.Add(_dirLabel, 0, 3);
        table.Controls.Add(_dirBox, 1, 3);
        _browseBtn.Click += (_, _) => OnBrowseDir();
        table.Controls.Add(_browseBtn, 2, 3);

        // 行 4：文件名格式
        table.Controls.Add(_patternLabel, 0, 4);
        table.Controls.Add(_patternBox, 1, 4);
        table.SetColumnSpan(_patternBox, 2);

        // 行 5：格式说明（占满三列，减少换行）
        table.Controls.Add(_patternHint, 0, 5);
        table.SetColumnSpan(_patternHint, 3);

        // 行 6~7：截图时的两个开关（紧跟文件名设置之后）
        AddFullRow(table, 6, _saveFileCheck);
        AddFullRow(table, 7, _copyCheck);

        // 行 8~11：其余复选项
        AddFullRow(table, 8, _soundCheck);
        AddFullRow(table, 9, _notifyCheck);
        AddFullRow(table, 10, _autoStartCheck);
        AddFullRow(table, 11, _startMinimizedCheck);

        // 行 12：当前权限状态 +（普通用户时）提权按钮
        var permissionRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
        };
        permissionRow.Controls.Add(_permissionLabel);
        permissionRow.Controls.Add(_elevateButton);
        table.Controls.Add(permissionRow, 0, 12);
        table.SetColumnSpan(permissionRow, 3);

        // 行 13：管理员权限说明
        table.Controls.Add(_adminHint, 0, 13);
        table.SetColumnSpan(_adminHint, 3);

        root.Controls.Add(table, 0, 0);

        // 底部按钮（靠右排列：保存 | 最小化到托盘 | 退出程序）
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8, 4, 14, 10),
            AutoSize = true,
        };

        _exitBtn = new Button { Text = "退出程序", AutoSize = true };
        _exitBtn.Click += (_, _) => ExitRequested?.Invoke();

        _minimizeBtn = new Button { Text = "最小化到托盘", AutoSize = true };
        _minimizeBtn.Click += (_, _) => Close();

        // 保存按钮默认禁用，改了设置才可用
        _saveBtn = new Button { Text = "保存", AutoSize = true, Enabled = false };
        _saveBtn.Click += (_, _) => OnSave();

        buttons.Controls.Add(_exitBtn);
        buttons.Controls.Add(_minimizeBtn);
        buttons.Controls.Add(_saveBtn);
        root.Controls.Add(buttons, 0, 1);

        Controls.Add(root);

        // 注意：故意不设置 AcceptButton（默认按钮）。
        // Windows 开启"自动将指针移动到对话框中的默认按钮"（鼠标属性 → 指针选项 → 贴靠）后，
        // 带默认按钮的窗口每次打开都会把鼠标光标吸到默认按钮上；不设默认按钮即可避免。
        _table = table;
        _buttons = buttons;
    }

    /// <summary>
    /// 按内容动态调整窗口高度。语言切换后文本长短变化（尤其英文权限说明更长），
    /// 用 GetPreferredSize 计算 table 的实际所需高度，避免中文时留大片空白。
    /// 不能用 Form.AutoSize（会因 TableLayoutPanel 百分比行导致高度塌陷），所以手动算。
    /// </summary>
    private void AdjustSize()
    {
        const int width = 592;
        int tableHeight = _table.GetPreferredSize(new Size(width, 0)).Height;
        int buttonsHeight = _buttons.PreferredSize.Height;
        ClientSize = new Size(width, tableHeight + buttonsHeight);
    }

    /// <summary>切换语言：立即生效并保存，无需重启。</summary>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_suppressDirty) return;

        bool english = (_langCombo.SelectedItem as LanguageItem)?.English ?? false;
        if (english == Strings.English) return;

        Strings.English = english;
        _config.Language = Strings.CurrentValue;

        _suppressDirty = true; // 刷新屏幕列表会触发 SelectedIndexChanged，别把它算作"用户修改"
        try
        {
            ApplyTexts();
            ReloadScreenList();
        }
        finally
        {
            _suppressDirty = false;
        }

        AdjustSize(); // 文本长度变了，重新按内容调整窗口高度

        _config.Save();
        LanguageChanged?.Invoke();
    }

    /// <summary>任一设置项被修改后调用：把"保存"按钮变为可用。</summary>
    private void MarkDirty()
    {
        if (_suppressDirty) return;
        _saveBtn.Enabled = true;
    }

    private static void AddFullRow(TableLayoutPanel table, int row, Control control)
    {
        table.Controls.Add(control, 0, row);
        table.SetColumnSpan(control, 3);
    }

    /// <summary>加载屏幕下拉列表（显示名含"主显示器/内置显示器"等本地化文字）。</summary>
    private void ReloadScreenList()
    {
        // 记住当前选中，重载后尽量保持
        string? selectedDevice = null;
        if (_screenCombo.SelectedIndex >= 0)
            selectedDevice = ((ScreenItem)_screenCombo.Items[_screenCombo.SelectedIndex]!).Device;
        selectedDevice ??= _config.ScreenDevice;

        _screenCombo.Items.Clear();

        var screens = Screen.AllScreens;
        var monitorNames = MonitorInfo.GetFriendlyNames();
        int target = -1;
        for (int i = 0; i < screens.Length; i++)
        {
            var s = screens[i];
            var desc = $"{i + 1}. ";
            if (monitorNames.TryGetValue(s.DeviceName, out var monitorName) && monitorName.Length > 0)
                desc += $"{monitorName} · ";
            desc += $"{s.Bounds.Width}×{s.Bounds.Height}";
            if (s.Primary) desc += Strings.PrimarySuffix;
            _screenCombo.Items.Add(new ScreenItem(s.DeviceName, desc));

            if (s.DeviceName == selectedDevice) target = i;
            else if (target < 0 && s.Primary) target = i;
        }
        if (target < 0) target = 0;
        _screenCombo.SelectedIndex = target;
    }

    private void LoadValues()
    {
        _suppressDirty = true; // 初始化赋值不算"用户修改"

        ReloadScreenList();

        _hotkeyBox.Text = HotkeyUtil.ToDisplay(_hotkey);
        _dirBox.Text = _config.GetSaveDirectory();
        _patternBox.Text = string.IsNullOrWhiteSpace(_config.FileNamePattern)
            ? FileNameBuilder.DefaultPattern
            : _config.FileNamePattern;
        _saveFileCheck.Checked = _config.SaveFile;
        _copyCheck.Checked = _config.CopyToClipboard;
        _soundCheck.Checked = _config.PlaySound;
        _notifyCheck.Checked = _config.ShowNotification;
        _startMinimizedCheck.Checked = _config.StartMinimized;
        UpdateSaveControls();
        UpdatePermissionUi();

        // 开机启动以注册表/计划任务的实际状态为准
        try { _autoStartCheck.Checked = AutoStartUtil.IsEnabled(); }
        catch { _autoStartCheck.Checked = _config.AutoStart; }

        _suppressDirty = false;
        _saveBtn.Enabled = false; // 没有改动，"保存"保持禁用
    }

    /// <summary>刷新“当前权限”显示：普通用户时在文字后面给出提权按钮。</summary>
    private void UpdatePermissionUi()
    {
        bool elevated = ElevationUtil.IsElevated();
        _permissionLabel.Text = elevated ? Strings.PermissionAdmin : Strings.PermissionUser;
        _elevateButton.Visible = !elevated;
    }

    private void OnElevateClicked()
    {
        var answer = MessageBox.Show(this,
            Strings.ElevateConfirm,
            "SoloScreen", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (answer != DialogResult.OK) return;

        RestartRequested?.Invoke();
    }

    private void OnSetHotkey()
    {
        using var dlg = new HotkeyCaptureForm();
        if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dlg.Result))
        {
            _hotkey = dlg.Result;
            _hotkeyBox.Text = HotkeyUtil.ToDisplay(_hotkey);
        }
    }

    private void OnBrowseDir()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = Strings.BrowseDirDesc,
            UseDescriptionForTitle = true,
        };
        try { dlg.InitialDirectory = _dirBox.Text; } catch { /* 忽略非法路径 */ }
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _dirBox.Text = dlg.SelectedPath;
        }
    }

    private void OnSave()
    {
        // 校验
        if (!HotkeyUtil.TryParse(_hotkey, out _, out _, out _))
        {
            MessageBox.Show(this, Strings.HotkeyInvalid, "SoloScreen",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        bool saveFile = _saveFileCheck.Checked;
        bool copyToClipboard = _copyCheck.Checked;

        // 两个都不勾等于截图没有任何结果，确认一下
        if (!saveFile && !copyToClipboard)
        {
            var confirm = MessageBox.Show(this, Strings.BothDisabledConfirm,
                "SoloScreen", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;
        }

        // 只在需要保存文件时才校验保存目录
        var dir = _dirBox.Text.Trim();
        if (saveFile)
        {
            if (dir.Length == 0)
            {
                MessageBox.Show(this, Strings.SetSaveDirectory, "SoloScreen",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try { Directory.CreateDirectory(dir); }
            catch (Exception ex)
            {
                MessageBox.Show(this, Strings.CreateDirFailed(ex.Message), "SoloScreen",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        // 写入配置
        _config.ScreenDevice = ((ScreenItem)_screenCombo.SelectedItem!).Device;
        _config.Hotkey = _hotkey;
        _config.SaveDirectory = dir;
        _config.SaveFile = saveFile;
        _config.CopyToClipboard = copyToClipboard;
        _config.PlaySound = _soundCheck.Checked;
        _config.ShowNotification = _notifyCheck.Checked;
        _config.AutoStart = _autoStartCheck.Checked;
        _config.StartMinimized = _startMinimizedCheck.Checked;
        _config.FileNamePattern = string.IsNullOrWhiteSpace(_patternBox.Text)
            ? FileNameBuilder.DefaultPattern
            : _patternBox.Text.Trim();

        // 开机启动：以普通权限运行则写注册表启动项；
        // 以管理员权限运行（或之前已建过计划任务）则改用"登录时运行 + 最高权限"计划任务，登录时不弹 UAC
        bool asAdmin = ElevationUtil.IsElevated() || AutoStartUtil.HasLogonTask();
        try { AutoStartUtil.Set(_autoStartCheck.Checked, asAdmin); }
        catch (Exception ex)
        {
            MessageBox.Show(this, Strings.AutoStartFailed(ex.Message), "SoloScreen",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        _config.Save();
        SettingsSaved?.Invoke();

        _saveBtn.Enabled = false; // 已保存，暂无待保存的改动
        MessageBox.Show(this, Strings.SaveSuccess, "SoloScreen",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
