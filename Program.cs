namespace SoloScreen;

internal static class Program
{
    private const string MutexName = @"Local\SoloScreen_SingleInstance";

    /// <summary>程序入口。启动后不显示任何窗口，仅常驻系统托盘。</summary>
    [STAThread]
    private static void Main(string[] args)
    {
        // 先读配置决定界面语言（单实例提示也要用）
        Strings.Initialize(AppConfig.Load().Language);

        // 单实例。以 --relaunch 重启（如切换管理员权限）时旧实例可能还没退干净，需短暂等待
        var mutex = AcquireSingleInstance(args.Contains("--relaunch", StringComparer.OrdinalIgnoreCase));
        if (mutex is null)
        {
            MessageBox.Show(Strings.AlreadyRunning, "SoloScreen",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            ApplicationConfiguration.Initialize();
            var app = new TrayApp();

            // --settings：提权重启后强制显示界面，方便用户确认权限已切换（优先于"启动时不显示界面"）
            // 其他情况：默认显示设置界面；只有勾选了"启动时不显示界面"才静默到托盘
            bool showSettings = args.Contains("--settings", StringComparer.OrdinalIgnoreCase)
                                || !app.StartMinimized;

            if (showSettings)
            {
                // 等消息循环跑起来再显示窗口，否则窗口无法正常激活到前台（会躲到其他窗口后面）
                var timer = new System.Windows.Forms.Timer { Interval = 120 };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    timer.Dispose();
                    app.ShowSettings();
                };
                timer.Start();
            }

            Application.Run(app);
        }
        finally
        {
            try { mutex.ReleaseMutex(); } catch { /* 忽略 */ }
            mutex.Dispose();
        }
    }

    private static Mutex? AcquireSingleInstance(bool waitForPrevious)
    {
        for (int attempt = 0; ; attempt++)
        {
            var mutex = new Mutex(true, MutexName, out bool createdNew);
            if (createdNew) return mutex;

            mutex.Dispose();
            if (!waitForPrevious || attempt >= 40) return null; // 最多等 4 秒
            Thread.Sleep(100);
        }
    }
}
