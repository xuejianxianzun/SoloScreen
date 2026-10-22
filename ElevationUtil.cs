using System.Diagnostics;
using System.Security.Principal;

namespace SoloScreen;

/// <summary>
/// 管理员权限相关。
/// 背景（Windows 的 UIPI 机制）：任务管理器、管理员权限的命令行等"高完整性级别"窗口处于前台时，
/// 系统不会把全局热键通知投递给普通权限的进程——这是安全设计，不是本程序的 bug。
/// 想在这种窗口前台时也能截图，只能以管理员身份运行本程序。
/// </summary>
internal static class ElevationUtil
{
    /// <summary>当前进程是否以管理员权限运行。</summary>
    public static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>以管理员身份重新启动自己（会弹 UAC 确认框）。用户取消时返回 false。</summary>
    public static bool RelaunchElevated()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return false;

            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb = "runas",
                // --relaunch：等旧实例释放互斥量；--settings：重启后直接把设置窗口显示出来，
                // 让用户能看到"已经切换成管理员权限"这个结果
                Arguments = "--relaunch --settings",
                WorkingDirectory = AppContext.BaseDirectory,
            };
            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            SimpleLog.Write("以管理员身份重启失败（可能用户取消了 UAC）：" + ex.Message);
            return false;
        }
    }
}
