using System.Windows.Forms;

namespace SoloScreen;

/// <summary>快捷键捕获对话框：直接按下按键组合即可录入，Esc 取消。</summary>
internal sealed class HotkeyCaptureForm : Form
{
    /// <summary>用户按下的快捷键（存储格式，如 "Ctrl+Alt+S"），取消时为 null。</summary>
    public string? Result { get; private set; }

    private readonly Label _display = new()
    {
        TextAlign = ContentAlignment.MiddleCenter,
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.FixedSingle,
        AutoEllipsis = true, // 组合键过长时显示省略号，不会硬切开
        Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 13f, FontStyle.Bold),
        Text = "…",
    };

    public HotkeyCaptureForm()
    {
        Text = Strings.SetHotkeyTitle;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(460, 190);
        KeyPreview = true;

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(12),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        // 提示文字：限制最大宽度，超出自动换行（否则会伸出窗口外看不全）
        var hint = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(430, 0),
            Text = Strings.HotkeyHint,
        };

        table.Controls.Add(hint, 0, 0);
        table.Controls.Add(_display, 0, 1);
        Controls.Add(table);

        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;

        // Esc 单独按下 = 取消
        if (e.KeyCode == Keys.Escape && e.Modifiers == Keys.None)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        // 只按了修饰键：显示等待状态
        if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
        {
            var pending = new List<string>();
            if (e.Modifiers.HasFlag(Keys.Control)) pending.Add("Ctrl");
            if (e.Modifiers.HasFlag(Keys.Alt)) pending.Add("Alt");
            if (e.Modifiers.HasFlag(Keys.Shift)) pending.Add("Shift");
            _display.Text = string.Join(" + ", pending) + " + …";
            return;
        }

        Result = HotkeyUtil.ToCanonical(e.Modifiers, e.KeyCode);
        DialogResult = DialogResult.OK;
        Close();
    }
}
