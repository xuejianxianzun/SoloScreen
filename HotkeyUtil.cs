using System.Text;
using System.Windows.Forms;

namespace SoloScreen;

/// <summary>
/// 快捷键字符串的解析与格式化。
/// 存储格式（config.json）："Ctrl+Alt+S"、"Pause"、"Ctrl+Shift+PrintScreen" …
/// 显示格式："Ctrl + Alt + S"。
/// </summary>
internal static class HotkeyUtil
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;

    /// <summary>解析存储格式的快捷键串。成功时输出注册用的 modifiers（含 MOD_NOREPEAT）、虚拟键码和显示文本。</summary>
    public static bool TryParse(string? text, out uint modifiers, out uint vk, out string display)
    {
        modifiers = 0;
        vk = 0;
        display = string.Empty;

        if (string.IsNullOrWhiteSpace(text)) return false;

        uint mods = 0;
        Keys key = Keys.None;

        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var part = raw.ToLowerInvariant();
            switch (part)
            {
                case "ctrl":
                case "control":
                    mods |= ModControl;
                    continue;
                case "alt":
                    mods |= ModAlt;
                    continue;
                case "shift":
                    mods |= ModShift;
                    continue;
                case "win":
                case "windows":
                case "meta":
                    mods |= ModWin;
                    continue;
            }

            if (!TryParseKey(raw, out var k)) return false;
            if (key != Keys.None) return false; // 只允许一个非修饰键
            key = k;
        }

        if (key == Keys.None) return false;

        modifiers = mods | ModNoRepeat;
        vk = (uint)key;
        display = ToDisplay(mods, key);
        return true;
    }

    private static bool TryParseKey(string text, out Keys key)
    {
        key = Keys.None;

        // 符号别名（与 KeyName 的显示一致，方便手改 config.json）
        switch (text)
        {
            case "[": key = Keys.Oem4; return true;
            case "]": key = Keys.Oem6; return true;
            case ";": key = Keys.Oem1; return true;
            case "/": key = Keys.Oem2; return true;
            case "`": key = Keys.Oem3; return true;
            case "\\": key = Keys.Oem5; return true;
            case "'": key = Keys.Oem7; return true;
            case "-": key = Keys.OemMinus; return true;
            case "=": key = Keys.Oemplus; return true;
            case ",": key = Keys.Oemcomma; return true;
            case ".": key = Keys.OemPeriod; return true;
        }

        // 别名
        switch (text.ToLowerInvariant())
        {
            case "prtsc":
            case "prtscr":
            case "prtscn":
            case "printscreen":
                key = Keys.PrintScreen;
                return true;
        }

        // 单个数字 0-9 → D0-D9
        if (text.Length == 1 && text[0] is >= '0' and <= '9')
        {
            key = Keys.D0 + (text[0] - '0');
            return true;
        }

        if (Enum.TryParse<Keys>(text, ignoreCase: true, out var k))
        {
            // 排除修饰键本身和超过虚拟键码范围的值
            if (k is Keys.None or Keys.ControlKey or Keys.ShiftKey or Keys.Menu
                or Keys.LWin or Keys.RWin or Keys.LControlKey or Keys.RControlKey
                or Keys.LShiftKey or Keys.RShiftKey or Keys.LMenu or Keys.RMenu)
                return false;
            if ((int)k > 0xFF) return false;
            key = k;
            return true;
        }
        return false;
    }

    /// <summary>把 KeyEventArgs 的修饰键 + 键转为存储格式串。</summary>
    public static string ToCanonical(Keys modifiers, Keys key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(Keys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(Keys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(Keys.Shift)) parts.Add("Shift");
        parts.Add(KeyName(key));
        return string.Join("+", parts);
    }

    /// <summary>把存储格式串转为显示格式（"Ctrl + Alt + S"）。解析失败时原样返回。</summary>
    public static string ToDisplay(string? canonical)
    {
        if (TryParse(canonical, out _, out _, out var display)) return display;
        return canonical ?? string.Empty;
    }

    public static string ToDisplay(uint mods, Keys key)
    {
        var sb = new StringBuilder();
        if ((mods & ModControl) != 0) sb.Append("Ctrl + ");
        if ((mods & ModAlt) != 0) sb.Append("Alt + ");
        if ((mods & ModShift) != 0) sb.Append("Shift + ");
        if ((mods & ModWin) != 0) sb.Append("Win + ");
        sb.Append(KeyName(key));
        return sb.ToString();
    }

    /// <summary>按键的友好名称。</summary>
    public static string KeyName(Keys key)
    {
        if (key is >= Keys.D0 and <= Keys.D9)
            return ((int)key - (int)Keys.D0).ToString();
        return key switch
        {
            Keys.PrintScreen => "PrintScreen",
            Keys.Pause => "Pause",
            Keys.Scroll => "Scroll",
            // OEM 键显示为实际字符，避免界面出现 "Oem4" 这类难懂的名称
            Keys.Oem4 => "[",
            Keys.Oem6 => "]",
            Keys.Oem1 => ";",
            Keys.Oem2 => "/",
            Keys.Oem3 => "`",
            Keys.Oem5 => "\\",
            Keys.Oem7 => "'",
            Keys.OemMinus => "-",
            Keys.Oemplus => "=",
            Keys.Oemcomma => ",",
            Keys.OemPeriod => ".",
            _ => key.ToString(),
        };
    }
}
