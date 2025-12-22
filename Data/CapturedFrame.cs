using System;
using System.Drawing;

namespace grbl_burn_em.Data
{
    public class CapturedFrame : IDisposable
    {
        public Bitmap Image { get; set; }
        public float WorldX { get; set; } // Center X or Top-Left X? Usually Camera Position (Center)
        public float WorldY { get; set; } // Camera Position (Center)
        public float Width { get; set; }  // In World Units (mm)
        public float Height { get; set; } // In World Units (mm)

        public CapturedFrame(Bitmap image, float x, float y, float w, float h)
        {
            Image = new Bitmap(image);
            WorldX = x;
            WorldY = y;
            Width = w;
            Height = h;
        }

        public void Dispose()
        {
            Image?.Dispose();
        }
    }
}
