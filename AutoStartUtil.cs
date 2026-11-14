using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;
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

    /// <summary>
    /// 用完整 XML 注册"登录时运行 + 最高权限"的计划任务。
    ///
    /// 必须走 XML，不能用 <c>schtasks /create /sc onlogon</c> 的简化参数：那样生成的任务会带上
    /// 几个对常驻托盘程序致命的默认值——
    ///   ExecutionTimeLimit=PT72H      运行满 72 小时由任务计划程序直接终止进程
    ///   DisallowStartIfOnBatteries   插着电源才启动
    ///   StopIfGoingOnBatteries       一拔电源就终止进程
    ///   StopOnIdleEnd                机器退出空闲状态时终止
    /// 被任务计划程序终止走的是 TerminateProcess，进程没有任何机会写日志，也不产生 WER 报告，
    /// 表现就是"托盘图标突然没了，error.log 里只有启动记录"。
    /// </summary>
    private static void CreateLogonTask()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"SoloScreen-task-{Environment.ProcessId}.xml");
        try
        {
            // schtasks /xml 要求 UTF-16 编码的任务定义文件
            File.WriteAllText(temp, BuildLogonTaskXml(), new UnicodeEncoding(false, true));

            var (code, output) = RunSchTasks($"/create /tn \"{TaskName}\" /xml \"{temp}\" /f");
            if (code != 0)
                throw new InvalidOperationException("创建计划任务失败：" + output.Trim());
        }
        finally
        {
            try { File.Delete(temp); } catch { /* 忽略 */ }
        }
    }

    /// <summary>
    /// 启动时自检现存计划任务的关键设置，发现是旧版遗留的"会杀进程"配置就重建一次。
    ///
    /// 只有管理员权限才能读写 C:\Windows\System32\Tasks 下的任务定义，所以非管理员启动时直接跳过——
    /// 那种情况下自启动本来就该走 HKCU\Run（没有时限问题）。任务确实存在且是管理员启动时，
    /// 下一次登录由任务拉起（带最高权限）就会自动修复，用户无需手动操作。
    /// </summary>
    public static void EnsureTaskHealthy()
    {
        try
        {
            if (!ElevationUtil.IsElevated()) return;

            var path = TaskFilePath(TaskName);
            if (path is null || !File.Exists(path)) return;

            var (healthy, problem) = InspectTaskFile(path);
            if (healthy) return;

            SimpleLog.Write($"计划任务设置不安全（{problem}），正在按新设置重建…");
            CreateLogonTask();
            SimpleLog.Write("计划任务已重建：无运行时长限制、不受电源/空闲状态影响。");
        }
        catch (Exception ex)
        {
            SimpleLog.Write("自检计划任务失败：" + ex.Message);
        }
    }

    /// <summary>检查任务定义里的几个致命字段。返回（是否健康，问题描述）。</summary>
    private static (bool Healthy, string Problem) InspectTaskFile(string path)
    {
        XDocument doc;
        using (var stream = File.OpenRead(path))
        {
            doc = XDocument.Load(stream);
        }

        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var settings = doc.Root?.Element(ns + "Settings");
        if (settings is null) return (false, "缺少 Settings 节点");

        // ExecutionTimeLimit：缺省即 72 小时（PT72H），只接受 PT0S（无限制）
        var limit = settings.Element(ns + "ExecutionTimeLimit")?.Value.Trim();
        if (!string.Equals(limit, "PT0S", StringComparison.OrdinalIgnoreCase))
            return (false, "运行时长限制=" + (string.IsNullOrEmpty(limit) ? "PT72H（默认值）" : limit));

        // 这两个的默认值都是 true
        if (!string.Equals(settings.Element(ns + "StopIfGoingOnBatteries")?.Value.Trim(), "false",
                StringComparison.OrdinalIgnoreCase))
            return (false, "使用电池时会终止任务");

        if (!string.Equals(settings.Element(ns + "DisallowStartIfOnBatteries")?.Value.Trim(), "false",
                StringComparison.OrdinalIgnoreCase))
            return (false, "使用电池时不启动");

        if (string.Equals(settings.Element(ns + "IdleSettings")?.Element(ns + "StopOnIdleEnd")?.Value.Trim(),
                "true", StringComparison.OrdinalIgnoreCase))
            return (false, "退出空闲状态时会终止任务");

        return (true, "");
    }

    /// <summary>生成任务定义 XML。设置项全写全，不留任何依赖系统默认值的字段。</summary>
    private static string BuildLogonTaskXml()
    {
        string sid = CurrentUserSid();
        string exe = ExePath;

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-16", null),
            new XElement(Xmlns + "Task",
                new XAttribute("version", "1.2"),
                new XElement(Xmlns + "RegistrationInfo",
                    new XElement(Xmlns + "Description", "SoloScreen — 登录时自动启动（常驻托盘，不限运行时长）"),
                    new XElement(Xmlns + "URI", "\\" + TaskName)),
                new XElement(Xmlns + "Principals",
                    new XElement(Xmlns + "Principal",
                        new XAttribute("id", "Author"),
                        new XElement(Xmlns + "UserId", sid),
                        // InteractiveToken：在当前登录会话里运行（托盘程序必须有桌面会话）
                        new XElement(Xmlns + "LogonType", "InteractiveToken"),
                        // HighestAvailable：不弹 UAC 就能拿到管理员权限（满足 UIPI 下的热键需求）
                        new XElement(Xmlns + "RunLevel", "HighestAvailable"))),
                new XElement(Xmlns + "Settings",
                    // 注意：任务定义 schema 对 Settings 子元素的顺序敏感，这里严格按官方文档顺序书写
                    new XElement(Xmlns + "AllowStartOnDemand", "true"),
                    // 进程异常结束（非 0 退出码）时最多自动拉起 3 次，避免"崩了就再也不回来"
                    new XElement(Xmlns + "RestartOnFailure",
                        new XElement(Xmlns + "Interval", "PT1M"),
                        new XElement(Xmlns + "Count", "3")),
                    new XElement(Xmlns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(Xmlns + "DisallowStartIfOnBatteries", "false"),
                    new XElement(Xmlns + "StopIfGoingOnBatteries", "false"),
                    new XElement(Xmlns + "AllowHardTerminate", "true"),
                    new XElement(Xmlns + "StartWhenAvailable", "true"),
                    new XElement(Xmlns + "RunOnlyIfNetworkAvailable", "false"),
                    new XElement(Xmlns + "WakeToRun", "false"),
                    // 关键：PT0S = 不限运行时长（缺省是 PT72H，到点会被任务计划程序直接终止）
                    new XElement(Xmlns + "ExecutionTimeLimit", "PT0S"),
                    new XElement(Xmlns + "Priority", "7"),
                    new XElement(Xmlns + "RunOnlyIfIdle", "false"),
                    new XElement(Xmlns + "IdleSettings",
                        new XElement(Xmlns + "StopOnIdleEnd", "false"),
                        new XElement(Xmlns + "RestartOnIdle", "false")),
                    new XElement(Xmlns + "Hidden", "false"),
                    new XElement(Xmlns + "Enabled", "true")),
                new XElement(Xmlns + "Triggers",
                    new XElement(Xmlns + "LogonTrigger",
                        new XElement(Xmlns + "Enabled", "true"),
                        new XElement(Xmlns + "UserId", sid))),
                new XElement(Xmlns + "Actions",
                    new XAttribute("Context", "Author"),
                    new XElement(Xmlns + "Exec",
                        new XElement(Xmlns + "Command", $"\"{exe}\"")))));

        return doc.Declaration + Environment.NewLine + doc.ToString();
    }

    private static readonly XNamespace Xmlns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    /// <summary>任务定义文件路径（仅在管理员权限下可读）。</summary>
    private static string? TaskFilePath(string name)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Tasks");
            return Path.Combine(dir, name);
        }
        catch
        {
            return null;
        }
    }

    private static string CurrentUserSid()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User?.Value ?? "";
        }
        catch
        {
            return "";
        }
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
