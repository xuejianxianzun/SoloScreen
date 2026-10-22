using System.Runtime.InteropServices;

namespace SoloScreen;

/// <summary>
/// 隐藏消息窗口，用于 RegisterHotKey 全局快捷键。
/// 必须创建在 STA UI 线程上（主线程），WM_HOTKEY 会投递到本窗口。
/// </summary>
internal sealed class HotkeyWindow : NativeWindow
{
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 1;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>快捷键被按下时触发（在 UI 线程）。</summary>
    public event Action? HotkeyPressed;

    public HotkeyWindow()
    {
        CreateHandle(new CreateParams { Caption = "SoloScreenHotkeyWindow" });
    }

    /// <summary>注册快捷键（先注销旧的）。成功返回 true。</summary>
    public bool Register(uint modifiers, uint vk)
    {
        Unregister();
        var ok = RegisterHotKey(Handle, HotkeyId, modifiers, vk);
        if (!ok)
        {
            int err = Marshal.GetLastWin32Error();
            SimpleLog.Write($"RegisterHotKey 失败: mods=0x{modifiers:X} vk=0x{vk:X} err={err} handle={Handle}");
        }
        return ok;
    }

    public void Unregister()
    {
        try { UnregisterHotKey(Handle, HotkeyId); }
        catch { /* 句柄已销毁时忽略 */ }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt64() == HotkeyId)
        {
            HotkeyPressed?.Invoke();
        }
        base.WndProc(ref m);
    }
}
