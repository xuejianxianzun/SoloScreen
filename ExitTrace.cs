using System.Text.Json;
using System.Threading;
using Microsoft.Win32;

namespace SoloScreen;

/// <summary>
/// 运行痕迹与退出原因记录。
///
/// 为什么需要它：本程序是无窗口的常驻托盘进程。一旦被**强制结束**（TerminateProcess），
/// 例如被计划任务按条件终止、任务管理器"结束任务"、系统内存压力回收、安全软件拦截，
/// 进程没有任何机会执行代码——既不抛未处理异常，也不会生成 Windows 错误报告（WER），
/// 于是事件查看器和 error.log 里都只剩一条启动记录，"退出"完全无声。
///
/// 这里做两件事：
/// ① 运行期间每 60 秒把心跳时间写进 session-state.json（相当于死亡时间戳）；
/// ② 正常退出时（托盘退出 / 用户注销 / 关机 / 未处理异常）把**退出原因**写进同一文件。
/// 下次启动时若发现上一次的 ExitReason 为空，即可判定"上次是被外部强制结束的"，
/// 并把最后心跳时间写进 error.log，作为近似死亡时间。
/// </summary>
internal static class ExitTrace
{
    private const int HeartbeatSeconds = 60;

    private static readonly object Gate = new();
    private static SessionState? _state;
    private static System.Threading.Timer? _timer;
    private static bool _written;

    private sealed class SessionState
    {
        public int Pid { get; set; }
        public string StartedAt { get; set; } = "";
        public string LastHeartbeat { get; set; } = "";
        public string ExitedAt { get; set; } = "";
        /// <summary>空 = 没有记录到退出（被强制结束）；非空 = 退出原因。</summary>
        public string ExitReason { get; set; } = "";
    }

    private static string StatePath => Path.Combine(AppContext.BaseDirectory, "session-state.json");

    /// <summary>
    /// 在主窗口/消息循环之前调用一次（务必在拿到单实例互斥量之后，否则第二个实例会覆盖第一个实例的痕迹）。
    /// </summary>
    public static void Start()
    {
        SessionState? previous = LoadPrevious();

        lock (Gate)
        {
            _state = new SessionState
            {
                Pid = Environment.ProcessId,
                StartedAt = Stamp(),
                LastHeartbeat = Stamp(),
            };
            Save();
        }

        ReportPrevious(previous);
        HookCrashHandlers();

        _timer = new System.Threading.Timer(_ => Beat(), null,
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(HeartbeatSeconds));
    }

    /// <summary>记录退出原因并落盘（只第一次生效）。</summary>
    public static void Complete(string reason)
    {
        lock (Gate)
        {
            if (_state is null || _written) return;
            _written = true;

            _state.ExitReason = reason;
            _state.ExitedAt = Stamp();
            Save();

            if (_timer is not null)
            {
                try { _timer.Dispose(); } catch { /* 忽略 */ }
                _timer = null;
            }
        }

        SimpleLog.Write("SoloScreen 退出：" + reason);
    }

    private static void Beat()
    {
        try
        {
            lock (Gate)
            {
                if (_state is null || _written) return;
                _state.LastHeartbeat = Stamp();
                Save();
            }
        }
        catch
        {
            // 心跳失败不影响主流程
        }
    }

    private static void HookCrashHandlers()
    {
        // ① UI 线程未处理异常：默认会弹"未处理的异常"对话框，这里先写日志保留完整堆栈
        Application.ThreadException += (_, e) =>
            SimpleLog.Write("UI 线程未处理异常：" + e.Exception);

        // ② 后台线程未处理异常：.NET 会直接终止进程（Application Error / WER 会留下记录）
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            SimpleLog.Write($"未处理异常（IsTerminating={e.IsTerminating}）：{e.ExceptionObject}");
            if (e.IsTerminating) Complete("未处理异常导致进程终止（堆栈见上一条）");
        };

        // ③ 未观察的 Task 异常：默认静默，仅记录
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            SimpleLog.Write("未观察的 Task 异常：" + e.Exception);
            e.SetObserved();
        };

        // ④ 正常退出路径（Application.Exit → Main 返回 → 进程退出）
        Application.ApplicationExit += (_, _) => Complete("正常退出（Application.Exit）");
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            Complete("正常退出（进程结束，未走托盘退出菜单）");

        // ⑤ 注销/关机：这也是"看起来突然消失"的常见原因，单独记一笔
        SystemEvents.SessionEnding += (_, e) =>
        {
            SimpleLog.Write($"系统正在注销/关机（原因：{e.Reason}），程序随之退出。");
            Complete($"系统注销/关机（{e.Reason}）");
        };
    }

    private static void ReportPrevious(SessionState? previous)
    {
        if (previous is null) return;

        if (string.IsNullOrWhiteSpace(previous.ExitReason))
        {
            SimpleLog.Write(
                "上次运行没有留下退出记录 → 进程是被强制结束的（不是崩溃，崩溃会生成 WER 报告）。" +
                $"上次 PID={previous.Pid}，启动于 {previous.StartedAt}，最后心跳 {previous.LastHeartbeat}。" +
                "常见来源：计划任务超时或被电源/空闲条件终止、任务管理器结束任务、安全软件拦截。");
            return;
        }

        SimpleLog.Write($"上次运行正常结束（{previous.ExitedAt}，原因：{previous.ExitReason}）。");
    }

    private static SessionState? LoadPrevious()
    {
        try
        {
            if (!File.Exists(StatePath)) return null;
            return JsonSerializer.Deserialize<SessionState>(File.ReadAllText(StatePath));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>调用方必须已持有 Gate。</summary>
    private static void Save()
    {
        try
        {
            File.WriteAllText(StatePath, JsonSerializer.Serialize(_state,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 写不进去（目录只读等）就放弃，不影响主流程
        }
    }

    private static string Stamp() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
}
