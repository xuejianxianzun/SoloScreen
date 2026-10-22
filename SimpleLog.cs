namespace SoloScreen;

/// <summary>极简错误日志，追加写入 exe 同级 error.log。任何异常都不向上抛。</summary>
internal static class SimpleLog
{
    /// <summary>日志文件超过此大小就清空，避免常驻托盘程序长期运行日志无限增长。</summary>
    private const long MaxLogBytes = 512 * 1024;

    public static void Write(string message)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "error.log");

            // 超限先清空（清理失败也继续追加，日志尽力而为）
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length > MaxLogBytes)
                    File.WriteAllText(path, string.Empty);
            }
            catch { /* 忽略 */ }

            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}";
            File.AppendAllText(path, line);
        }
        catch
        {
            // 日志失败时静默，绝不影响主流程
        }
    }
}
