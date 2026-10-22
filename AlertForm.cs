using System.Windows.Forms;

namespace SoloScreen;

/// <summary>
/// 错误提示框（外观类似 MessageBox）：
/// 置顶显示，保证用户一定能看到；不阻塞主流程；同一时间只保留一个。
/// </summary>
internal sealed class AlertForm : Form
{
    private const int Pad = 16;
    private const int ContentWidth = 400;

    private static AlertForm? _current;

    public static void ShowOnce(string message)
    {
        if (_current is { IsDisposed: false })
        {
            _current.Activate();
            return;
        }

        var form = new AlertForm(message);
        _current = form;
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_current, form)) _current = null;
        };
        form.Show();
        form.Activate();
    }

    private AlertForm(string message)
    {
        Text = "SoloScreen";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true; // 后台进程弹窗抢不到前台，必须置顶才看得见
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 9.5f);

        var icon = new PictureBox
        {
            Image = SystemIcons.Warning.ToBitmap(),
            SizeMode = PictureBoxSizeMode.AutoSize,
            Location = new Point(Pad, Pad + 2),
        };

        var text = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(ContentWidth - Pad * 2 - icon.Width - 12, 0),
            Location = new Point(Pad + icon.Width + 12, Pad),
            Text = message,
        };

        var ok = new Button
        {
            Text = Strings.OK,
            AutoSize = true,
        };
        ok.Click += (_, _) => Close();

        Controls.Add(icon);
        Controls.Add(text);
        Controls.Add(ok);

        int bottom = Math.Max(text.Top + text.Height, icon.Top + icon.Height);
        int okHeight = ok.PreferredSize.Height;
        ClientSize = new Size(ContentWidth, bottom + 12 + okHeight + Pad);
        ok.Location = new Point(ContentWidth - Pad - ok.PreferredSize.Width, bottom + 12);

        AcceptButton = ok;
        CancelButton = ok;
    }
}
