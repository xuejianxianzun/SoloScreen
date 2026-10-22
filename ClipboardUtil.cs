using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SoloScreen;

/// <summary>
/// 剪贴板写入（带重试：剪贴板可能被其他程序短暂占用）。
///
/// 这里直接用 Win32 剪贴板 API 写 **CF_DIB**（24bpp）和 **PNG** 两种格式，原因：
/// 若把 .NET Bitmap 交给剪贴板（Clipboard.SetImage / DataObject 装 Image），
/// 那份 15MB 的 GDI+ 像素内存会被长期持有且不受 GC 回收，导致每截一张就涨一张图大小的内存
/// （实测 2560×1440 每张 +14MB，几十张就几百 MB）。
/// 现在 DIB 直接写进系统全局内存，进程内不产生大对象，内存不随截图次数增长。
/// </summary>
internal static class ClipboardUtil
{
    private const uint CF_DIB = 8;
    private const uint GMEM_MOVEABLE = 0x0002;
    private const int BitmapInfoHeaderSize = 40;

    /// <summary>PNG 剪贴板格式 ID。注册一次即可，系统运行期间同名字返回同一 ID。</summary>
    private static readonly uint PngFormat = RegisterClipboardFormat("PNG");

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterClipboardFormat(string lpszFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    public static void SetImage(Bitmap source)
    {
        for (int attempt = 0; ; attempt++)
        {
            if (TrySetClipboard(source)) return;

            if (attempt >= 10)
                throw new ExternalException("打开剪贴板失败（可能被其他程序占用）。");
            Thread.Sleep(40);
        }
    }

    private static bool TrySetClipboard(Bitmap source)
    {
        if (!OpenClipboard(IntPtr.Zero)) return false;

        try
        {
            if (!EmptyClipboard()) return false;

            // ① CF_DIB：几乎所有 Windows 程序都能粘贴
            IntPtr dib = CreateDibHandle(source);
            if (dib == IntPtr.Zero) return false;
            if (SetClipboardData(CF_DIB, dib) == IntPtr.Zero)
            {
                GlobalFree(dib);
                return false;
            }
            // 成功后所有权归系统剪贴板，不能再释放

            // ② PNG：浏览器、聊天工具更偏好这个。
            // PNG 编码失败不应掩盖"CF_DIB 已经成功写入"这一结果，单独捕获，仅记录日志。
            try
            {
                if (PngFormat != 0)
                {
                    IntPtr png = CreatePngHandle(source);
                    if (png != IntPtr.Zero && SetClipboardData(PngFormat, png) == IntPtr.Zero)
                        GlobalFree(png);
                }
            }
            catch (Exception ex)
            {
                SimpleLog.Write("写入 PNG 到剪贴板失败（CF_DIB 已成功写入）：" + ex.Message);
            }

            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>把位图写成 24bpp 的 DIB（BITMAPINFOHEADER + 自底向上的像素行）到全局内存。</summary>
    private static IntPtr CreateDibHandle(Bitmap source)
    {
        int width = source.Width;
        int height = source.Height;
        int dstStride = (width * 3 + 3) & ~3;
        int pixelBytes = dstStride * height;

        IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)(BitmapInfoHeaderSize + pixelBytes));
        if (hMem == IntPtr.Zero) return IntPtr.Zero;

        IntPtr target = GlobalLock(hMem);
        if (target == IntPtr.Zero)
        {
            GlobalFree(hMem);
            return IntPtr.Zero;
        }

        bool success = false;
        try
        {
            var header = new byte[BitmapInfoHeaderSize];
            WriteInt(header, 0, BitmapInfoHeaderSize);
            WriteInt(header, 4, width);
            WriteInt(header, 8, height);        // 正数 = 自底向上
            WriteShort(header, 12, 1);          // biPlanes
            WriteShort(header, 14, 24);         // biBitCount
            WriteInt(header, 16, 0);            // BI_RGB
            WriteInt(header, 20, pixelBytes);   // biSizeImage
            Marshal.Copy(header, 0, target, BitmapInfoHeaderSize);

            var bits = source.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var srcRow = new byte[width * 4];
                var dstRow = new byte[dstStride];

                for (int y = 0; y < height; y++)
                {
                    Marshal.Copy(bits.Scan0 + y * bits.Stride, srcRow, 0, width * 4);

                    // BGRA -> BGR
                    for (int x = 0, s = 0, d = 0; x < width; x++, s += 4, d += 3)
                    {
                        dstRow[d] = srcRow[s];
                        dstRow[d + 1] = srcRow[s + 1];
                        dstRow[d + 2] = srcRow[s + 2];
                    }

                    Marshal.Copy(dstRow, 0, target + BitmapInfoHeaderSize + (height - 1 - y) * dstStride, dstStride);
                }
            }
            finally
            {
                source.UnlockBits(bits);
            }

            success = true;
            return hMem;
        }
        finally
        {
            GlobalUnlock(hMem);
            if (!success) GlobalFree(hMem);
        }
    }

    private static IntPtr CreatePngHandle(Bitmap source)
    {
        byte[] png;
        using (var ms = new MemoryStream())
        {
            source.Save(ms, ImageFormat.Png);
            png = ms.ToArray();
        }

        IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)png.Length);
        if (hMem == IntPtr.Zero) return IntPtr.Zero;

        IntPtr target = GlobalLock(hMem);
        if (target == IntPtr.Zero)
        {
            GlobalFree(hMem);
            return IntPtr.Zero;
        }

        Marshal.Copy(png, 0, target, png.Length);
        GlobalUnlock(hMem);
        return hMem;
    }

    private static void WriteInt(byte[] buffer, int offset, int value)
        => BitConverter.TryWriteBytes(buffer.AsSpan(offset, 4), value);

    private static void WriteShort(byte[] buffer, int offset, short value)
        => BitConverter.TryWriteBytes(buffer.AsSpan(offset, 2), value);
}
