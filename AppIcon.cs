using System.Reflection;

namespace SoloScreen;

/// <summary>
/// 程序图标：直接读取嵌入的 app.ico（多尺寸 16/20/24/32/40/48/256）。
/// 这样窗口标题栏、托盘、任务栏、资源管理器、任务管理器里显示的图标完全一致，
/// 且由系统按场景挑最合适的尺寸，不必运行时再画一遍。
/// </summary>
internal static class AppIcon
{
    private static Icon? _icon;
    private static MemoryStream? _stream; // Icon 使用期间需要流保持有效

    public static Icon Get()
    {
        if (_icon is not null) return _icon;

        try
        {
            using var res = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("SoloScreen.app.ico");
            if (res is not null)
            {
                _stream = new MemoryStream();
                res.CopyTo(_stream);
                _stream.Position = 0;
                _icon = new Icon(_stream);
                return _icon;
            }
        }
        catch (Exception ex)
        {
            SimpleLog.Write("加载图标资源失败，改用系统默认图标：" + ex.Message);
        }

        _icon = (Icon)SystemIcons.Application.Clone();
        return _icon;
    }
}
