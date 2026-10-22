using System.Reflection;

namespace SoloScreen;

/// <summary>
/// 程序版本信息。
/// 版本号的定义位置是 csproj 的 &lt;Version&gt; 属性（单一来源），编译时固化进程序集元数据，
/// 这里在运行时读取，供界面标题等地方显示。改版本号只需改 csproj 那一处。
/// </summary>
internal static class AppInfo
{
    /// <summary>形如 "1.0.0" 的版本号（取 Major.Minor.Build 三段）。</summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v is null ? "1.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
