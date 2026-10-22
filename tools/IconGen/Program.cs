using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;

// 图标工具：
//   无参数          → 生成 6 个候选预览 PNG + 对比图
//   --ico <输出路径> → 用 B 方案生成正式多尺寸 .ico（直接给 exe 用）
//   --extract <exe> <输出png> → 从 exe 提取图标，验证嵌入结果
internal static class Program
{
    private const int S = 256;          // 设计基准画布
    private static readonly string OutDir = @"C:\Users\12477\.workbuddy\icon-preview\out";

    private static readonly Color BodyDark1 = Color.FromArgb(58, 61, 82);
    private static readonly Color BodyDark2 = Color.FromArgb(30, 32, 44);
    private static readonly Color AccentDeep = Color.FromArgb(0, 96, 200);

    private static void Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--ico")
        {
            MakeIco(args[1]);
            return;
        }

        if (args.Length >= 2 && args[0] == "--extract")
        {
            Extract(args[1], args[2]);
            return;
        }

        if (args.Length >= 2 && args[0] == "--small")
        {
            MakeSmallSheet(args[1]);
            return;
        }

        if (args.Length >= 2 && args[0] == "--diag")
        {
            foreach (var s in args.Skip(1).Select(int.Parse))
                DumpAscii(s);
            return;
        }

        MakePreviews();
    }

    // ---------- 正式图标（B 方案） ----------

    private static void MakeIco(string path)
    {
        int[] sizes = { 16, 20, 24, 32, 40, 48, 256 };
        var images = new List<(int size, byte[] dib, byte[] png)>();

        foreach (int size in sizes)
        {
            using var bmp = RenderB(size);
            images.Add((size, ToDib(bmp), ToPng(bmp)));
        }

        WriteIco(path, images);
        Console.WriteLine($"已生成 {path}（{new FileInfo(path).Length / 1024} KB，尺寸 {string.Join('/', sizes)}）");
    }

    /// <summary>
    /// B 方案：显示器 + 屏幕四角取景框（所有尺寸都画，不做简化）。
    /// 小尺寸下线条按比例缩放后不足 1px 会糊成噪点，因此分两种画法：
    /// ・≥48px：按设计稿比例（9/256）用抗锯齿绘制；
    /// ・≤32px：在像素网格上画 1px 的 L 形（关闭抗锯齿、坐标取整），保证四角清晰可辨。
    /// </summary>
    private static Bitmap RenderB(int size)
    {
        // 机身与屏幕（带超采样）
        var bmp = Render(g =>
        {
            MonitorBody(g, new RectangleF(20, 40, 216, 156), new RectangleF(36, 56, 184, 124), 14);
            Stand(g);
        }, size);

        if (size < 48)
        {
            DrawCrispCorners(bmp, size);
            return bmp;
        }

        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(size / (float)S, size / (float)S);
            using var pen = new Pen(Color.White, 9) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            const float inset = 16, len = 30;
            float l = 36 + inset, t = 56 + inset, r = 220 - inset, b = 180 - inset;
            g.DrawLines(pen, new[] { new PointF(l, t + len), new PointF(l, t), new PointF(l + len, t) });
            g.DrawLines(pen, new[] { new PointF(r - len, t), new PointF(r, t), new PointF(r, t + len) });
            g.DrawLines(pen, new[] { new PointF(l, b - len), new PointF(l, b), new PointF(l + len, b) });
            g.DrawLines(pen, new[] { new PointF(r - len, b), new PointF(r, b), new PointF(r, b - len) });
        }

        return bmp;
    }

    /// <summary>小尺寸专用：像素对齐的 1px 取景框角标。</summary>
    private static void DrawCrispCorners(Bitmap bmp, int size)
    {
        // 机身是抗锯齿渲染的，设计坐标换算出来的位置和"实际画出来的屏幕"差半像素到 1 像素，
        // 所以直接测量渲染结果里蓝色屏幕的实际范围，再在以它为中心的对称位置放角标。
        var box = FindScreenBox(bmp);
        if (box.Width < 6 || box.Height < 4) return;

        const float inset = 1.7f;
        // 注意：Rectangle.Right/Bottom 是开区间（= x + width），像素中心要用 -1 后的闭区间
        float cx = (box.Left + box.Right - 1) / 2f;
        float cy = (box.Top + box.Bottom - 1) / 2f;
        float halfW = box.Width / 2f - inset;
        float halfH = box.Height / 2f - inset;

        int left = (int)Math.Round(cx - halfW);
        int right = (int)Math.Round(cx + halfW);
        int top = (int)Math.Round(cy - halfH);
        int bottom = (int)Math.Round(cy + halfH);

        int arm = size >= 32 ? 4 : size >= 24 ? 3 : 2;

        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.None;
        g.PixelOffsetMode = PixelOffsetMode.Default; // 整数坐标必须落在整数像素上，否则整体偏移 1px
        using var pen = new Pen(Color.White, 1) { StartCap = LineCap.Flat, EndCap = LineCap.Flat };

        // 左上
        g.DrawLine(pen, left, top, left + arm - 1, top);
        g.DrawLine(pen, left, top, left, top + arm - 1);
        // 右上
        g.DrawLine(pen, right - arm + 1, top, right, top);
        g.DrawLine(pen, right, top, right, top + arm - 1);
        // 左下
        g.DrawLine(pen, left, bottom - arm + 1, left, bottom);
        g.DrawLine(pen, left, bottom, left + arm - 1, bottom);
        // 右下
        g.DrawLine(pen, right, bottom - arm + 1, right, bottom);
        g.DrawLine(pen, right - arm + 1, bottom, right, bottom);
    }

    /// <summary>测量渲染结果里蓝色屏幕的实际像素范围。</summary>
    private static Rectangle FindScreenBox(Bitmap bmp)
    {
        int minX = bmp.Width, minY = bmp.Height, maxX = -1, maxY = -1;

        for (int y = 0; y < bmp.Height; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                var c = bmp.GetPixel(x, y);
                if (c.A > 60 && c.B > 110 && c.B > c.R + 25)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        return maxX < 0 ? Rectangle.Empty : new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    /// <summary>把指定尺寸的图标逐像素打印成字符画，用来检查角标是否居中。</summary>
    private static void DumpAscii(int size)
    {
        using var bmp = RenderB(size);
        Console.WriteLine($"===== {size}px =====");
        int minX = size, maxX = -1, minY = size, maxY = -1;

        for (int y = 0; y < size; y++)
        {
            var sb = new System.Text.StringBuilder();
            for (int x = 0; x < size; x++)
            {
                var c = bmp.GetPixel(x, y);
                char ch;
                if (c.A < 40) ch = ' ';
                else if (c.R > 200 && c.G > 200 && c.B > 200)
                {
                    ch = '#';                                   // 白色取景框
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
                else if (c.B > 120 && c.B > c.R + 30) ch = '+';  // 屏幕（蓝）
                else ch = '.';                                   // 机身
                sb.Append(ch);
            }
            Console.WriteLine(sb.ToString());
        }

        if (maxX >= 0)
        {
            var screen = FindScreenBox(bmp);
            Console.WriteLine($"白色像素包围盒: x {minX}..{maxX}（画布 0..{size - 1}） y {minY}..{maxY}");
            Console.WriteLine($"屏幕范围: x {screen.Left}..{screen.Right - 1}  y {screen.Top}..{screen.Bottom - 1}");
            Console.WriteLine($"四周留白: 左 {minX - screen.Left}  右 {screen.Right - 1 - maxX}  " +
                              $"上 {minY - screen.Top}  下 {screen.Bottom - 1 - maxY}");
        }
    }

    /// <summary>把小尺寸放大展示，便于肉眼检查 16/20/24/32 的效果。</summary>
    private static void MakeSmallSheet(string path)
    {
        int[] sizes = { 16, 20, 24, 32, 48, 256 };
        int cell = 300, zoom = 260;
        using var sheet = new Bitmap(cell * sizes.Length, cell, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.FromArgb(246, 247, 250));
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        using var font = new Font("Segoe UI", 11, FontStyle.Bold);
        using var brush = new SolidBrush(Color.FromArgb(60, 64, 80));

        for (int i = 0; i < sizes.Length; i++)
        {
            using var bmp = RenderB(sizes[i]);
            int z = sizes[i] == 256 ? zoom : zoom;
            g.DrawImage(bmp, i * cell + (cell - z) / 2, 30, z, z);
            g.DrawString($"{sizes[i]} px" + (sizes[i] < 256 ? $"  （放大 {z / sizes[i]}x）" : ""),
                font, brush, i * cell + 20, 6);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        sheet.Save(path, ImageFormat.Png);
        Console.WriteLine("小尺寸检查图: " + path);
    }

    private static Bitmap Render(Action<Graphics> draw, int size)
    {
        const int ss = 4; // 超采样，小尺寸边缘更干净
        using var big = new Bitmap(size * ss, size * ss, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(big))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            g.ScaleTransform(size * ss / (float)S, size * ss / (float)S);
            draw(g);
        }

        var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g2 = Graphics.FromImage(result))
        {
            g2.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g2.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g2.DrawImage(big, new Rectangle(0, 0, size, size));
        }
        return result;
    }

    // ---------- 公共绘制部件 ----------

    private static void MonitorBody(Graphics g, RectangleF body, RectangleF screen, float radius)
    {
        using var bodyBrush = new LinearGradientBrush(
            new RectangleF(body.X, body.Y, body.Width, body.Height),
            BodyDark1, BodyDark2, LinearGradientMode.Vertical);
        g.FillPath(bodyBrush, Round(body, radius));

        using var scr = new LinearGradientBrush(
            new RectangleF(screen.X, screen.Y, screen.Width, screen.Height),
            Color.FromArgb(60, 200, 255), AccentDeep, LinearGradientMode.ForwardDiagonal);
        g.FillPath(scr, Round(screen, Math.Max(2, radius * 0.5f)));

        using var gloss = new SolidBrush(Color.FromArgb(70, 255, 255, 255));
        g.FillPolygon(gloss, new[]
        {
            new PointF(screen.X, screen.Y),
            new PointF(screen.X + screen.Width * 0.62f, screen.Y),
            new PointF(screen.X, screen.Y + screen.Height * 0.72f),
        });
    }

    private static void Stand(Graphics g)
    {
        using var brush = new SolidBrush(BodyDark2);
        g.FillRectangle(brush, 116, 196, 24, 18);
        g.FillPath(brush, Round(new RectangleF(88, 214, 80, 16), 8));
    }

    private static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        if (d > r.Width) d = r.Width;
        if (d > r.Height) d = r.Height;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // ---------- ICO 写入 ----------

    private static byte[] ToPng(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static byte[] ToDib(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        int xorSize = w * h * 4;
        int maskStride = ((w + 31) / 32) * 4;
        int maskSize = maskStride * h;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(40); bw.Write(w); bw.Write(h * 2);
        bw.Write((short)1); bw.Write((short)32);
        bw.Write(0); bw.Write(xorSize);
        bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);

        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[w * 4];
            for (int y = h - 1; y >= 0; y--)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, w * 4);
                bw.Write(row);
            }
        }
        finally { bmp.UnlockBits(data); }

        bw.Write(new byte[maskSize]);
        bw.Flush();
        return ms.ToArray();
    }

    private static void WriteIco(string path, List<(int size, byte[] dib, byte[] png)> images)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        bw.Write((short)0);
        bw.Write((short)1);
        bw.Write((short)images.Count);

        int offset = 6 + 16 * images.Count;
        foreach (var (size, dib, png) in images)
        {
            var payload = size >= 256 ? png : dib;
            bw.Write((byte)(size >= 256 ? 0 : size));
            bw.Write((byte)(size >= 256 ? 0 : size));
            bw.Write((byte)0);
            bw.Write((byte)0);
            bw.Write((short)1);
            bw.Write((short)32);
            bw.Write(payload.Length);
            bw.Write(offset);
            offset += payload.Length;
        }

        foreach (var (size, dib, png) in images)
            bw.Write(size >= 256 ? png : dib);

        bw.Flush();
    }

    private static void Extract(string exe, string outPng)
    {
        using var ico = Icon.ExtractAssociatedIcon(exe)!;
        using var bmp = ico.ToBitmap();
        bmp.Save(outPng, ImageFormat.Png);
        Console.WriteLine($"提取成功 {ico.Width}x{ico.Height} -> {outPng}");
    }

    // ---------- 预览图（6 个候选） ----------

    private static void MakePreviews()
    {
        Directory.CreateDirectory(OutDir);

        var designs = new (string key, string label, Action<Graphics> draw)[]
        {
            ("A", "A  Classic Bezel",     g => { MonitorBody(g, new RectangleF(20, 40, 216, 156), new RectangleF(36, 56, 184, 124), 14); Stand(g); }),
            ("B", "B  Focus Corners",     g => DrawBPreview(g)),
            ("C", "C  Flash",             g => DrawC(g)),
            ("D", "D  Screen Only",       g => DrawD(g)),
            ("E", "E  Flat Blue",         g => DrawE(g)),
            ("F", "F  Scan Line",         g => DrawF(g)),
        };

        foreach (var (key, label, draw) in designs)
        {
            using var bmp = Render(draw, S);
            bmp.Save(Path.Combine(OutDir, $"icon_{key}.png"), ImageFormat.Png);
            Console.WriteLine($"生成 {key} -> icon_{key}.png  ({label})");
        }

        MakeSheet(designs);
    }

    private static void DrawBPreview(Graphics g)
    {
        MonitorBody(g, new RectangleF(20, 40, 216, 156), new RectangleF(36, 56, 184, 124), 14);
        Stand(g);
        using var pen = new Pen(Color.White, 9) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        const float inset = 16, len = 30;
        float l = 36 + inset, t = 56 + inset, r = 220 - inset, b = 180 - inset;
        g.DrawLines(pen, new[] { new PointF(l, t + len), new PointF(l, t), new PointF(l + len, t) });
        g.DrawLines(pen, new[] { new PointF(r - len, t), new PointF(r, t), new PointF(r, t + len) });
        g.DrawLines(pen, new[] { new PointF(l, b - len), new PointF(l, b), new PointF(l + len, b) });
        g.DrawLines(pen, new[] { new PointF(r - len, b), new PointF(r, b), new PointF(r, b - len) });
    }

    private static void DrawC(Graphics g)
    {
        MonitorBody(g, new RectangleF(20, 40, 216, 156), new RectangleF(36, 56, 184, 124), 14);
        Stand(g);
        using var white = new SolidBrush(Color.White);
        const float cx = 196f, cy = 84f;
        g.FillPolygon(white, new[]
        {
            new PointF(cx, cy - 30), new PointF(cx + 7, cy - 7), new PointF(cx + 30, cy),
            new PointF(cx + 7, cy + 7), new PointF(cx, cy + 30), new PointF(cx - 7, cy + 7),
            new PointF(cx - 30, cy), new PointF(cx - 7, cy - 7),
        });
    }

    private static void DrawD(Graphics g)
    {
        var screen = new RectangleF(18, 52, 220, 152);
        using var bodyBrush = new LinearGradientBrush(
            new RectangleF(screen.X, screen.Y, screen.Width, screen.Height),
            BodyDark1, BodyDark2, LinearGradientMode.Vertical);
        g.FillPath(bodyBrush, Round(screen, 22));

        var inner = new RectangleF(34, 68, 188, 120);
        using var scr = new LinearGradientBrush(
            new RectangleF(inner.X, inner.Y, inner.Width, inner.Height),
            Color.FromArgb(64, 205, 255), AccentDeep, LinearGradientMode.ForwardDiagonal);
        g.FillPath(scr, Round(inner, 12));

        using var gloss = new SolidBrush(Color.FromArgb(64, 255, 255, 255));
        g.FillPolygon(gloss, new[]
        {
            new PointF(inner.X, inner.Y),
            new PointF(inner.X + inner.Width * 0.60f, inner.Y),
            new PointF(inner.X, inner.Y + inner.Height * 0.78f),
        });
    }

    private static void DrawE(Graphics g)
    {
        using var bodyBrush = new SolidBrush(Color.FromArgb(10, 132, 255));
        g.FillPath(bodyBrush, Round(new RectangleF(18, 44, 220, 150), 18));

        using var white = new SolidBrush(Color.White);
        g.FillPath(white, Round(new RectangleF(34, 60, 188, 118), 10));

        using var dark = new SolidBrush(Color.FromArgb(10, 90, 190));
        g.FillPath(dark, Round(new RectangleF(52, 78, 152, 82), 6));

        using var stand = new SolidBrush(Color.FromArgb(10, 132, 255));
        g.FillRectangle(stand, 118, 194, 20, 16);
        g.FillPath(stand, Round(new RectangleF(92, 210, 72, 14), 7));
    }

    private static void DrawF(Graphics g)
    {
        MonitorBody(g, new RectangleF(20, 40, 216, 156), new RectangleF(36, 56, 184, 124), 14);
        Stand(g);

        using var band = new LinearGradientBrush(
            new RectangleF(36, 128, 184, 40),
            Color.FromArgb(0, 255, 255, 255), Color.FromArgb(210, 255, 255, 255),
            LinearGradientMode.Vertical);

        var state = g.Save();
        g.SetClip(Round(new RectangleF(36, 56, 184, 124), 7));
        g.FillRectangle(band, new RectangleF(36, 132, 184, 26));
        g.Restore(state);
    }

    private static void MakeSheet((string key, string label, Action<Graphics> draw)[] designs)
    {
        const int cellW = 300, cellH = 340, cols = 3;
        int rows = (designs.Length + cols - 1) / cols;
        using var sheet = new Bitmap(cellW * cols, cellH * rows, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.FromArgb(246, 247, 250));

        using var labelBrush = new SolidBrush(Color.FromArgb(45, 48, 62));
        using var smallBrush = new SolidBrush(Color.FromArgb(120, 125, 140));
        using var font = new Font("Segoe UI", 13, FontStyle.Bold);
        using var smallFont = new Font("Segoe UI", 9);

        for (int i = 0; i < designs.Length; i++)
        {
            int cx = (i % cols) * cellW;
            int cy = (i / cols) * cellH;

            using var icon = Render(designs[i].draw, 160);
            g.DrawImage(icon, cx + (cellW - 160) / 2, cy + 34, 160, 160);
            g.DrawString(designs[i].label, font, labelBrush, cx + 24, cy + 8);

            int x = cx + 40;
            foreach (int sz in new[] { 16, 24, 32, 48 })
            {
                using var mini = Render(designs[i].draw, sz);
                g.DrawImage(mini, x, cy + 214, sz, sz);
                x += sz + 18;
            }
            g.DrawString("16 / 24 / 32 / 48 px", smallFont, smallBrush, cx + 40, cy + 268);
        }

        sheet.Save(Path.Combine(OutDir, "all_variants.png"), ImageFormat.Png);
        Console.WriteLine("对比图: " + Path.Combine(OutDir, "all_variants.png"));
    }
}
