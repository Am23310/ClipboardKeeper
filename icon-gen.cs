// 图标生成器：绘制 ClipboardKeeper 图标并输出 app.ico（PNG 压缩格式，Vista+）
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

static class IconGen
{
    static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static Bitmap Draw(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = size / 256f;

            // 深蓝圆角底 + 青色描边
            var bg = RoundedRect(new RectangleF(8 * s, 8 * s, 240 * s, 240 * s), 52 * s);
            using (var b = new SolidBrush(Color.FromArgb(255, 15, 38, 58))) g.FillPath(b, bg);
            using (var p = new Pen(Color.FromArgb(255, 0, 200, 255), 7 * s)) g.DrawPath(p, bg);

            // 白色剪贴板纸
            var paper = RoundedRect(new RectangleF(66 * s, 58 * s, 124 * s, 150 * s), 14 * s);
            using (var b = new SolidBrush(Color.White)) g.FillPath(b, paper);

            // 顶部夹子（青色）
            var clip = RoundedRect(new RectangleF(102 * s, 42 * s, 52 * s, 30 * s), 8 * s);
            using (var b = new SolidBrush(Color.FromArgb(255, 0, 168, 224))) g.FillPath(b, clip);
            using (var b = new SolidBrush(Color.FromArgb(255, 0, 168, 224))) g.FillRectangle(b, 102 * s, 56 * s, 52 * s, 12 * s);

            // 三条内容线条：青、深灰、琥珀（第三条短，加一点识别度）
            using (var p = new Pen(Color.FromArgb(255, 0, 168, 224), 11 * s))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawLine(p, 88 * s, 106 * s, 168 * s, 106 * s);
            }
            using (var p = new Pen(Color.FromArgb(255, 120, 138, 155), 11 * s))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawLine(p, 88 * s, 136 * s, 168 * s, 136 * s);
            }
            using (var p = new Pen(Color.FromArgb(255, 255, 176, 32), 11 * s))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawLine(p, 88 * s, 166 * s, 130 * s, 166 * s);
            }
        }
        return bmp;
    }

    static byte[] ToPng(Bitmap bmp)
    {
        using (var ms = new MemoryStream())
        {
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
    }

    // 传统未压缩 DIB 条目（XOR 32bpp 自下而上 + AND 掩码），老 GDI 也能解析
    static byte[] ToDib(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        var px = new int[w * h];
        BitmapData d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        System.Runtime.InteropServices.Marshal.Copy(d.Scan0, px, 0, px.Length);
        bmp.UnlockBits(d);

        int xorStride = w * 4;
        int andStride = ((w + 31) / 32) * 4;
        var dib = new byte[40 + xorStride * h + andStride * h];

        // BITMAPINFOHEADER，biHeight = 高度 ×2（XOR + AND）
        WriteInt(dib, 0, 40);
        WriteInt(dib, 4, w);
        WriteInt(dib, 8, h * 2);
        dib[12] = 1; dib[13] = 0;          // planes
        dib[14] = 32; dib[15] = 0;         // bpp
        WriteInt(dib, 20, xorStride * h + andStride * h);

        // XOR：BGRA 自下而上
        int pos = 40;
        for (int y = h - 1; y >= 0; y--)
        {
            for (int x = 0; x < w; x++)
            {
                int argb = px[y * w + x];
                dib[pos++] = (byte)(argb & 0xFF);         // B
                dib[pos++] = (byte)((argb >> 8) & 0xFF);  // G
                dib[pos++] = (byte)((argb >> 16) & 0xFF); // R
                dib[pos++] = (byte)((argb >> 24) & 0xFF); // A
            }
        }
        // AND 掩码全 0（不透明，透明度由 alpha 通道负责）
        return dib;
    }

    static void WriteInt(byte[] b, int off, int v)
    {
        b[off] = (byte)v; b[off + 1] = (byte)(v >> 8); b[off + 2] = (byte)(v >> 16); b[off + 3] = (byte)(v >> 24);
    }

    static void Main()
    {
        int[] sizes = new int[] { 256, 128, 64, 48, 32, 24, 16 };
        var dibs = new List<byte[]>();
        foreach (int size in sizes)
        {
            using (Bitmap bmp = Draw(size)) dibs.Add(ToDib(bmp));
        }

        using (var fs = File.Create("app.ico"))
        using (var w = new BinaryWriter(fs))
        {
            w.Write((short)0);          // reserved
            w.Write((short)1);          // type: icon
            w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                w.Write((byte)(s == 256 ? 0 : s)); // width
                w.Write((byte)(s == 256 ? 0 : s)); // height
                w.Write((byte)0);                  // palette
                w.Write((byte)0);                  // reserved
                w.Write((short)1);                 // planes
                w.Write((short)32);                // bpp
                w.Write((int)dibs[i].Length);
                w.Write((int)offset);
                offset += dibs[i].Length;
            }
            foreach (var dib in dibs) w.Write(dib);
        }
        Console.WriteLine("app.ico written: " + sizes.Length + " sizes (DIB)");

        // 预览图
        using (Bitmap prev = Draw(128)) prev.Save("icon-preview.png", ImageFormat.Png);
        Console.WriteLine("icon-preview.png written");
    }
}
