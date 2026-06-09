using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace grbl_burn_em_emulator;

public class VirtualCamera
{
    private static VirtualCamera? _instance;
    public static VirtualCamera Instance => _instance ??= new VirtualCamera();

    public float OffsetX { get; set; } = 50f;
    public float OffsetY { get; set; } = 0f;
    public float OffsetZ { get; set; } = 100f;

    public float FovWidth { get; set; } = 140f; // mm - covers 120mm board
    public float FovHeight { get; set; } = 100f; // mm

    public int ResX { get; set; } = 1280;
    public int ResY { get; set; } = 960;

    public bool SimulateDistortion { get; set; } = false;
    public float DistortionK1 { get; set; } = 0f;
    public float DistortionK2 { get; set; } = 0f;

    public bool DrawCrosshair { get; set; } = true;
    public float NoiseLevel { get; set; } = 2f;

    public Bitmap Capture(Bitmap bed, float bedScale)
    {
        float headX = EmulatorLogic.Instance.X;
        float headY = EmulatorLogic.Instance.Y;

        float camX = headX + OffsetX;
        float camY = headY + OffsetY;

        float cncLeft = camX - FovWidth / 2;
        float cncTop = camY + FovHeight / 2;

        float px = cncLeft * bedScale;
        float py = bed.Height - (cncTop * bedScale);
        float pw = FovWidth * bedScale;
        float ph = FovHeight * bedScale;

        var frame = new Bitmap(ResX, ResY, PixelFormat.Format24bppRgb);

        using (var g = Graphics.FromImage(frame))
        {
            g.Clear(Color.Black);

            var srcRect = new RectangleF(px, py, pw, ph);
            var dstRect = new RectangleF(0, 0, ResX, ResY);

            lock (bed)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(bed, dstRect, srcRect, GraphicsUnit.Pixel);
            }

            if (SimulateDistortion && Math.Abs(DistortionK1) > 0.0001f)
                ApplyDistortion(frame);

            if (NoiseLevel > 0)
                ApplyNoise(frame, NoiseLevel);

            if (DrawCrosshair)
            {
                int cx = ResX / 2, cy = ResY / 2;
                using var pen = new Pen(Color.FromArgb(128, 0, 255, 0), 1f);
                g.DrawLine(pen, cx - 20, cy, cx + 20, cy);
                g.DrawLine(pen, cx, cy - 20, cx, cy + 20);
                g.DrawEllipse(pen, cx - 5, cy - 5, 10, 10);
            }
        }

        return frame;
    }

    private void ApplyDistortion(Bitmap bmp)
    {
        float cx = bmp.Width / 2f;
        float cy = bmp.Height / 2f;
        float norm = MathF.Max(bmp.Width, bmp.Height);

        using var src = new Bitmap(bmp);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Black);

        var srcData = src.LockBits(new Rectangle(0, 0, src.Width, src.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        var dstData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

        int srcStride = srcData.Stride;
        int dstStride = dstData.Stride;
        int w = bmp.Width, h = bmp.Height;

        unsafe
        {
            byte* srcPtr = (byte*)srcData.Scan0;
            byte* dstPtr = (byte*)dstData.Scan0;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float nx = (x - cx) / norm;
                    float ny = (y - cy) / norm;
                    float r2 = nx * nx + ny * ny;
                    float radial = 1f + DistortionK1 * r2 + DistortionK2 * r2 * r2;
                    float sx = cx + nx * radial * norm;
                    float sy = cy + ny * radial * norm;

                    int ix = (int)sx, iy = (int)sy;
                    if (ix >= 0 && ix < w - 1 && iy >= 0 && iy < h - 1)
                    {
                        int srcOff = iy * srcStride + ix * 3;
                        int dstOff = y * dstStride + x * 3;
                        dstPtr[dstOff] = srcPtr[srcOff];
                        dstPtr[dstOff + 1] = srcPtr[srcOff + 1];
                        dstPtr[dstOff + 2] = srcPtr[srcOff + 2];
                    }
                }
            }
        }

        bmp.UnlockBits(dstData);
        src.UnlockBits(srcData);
    }

    private static void ApplyNoise(Bitmap bmp, float level)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
        var rng = new Random();
        int stride = data.Stride;
        int w = bmp.Width, h = bmp.Height;

        unsafe
        {
            byte* ptr = (byte*)data.Scan0;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int off = y * stride + x * 3;
                    int n = (int)((rng.NextDouble() * 2 - 1) * level);
                    ptr[off] = (byte)Math.Clamp(ptr[off] + n, 0, 255);
                    ptr[off + 1] = (byte)Math.Clamp(ptr[off + 1] + n, 0, 255);
                    ptr[off + 2] = (byte)Math.Clamp(ptr[off + 2] + n, 0, 255);
                }
            }
        }

        bmp.UnlockBits(data);
    }
}
