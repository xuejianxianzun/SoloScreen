using System.Text.Json;

namespace SoloScreen;

/// <summary>应用配置。保存在 exe 同级的 config.json，启动时读取。</summary>
internal sealed class AppConfig
{
    /// <summary>截取哪个屏幕（Screen.DeviceName，空字符串 = 主显示器）。</summary>
    public string ScreenDevice { get; set; } = "";

    /// <summary>截图快捷键，如 "Pause"、"Ctrl+Alt+S"。</summary>
    public string Hotkey { get; set; } = "Pause";

    /// <summary>自动保存目录，空 = 默认（图片\Screenshots，即 Windows 系统截图目录）。</summary>
    public string SaveDirectory { get; set; } = "";

    /// <summary>截图时是否保存到文件。</summary>
    public bool SaveFile { get; set; } = true;

    /// <summary>截图时是否复制到剪贴板。</summary>
    public bool CopyToClipboard { get; set; } = true;

    /// <summary>截图后是否播放音效（外置 audio.wav / audio.mp3 优先，否则用内置音效）。</summary>
    public bool PlaySound { get; set; } = true;

    /// <summary>截图后是否显示系统通知（默认关闭）。</summary>
    public bool ShowNotification { get; set; }

    /// <summary>是否开机启动（注册表/计划任务的实际状态为准，这里只做记录）。</summary>
    public bool AutoStart { get; set; }

    /// <summary>启动时是否不显示设置界面（勾选则直接静默到托盘）。</summary>
    public bool StartMinimized { get; set; }

    /// <summary>文件名格式模板。</summary>
    public string FileNamePattern { get; set; } = "$YYYY-$MM-$DD-$hh-$mm-$ss-$fff";

    /// <summary>界面语言："en" = 英语，"zh-Hans" = 简体中文，空字符串 = 跟随系统语言。</summary>
    public string Language { get; set; } = "";

    public static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath));
                if (cfg is not null) return cfg;
            }
        }
        catch (Exception ex)
        {
            SimpleLog.Write("读取配置失败，使用默认配置：" + ex.Message);
        }
        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, options));
        }
        catch (Exception ex)
        {
            SimpleLog.Write("保存配置失败：" + ex.Message);
        }
    }

    /// <summary>返回实际生效的保存目录。</summary>
    public string GetSaveDirectory()
    {
        if (!string.IsNullOrWhiteSpace(SaveDirectory)) return SaveDirectory;
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "Screenshots");
    }
}
