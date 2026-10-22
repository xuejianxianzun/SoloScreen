using System.Drawing.Imaging;
using System.Windows.Forms;

namespace SoloScreen;

/// <summary>核心截图流程：截屏 →（可选）保存 PNG →（可选）复制剪贴板 →（可选）播放音效。</summary>
internal static class CaptureService
{
    /// <summary>
    /// 截图结果。
    /// Saved/Copied 表示对应步骤是否成功；SaveError/ClipboardError 非空表示该步骤失败（互不影响）。
    /// </summary>
    public sealed record CaptureResult(
        bool Saved,
        string? FileName,
        string? Directory,
        bool Copied,
        string? SaveError,
        string? ClipboardError);

    public static CaptureResult CaptureAndSave(AppConfig cfg)
    {
        var screen = ResolveScreen(cfg.ScreenDevice);
        var bounds = screen.Bounds;

        // 捕获整屏（DPI 感知下 Bounds 即物理像素）
        using var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        }

        // ① 保存到文件（可选）
        bool saved = false;
        string? fileName = null;
        string? directory = null;
        string? saveError = null;

        if (cfg.SaveFile)
        {
            var targetDir = cfg.GetSaveDirectory();
            bool dirReady = false;
            try
            {
                Directory.CreateDirectory(targetDir);
                dirReady = true;
                directory = targetDir;
            }
            catch (Exception ex)
            {
                saveError = Strings.CreateDirError;
                SimpleLog.Write($"创建截图目录失败 {targetDir}：{ex.Message}");
            }

            if (dirReady)
            {
                try
                {
                    fileName = FileNameBuilder.Build(cfg.FileNamePattern) + ".png";
                    bmp.Save(Path.Combine(targetDir, fileName), ImageFormat.Png);
                    saved = true;
                }
                catch (Exception ex)
                {
                    fileName = null;
                    saveError = Strings.SaveFileErrorPrefix + ex.Message;
                    SimpleLog.Write($"保存截图文件失败：{ex}");
                }
            }
        }

        // ② 复制到剪贴板（可选；即使保存失败也照常执行）
        bool copied = false;
        string? clipboardError = null;
        if (cfg.CopyToClipboard)
        {
            try
            {
                ClipboardUtil.SetImage(bmp);
                copied = true;
            }
            catch (Exception ex)
            {
                clipboardError = Strings.ClipboardErrorPrefix + ex.Message;
                SimpleLog.Write($"复制到剪贴板失败：{ex}");
            }
        }

        // ③ 音效（可选）
        if (cfg.PlaySound)
        {
            SoundUtil.PlayProgramDirAudio();
        }

        return new CaptureResult(saved, fileName, directory, copied, saveError, clipboardError);
    }

    private static Screen ResolveScreen(string? deviceName)
    {
        foreach (var s in Screen.AllScreens)
        {
            if (s.DeviceName == deviceName) return s;
        }
        // 设备不存在（如拔掉了显示器）时回退到主屏
        return Screen.PrimaryScreen ?? Screen.AllScreens[0];
    }
}
