using System.Globalization;

namespace SoloScreen;

/// <summary>
/// 界面文本（简体中文 / English）。集中管理，切换语言后重新给各控件赋值 Text 即可，无需重启。
/// 品牌名 "SoloScreen" 不翻译。
/// </summary>
internal static class Strings
{
    /// <summary>当前是否使用英语。true = English，false = 简体中文。</summary>
    public static bool English { get; set; }

    // 语言配置值：空字符串 = 跟随系统（auto）
    public const string AutoValue = "";
    public const string EnValue = "en";
    public const string ZhValue = "zh-Hans";

    /// <summary>启动时根据已保存的语言配置 + 系统语言决定当前语言。</summary>
    public static void Initialize(string? configValue)
    {
        English = configValue switch
        {
            EnValue => true,
            ZhValue => false,
            _ => !IsSystemChinese(),
        };
    }

    /// <summary>当前语言的配置值（用于保存到 config.json）。</summary>
    public static string CurrentValue => English ? EnValue : ZhValue;

    private static bool IsSystemChinese() =>
        CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    private static string T(string zh, string en) => English ? en : zh;

    // ——— 设置窗口 ———

    public static string AppTitle => T($"SoloScreen v{AppInfo.Version} 设置", $"SoloScreen v{AppInfo.Version} Settings");
    public static string DisplayLanguage => T("显示语言：", "Language:");
    public static string CaptureScreen => T("截取屏幕：", "Screen:");
    public static string Hotkey => T("快捷键：", "Hotkey:");
    public static string SaveDirectory => T("保存目录：", "Save to:");
    public static string FileName => T("文件名：", "File name:");
    public static string SetHotkeyBtn => T("设置…", "Set…");
    public static string BrowseBtn => T("浏览…", "Browse…");
    public static string ExitBtn => T("退出程序", "Exit");
    public static string MinimizeBtn => T("最小化到托盘", "Minimize to tray");
    public static string SaveBtn => T("保存", "Save");

    public static string PatternHint => T(
        "可用占位符：$YYYY $MM $DD $hh $mm $ss $fff（年/月/日/时/分/秒/毫秒），保存为 PNG。",
        "Placeholders: $YYYY $MM $DD $hh $mm $ss $fff (year/month/day/hour/minute/second/millisecond), saved as PNG.");

    public static string SaveFileCheck => T("截图时保存文件", "Save file when capturing");
    public static string CopyCheck => T("截图时复制到剪贴板", "Copy to clipboard when capturing");
    public static string SoundCheck => T("截图时播放音效", "Play sound when capturing");
    public static string NotifyCheck => T("截图后显示系统通知", "Show notification after capture");
    public static string AutoStartCheck => T("开机时自动启动", "Start with Windows");
    public static string StartMinimizedCheck => T("启动时最小化到托盘", "Minimize to tray on startup");

    public static string PermissionAdmin => T("当前权限：管理员", "Permission: Administrator");
    public static string PermissionUser => T("当前权限：普通用户", "Permission: Standard user");
    public static string ElevateBtn => T("以管理员身份运行", "Run as administrator");

    public static string AdminHint => T(
        "权限说明：Windows 的安全机制（UIPI）规定，当任务管理器、管理员命令行等提权窗口是前台的焦点窗口时，"
        + "普通权限程序收不到全局热键。因此当本程序以普通用户身份运行时，在上述场景里无法触发截图动作。"
        + "如果你想在这些场景里也可以截图，需要使用管理员权限。",
        "About permissions: Windows security (UIPI) prevents non-elevated programs from receiving global hotkeys "
        + "while an elevated window (Task Manager, admin command prompt, etc.) is in the foreground. "
        + "So when this program runs as a standard user, it cannot capture in those scenarios. "
        + "Run it as administrator if you want to capture in those cases too.");

    public static string PrimarySuffix => T("（主显示器）", " (primary)");
    public static string BuiltInDisplay => T("内置显示器", "Built-in display");
    public static string BuiltInDisplayFmt(string name) =>
        T($"内置显示器（{name}）", $"Built-in display ({name})");

    // ——— 设置窗口的对话框 ———

    public static string HotkeyInvalid => T("快捷键无效，请重新设置。", "Invalid hotkey. Please set it again.");
    public static string SetSaveDirectory => T("请设置保存目录。", "Please set a save directory.");
    public static string BrowseDirDesc => T("选择截图保存目录", "Choose a folder to save screenshots");
    public static string CreateDirFailed(string detail) =>
        T($"保存目录无法创建：{detail}", $"Cannot create save directory: {detail}");
    public static string AutoStartFailed(string detail) =>
        T($"设置开机启动失败：{detail}", $"Failed to set auto-start: {detail}");
    public static string SaveSuccess => T("保存成功。", "Saved.");
    public static string BothDisabledConfirm => T(
        "两项都未勾选：截图后既不会保存文件，也不会复制到剪贴板，本次截图没有任何结果。\n\n仍要保存设置吗？",
        "Both options are unchecked: the capture will neither be saved to a file nor copied to the clipboard.\n\nSave these settings anyway?");
    public static string ElevateConfirm => T(
        "要切换到管理员身份，需要重启该程序。如果你点击确认按钮，该程序会请求 UAC 权限并重新启动。",
        "Switching to administrator requires restarting the program. If you confirm, the program will request UAC elevation and restart.");

    // ——— 托盘 ———

    public static string TraySettings => T("设置(&S)", "&Settings");
    public static string TrayExit => T("退出(&X)", "E&xit");
    public static string TrayTooltip => T("SoloScreen 截图工具", "SoloScreen Screenshot Tool");
    public static string HotkeyInvalidNotify(string hotkey) =>
        T($"快捷键配置无效：{hotkey}", $"Invalid hotkey: {hotkey}");
    public static string HotkeyRegisterFailed(string display) =>
        T($"快捷键【{display}】注册失败，可能已被其他程序占用。", $"Failed to register hotkey [{display}]. It may be used by another program.");
    public static string SavedAndCopied(string fileName) =>
        T($"已保存 {fileName}\n并复制到剪贴板", $"Saved {fileName}\nand copied to clipboard");
    public static string Saved(string fileName) => T($"已保存 {fileName}", $"Saved {fileName}");
    public static string Copied => T("已复制到剪贴板", "Copied to clipboard");
    public static string CaptureFailed(string detail) => T($"截图失败：{detail}", $"Capture failed: {detail}");
    public static string ElevateFailed => T(
        "以管理员身份重启失败（可能取消了 UAC 确认）。",
        "Failed to restart as administrator (UAC may have been cancelled).");

    // ——— 通用 / 其它 ———

    public static string AlreadyRunning => T(
        "SoloScreen 已经在运行，请查看系统托盘。",
        "SoloScreen is already running. Check the system tray.");
    public static string OK => T("确定", "OK");
    public static string SetHotkeyTitle => T("设置快捷键", "Set Hotkey");
    public static string HotkeyHint => T(
        "请直接按下新的快捷键（可配合 Ctrl / Alt / Shift，例如：Ctrl + Alt + S），按 Esc 取消：",
        "Press the new hotkey (with Ctrl / Alt / Shift, e.g. Ctrl + Alt + S). Press Esc to cancel:");

    // ——— 截图错误（CaptureService） ———

    public static string CreateDirError => T(
        "创建截图目录时失败，所以本次截图未保存到文件。",
        "Failed to create the screenshot directory, so this capture was not saved to a file.");
    public static string SaveFileErrorPrefix => T(
        "保存截图文件时失败，所以本次截图未保存到文件。\n\n",
        "Failed to save the screenshot, so it was not written to a file.\n\n");
    public static string ClipboardErrorPrefix => T(
        "复制截图到剪贴板时失败。\n\n",
        "Failed to copy the screenshot to the clipboard.\n\n");
}
