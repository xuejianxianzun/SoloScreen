using System.Diagnostics;
using Microsoft.Win32;

namespace SoloScreen;

/// <summary>
/// 开机启动。
/// 普通权限：写入 HKCU\Software\Microsoft\Windows\CurrentVersion\Run（不弹 UAC）。
/// 管理员权限（为了热键能在任务管理器等提权窗口前台时生效）：改为创建"登录时运行 + 最高权限"的计划任务，
/// 这样登录时不会弹 UAC 确认框。
/// </summary>
internal static class AutoStartUtil
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SoloScreen";
    private const string TaskName = "SoloScreen";

    // 改名前（原产品名 Snapshot）遗留的启动项名称，启动时清理/迁移一次
    private const string LegacyValueName = "Snapshot";
    private const string LegacyTaskName = "Snapshot";

    /// <summary>
    /// 清理改名前遗留的开机启动项，避免同名旧项继续拉起已经不存在的旧 exe。
    /// 注册表值一定能删；旧计划任务需要管理员权限，删不掉就在下次以管理员身份运行时再处理。
    /// </summary>
    public static void MigrateLegacyEntries()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(LegacyValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            SimpleLog.Write("清理旧注册表启动项失败：" + ex.Message);
        }

        try
        {
            if (!TaskFileExists(LegacyTaskName)) return;

            // 旧任务还在：先按新名字重建（保持开机自启），再删掉旧的
            if (ElevationUtil.IsElevated() && !TaskExists())
                CreateLogonTask();

            RunSchTasks($"/delete /tn \"{LegacyTaskName}\" /f");
        }
        catch (Exception ex)
        {
            SimpleLog.Write("迁移旧计划任务失败：" + ex.Message);
        }
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            if (key?.GetValue(ValueName) is not null) return true;
        }
        catch { /* 忽略 */ }

        return TaskExists();
    }

    /// <summary>是否已经存在"登录时运行 + 最高权限"的计划任务（用于保持原来的启动方式，不被悄悄降级）。</summary>
    public static bool HasLogonTask() => TaskExists();

    public static void Set(bool enable, bool asAdministrator)
    {
        if (!enable)
        {
            RemoveRunValue();
            DeleteLogonTask();
            VerifyApplied(false);
            return;
        }

        if (!asAdministrator)
        {
            DeleteLogonTask();
            SetRunValue();
            VerifyApplied(true);
            return;
        }

        // 希望以"管理员权限"自启
        if (TaskExists())
        {
            // 任务已经在：提权状态下刷新一下（exe 位置可能变了）；
            // 普通权限下无法改计划任务，保持原样即可，不要反复重建（否则会弹"拒绝访问"）
            if (ElevationUtil.IsElevated()) CreateLogonTask();

            // 顺手清掉可能残留的注册表启动项，免得开机被启动两次
            RemoveRunValue();

            VerifyApplied(true);
            return;
        }

        if (ElevationUtil.IsElevated())
        {
            CreateLogonTask();
        }
        else
        {
            // 没有管理员权限就建不了计划任务，退回注册表启动项，至少保证还能自启
            SetRunValue();
        }

        VerifyApplied(true);
    }

    /// <summary>设置后核对实际状态，只有真的没生效才报错（避免无意义的警告弹窗）。</summary>
    private static void VerifyApplied(bool expectedEnabled)
    {
        if (IsEnabled() == expectedEnabled) return;

        throw new InvalidOperationException(expectedEnabled
            ? "无法注册开机启动项（可能需要管理员权限）。"
            : "无法取消开机启动：计划任务需要管理员权限才能删除，请以管理员身份运行后再试。");
    }

    private static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;

    private static void SetRunValue()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath)
            ?? throw new InvalidOperationException("无法打开注册表启动项。");
        key.SetValue(ValueName, $"\"{ExePath}\"");
    }

    private static void RemoveRunValue()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch { /* 忽略 */ }
    }

    /// <summary>
    /// 计划任务是否存在。
    /// 优先用任务定义文件判断（即使 schtasks.exe 被安全策略禁用也能判断），再退回 schtasks 查询。
    /// </summary>
    private static bool TaskExists() => TaskFileExists(TaskName);

    private static bool TaskFileExists(string name)
    {
        try
        {
            if (File.Exists(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System), "Tasks", name)))
                return true;
        }
        catch { /* 忽略 */ }

        var (code, _) = RunSchTasks($"/query /tn \"{name}\"");
        return code == 0;
    }

    private static void CreateLogonTask()
    {
        // /rl highest：以最高权限运行（不弹 UAC）；/sc onlogon：登录时触发
        var (code, output) = RunSchTasks(
            $"/create /tn \"{TaskName}\" /tr \"\\\"{ExePath}\\\"\" /sc onlogon /rl highest /f");
        if (code != 0)
            throw new InvalidOperationException("创建计划任务失败：" + output.Trim());
    }

    private static void DeleteLogonTask()
    {
        if (TaskExists()) RunSchTasks($"/delete /tn \"{TaskName}\" /f");
    }

    private static (int Code, string Output) RunSchTasks(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return (-1, "无法启动 schtasks.exe");

            string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            if (!p.WaitForExit(10000))
            {
                try { p.Kill(); } catch { /* 忽略 */ }
                return (-1, "schtasks 超时");
            }
            return (p.ExitCode, output);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
