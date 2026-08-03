using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Emgu.CV;
using Emgu.CV.Aruco;

namespace grbl_burn_em_emulator;

/// <summary>
/// Renders the ChArUco board onto the emulator bed bitmap.
/// </summary>
public static class EmulatorBoardRenderer
{
    /// <summary>
    /// Draws the board so that its origin corner (outer top-left corner of the printed
    /// board) is placed at CNC (boardX, boardY) and the board extends toward +X/+Y,
    /// matching the registration convention.
    /// The board image is flipped vertically because the bed bitmap's +Y axis points
    /// down while CNC +Y points up; VirtualCamera flips the feed back so the markers
    /// appear unmirrored to the application (ArUco markers are not mirror-symmetric).
    /// </summary>
    /// <param name="bed">Bed bitmap to draw on.</param>
    /// <param name="scale">Bed pixels per mm.</param>
    /// <param name="boardX">CNC X of the board origin corner (mm).</param>
    /// <param name="boardY">CNC Y of the board origin corner (mm).</param>
    /// <param name="squares">Board squares per side (square board).</param>
    /// <param name="boardSizeMm">Physical board size (mm).</param>
    /// <param name="dictionary">Marker dictionary.</param>
    /// <param name="clearRect">Previous board destination rect to clear, if any.</param>
    /// <returns>The destination rect of the drawn bitmap (for clearing later).</returns>
    public static Rectangle DrawBoard(Bitmap bed, float scale,
        float boardX, float boardY, int squares, float boardSizeMm,
        Dictionary dictionary, Rectangle? clearRect = null)
    {
        float squareSizeMm = boardSizeMm / squares;
        float markerSizeMm = squareSizeMm * 0.7f;

        int boardPx = (int)(boardSizeMm * scale);
        int bx = (int)(boardX * scale);
        int by = bed.Height - (int)(boardY * scale);

        const int pxPerSquare = 80;
        int margin = pxPerSquare;
        int imgW = squares * pxPerSquare + 2 * margin;
        int imgH = squares * pxPerSquare + 2 * margin;

        using var board = new CharucoBoard(squares, squares, squareSizeMm, markerSizeMm, dictionary);
        using var boardImg = new Mat();
        ArucoInvoke.GenerateImage(board, new Size(imgW, imgH), boardImg, margin, 1);

        using var srcBmp = MatToRgbBitmap(boardImg);
        srcBmp.RotateFlip(RotateFlipType.RotateNoneFlipY);

        // The bitmap contains a 1-square margin on each side. Scale so the board region
        // (squares x squares) maps to the physical board size and place the board origin
        // (bottom-left of the board region after the flip) at CNC (boardX, boardY).
        float scalePx = boardPx / (float)squares / pxPerSquare;
        int destW = (int)MathF.Round(imgW * scalePx);
        int destH = (int)MathF.Round(imgH * scalePx);
        int destX = bx - (int)MathF.Round(margin * scalePx);
        int destY = by - (int)MathF.Round((margin + squares * pxPerSquare) * scalePx);

        lock (bed)
        {
            using var g = Graphics.FromImage(bed);
            if (clearRect.HasValue)
            {
                using var clearBrush = new SolidBrush(Color.Beige);
                g.FillRectangle(clearBrush, clearRect.Value);
            }
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(srcBmp, destX, destY, destW, destH);
        }

        return new Rectangle(destX, destY, destW, destH);
    }

    private static Bitmap MatToRgbBitmap(Mat m)
    {
        var bmp = new Bitmap(m.Width, m.Height, PixelFormat.Format24bppRgb);
        var bd = bmp.LockBits(new Rectangle(0, 0, m.Width, m.Height),
            ImageLockMode.WriteOnly, bmp.PixelFormat);
        try
        {
            if (m.NumberOfChannels == 1)
            {
                for (int y = 0; y < m.Height; y++)
                {
                    IntPtr src = m.DataPointer + y * m.Step;
                    IntPtr dst = bd.Scan0 + y * bd.Stride;
                    for (int x = 0; x < m.Width; x++)
                    {
                        byte v = Marshal.ReadByte(src + x);
                        Marshal.WriteByte(dst + x * 3, v);
                        Marshal.WriteByte(dst + x * 3 + 1, v);
                        Marshal.WriteByte(dst + x * 3 + 2, v);
                    }
                }
            }
        }
        finally
        {
            bmp.UnlockBits(bd);
        }
        return bmp;
    }
}
