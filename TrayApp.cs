namespace SoloScreen;

/// <summary>
/// 常驻托盘的应用上下文：托盘图标、右键菜单、全局快捷键、设置窗口管理。
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly AppConfig _config;
    private readonly NotifyIcon _tray;
    private readonly HotkeyWindow _hotkeyWindow = new();
    private SettingsForm? _settingsForm;
    private readonly ToolStripMenuItem _settingsItem = new(Strings.TraySettings);
    private readonly ToolStripMenuItem _exitItem = new(Strings.TrayExit);

    public TrayApp()
    {
        _config = AppConfig.Load();

        _settingsItem.Click += (_, _) => ShowSettings();
        _exitItem.Click += (_, _) => ExitApp();

        var menu = new ContextMenuStrip();
        menu.Items.Add(_settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_exitItem);

        _tray = new NotifyIcon
        {
            Icon = AppIcon.Get(),
            Text = Strings.TrayTooltip,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ShowSettings();

        _hotkeyWindow.HotkeyPressed += OnHotkey;
        ApplyHotkey(notifyError: true);
        SoundUtil.Prepare(); // 预加载音效，避免首次截图丢开头
        AutoStartUtil.MigrateLegacyEntries(); // 清理改名前遗留的启动项

        bool elevated = ElevationUtil.IsElevated();
        SimpleLog.Write($"SoloScreen 已启动（托盘模式）。配置的热键：{_config.Hotkey}；管理员权限：{elevated}；音效：{SoundUtil.SourceDescription}");
    }

    /// <summary>启动时是否应静默到托盘（配置项"启动时不显示界面，最小化到托盘"）。</summary>
    internal bool StartMinimized => _config.StartMinimized;

    /// <summary>按当前配置注册全局快捷键。</summary>
    private void ApplyHotkey(bool notifyError)
    {
        if (!HotkeyUtil.TryParse(_config.Hotkey, out var modifiers, out var vk, out var display))
        {
            if (notifyError)
                Notify(Strings.HotkeyInvalidNotify(_config.Hotkey), ToolTipIcon.Warning);
            return;
        }

        if (!_hotkeyWindow.Register(modifiers, vk) && notifyError)
            Notify(Strings.HotkeyRegisterFailed(display), ToolTipIcon.Warning);
    }

    private void OnHotkey()
    {
        try
        {
            var result = CaptureService.CaptureAndSave(_config);

            // 保存/剪贴板失败：弹置顶提示框（同一时间只保留一个，不叠加）
            if (result.SaveError is not null) AlertForm.ShowOnce(result.SaveError);
            if (result.ClipboardError is not null) AlertForm.ShowOnce(result.ClipboardError);

            if (_config.ShowNotification)
            {
                string? text = (result.Saved, result.Copied) switch
                {
                    (true, true) => Strings.SavedAndCopied(result.FileName!),
                    (true, false) => Strings.Saved(result.FileName!),
                    (false, true) => Strings.Copied,
                    _ => null,
                };
                if (text is not null)
                    _tray.ShowBalloonTip(2000, "SoloScreen", text, ToolTipIcon.Info);
            }
        }
        catch (Exception ex)
        {
            SimpleLog.Write("截图失败：" + ex);
            Notify(Strings.CaptureFailed(ex.Message), ToolTipIcon.Error);
        }
    }


    /// <summary>语言切换后刷新托盘菜单与提示文字。</summary>
    internal void RefreshLanguage()
    {
        _settingsItem.Text = Strings.TraySettings;
        _exitItem.Text = Strings.TrayExit;
        _tray.Text = Strings.TrayTooltip;
    }

    private void Notify(string text, ToolTipIcon icon)
        => _tray.ShowBalloonTip(3000, "SoloScreen", text, icon);

    internal void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            BringToFrontHard(_settingsForm);
            return;
        }

        _settingsForm = new SettingsForm(_config);
        _settingsForm.SettingsSaved += () => ApplyHotkey(notifyError: true);
        _settingsForm.LanguageChanged += RefreshLanguage;
        _settingsForm.RestartRequested += () =>
        {
            // 以新权限重启：先让本实例退出，再启动新实例（新实例带 --relaunch，会等待互斥量释放）
            if (ElevationUtil.RelaunchElevated())
            {
                ExitApp();
            }
            else
            {
                Notify(Strings.ElevateFailed, ToolTipIcon.Warning);
            }
        };
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.ExitRequested += ExitApp;
        _settingsForm.Show();
        BringToFrontHard(_settingsForm);
    }

    /// <summary>
    /// 把窗口唤到所有普通窗口之前。
    /// 做法：短暂置顶再取消置顶 + 附加到前台线程后 SetForegroundWindow（否则前台锁定会拒绝）
    /// + 重设 TopMost 保证真在最上面。
    /// 不能永久置顶——否则设置窗口会始终盖住自己弹出的模态对话框（快捷键设置、浏览文件夹等）。
    /// </summary>
    private static void BringToFrontHard(Form form)
    {
        form.Activate();
        form.TopMost = true;
        form.TopMost = false;

        var foreground = GetForegroundWindow();
        uint fgThread = GetWindowThreadProcessId(foreground, IntPtr.Zero);
        uint myThread = GetCurrentThreadId();

        bool attached = fgThread != 0 && fgThread != myThread && AttachThreadInput(myThread, fgThread, true);
        try
        {
            BringWindowToTop(form.Handle);
            SetForegroundWindow(form.Handle);
            form.Activate();
        }
        finally
        {
            if (attached) AttachThreadInput(myThread, fgThread, false);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    private void ExitApp()
    {
        _hotkeyWindow.Unregister();
        SoundUtil.Shutdown();
        _tray.Visible = false;
        _tray.Dispose();
        _settingsForm?.Close();
        Application.Exit();
    }
}
