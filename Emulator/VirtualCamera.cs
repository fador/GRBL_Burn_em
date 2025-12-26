using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace grbl_burn_em_emulator;

public class VirtualCamera
{
    private static VirtualCamera? _instance;
    public static VirtualCamera Instance => _instance ??= new VirtualCamera();

    // Camera Parameters relative to Head
    public float OffsetX { get; set; } = 50; // mm
    public float OffsetY { get; set; } = 0;   // mm
    
    // FOV in mm
    public float FovWidth { get; set; } = 80; // mm
    public float FovHeight { get; set; } = 60; // mm
    
    // Output Resolution
    public int ResX { get; set; } = 1280;
    public int ResY { get; set; } = 960;

    public Bitmap Capture(Bitmap bed, float bedScale)
    {
        // 1. Calculate Crop Rect in Bed Pixels
        float headX = EmulatorLogic.Instance.X;
        float headY = EmulatorLogic.Instance.Y;
        
        float camX = headX + OffsetX;
        float camY = headY + OffsetY;
        
        // FOV Boundaries in CNC Coordinates
        float cncLeft = camX - FovWidth / 2;
        float cncTop = camY + FovHeight / 2; // Top in CNC is Y+ relative to Center
        
        // Convert to Bitmap Pixels (Y Inverted)
        float px = cncLeft * bedScale;
        float py = bed.Height - (cncTop * bedScale);
        float pw = FovWidth * bedScale;
        float ph = FovHeight * bedScale;
        
        // 2. Extract Logic
        var frame = new Bitmap(ResX, ResY, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(frame))
        {
            g.Clear(Color.Black);
            
            // Transform: We want to map source Rect(px, py, pw, ph) to dest Rect(0,0, ResX, ResY)
            // But we also want to add Rotation/Distortion?
            // Simple approach first: DrawImage with SrcRect
            
            var srcRect = new RectangleF(px, py, pw, ph);
            var dstRect = new RectangleF(0, 0, ResX, ResY);
            
            // We need to lock bed? Or clone? 
            // The caller should ideally handle lock, but we can try here.
            lock(bed)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(bed, dstRect, srcRect, GraphicsUnit.Pixel);
            }
            
            // Add Noise/Vignette to make it look real
            // Fill with Noise
            // This is slow in GDI+, maybe skip for performance or do simple overlay
            
            // Draw Crosshair (Lens Center)
            // g.DrawLine(Pens.Green, ResX/2 - 20, ResY/2, ResX/2 + 20, ResY/2);
            // g.DrawLine(Pens.Green, ResX/2, ResY/2 - 20, ResX/2, ResY/2 + 20);
        }
        
        return frame;
    }
}
