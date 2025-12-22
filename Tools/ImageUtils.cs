/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace grbl_burn_em.Tools
{
    public static class ImageUtils
    {
        /// <summary>
        /// Finds the centroid of the darkest area in the image.
        /// Useful for finding laser burn marks (black dots) on light material.
        /// </summary>
        /// <param name="bmp">The image to search.</param>
        /// <param name="threshold">Luminance threshold (0-255). Pixels darker than this are considered.</param>
        /// <returns>Centroid PointF or null if nothing found.</returns>
        public static PointF? FindDarkestSpot(Bitmap bmp, int threshold = 80)
        {
            if (bmp == null) return null;

            int w = bmp.Width;
            int h = bmp.Height;

            // Ensure format is 24bpp or 32bpp
            // We'll clone to 24bpp to be safe/consistent
            Bitmap? temp = null;
            if (bmp.PixelFormat != PixelFormat.Format24bppRgb)
            {
                temp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(temp))
                {
                    g.DrawImage(bmp, 0, 0);
                }
                bmp = temp;
            }

            BitmapData data = bmp.LockBits(
                new Rectangle(0, 0, w, h),
                ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb
            );

            try
            {
                double sumX = 0;
                double sumY = 0;
                double totalWeight = 0;

                int stride = data.Stride;
                int byteCount = stride * h;
                byte[] pixels = new byte[byteCount];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, byteCount);

                for (int y = 0; y < h; y++)
                {
                    int rowOffset = y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        int idx = rowOffset + x * 3;
                        byte b = pixels[idx];
                        byte g = pixels[idx + 1];
                        byte r = pixels[idx + 2];

                        // Simple average luminance
                        int lum = (r + g + b) / 3;

                        if (lum < threshold)
                        {
                            // Weight by darkness (inverted)
                            double weight = (255 - lum);
                            sumX += x * weight;
                            sumY += y * weight;
                            totalWeight += weight;
                        }
                    }
                }

                if (totalWeight > 0)
                {
                    return new PointF((float)(sumX / totalWeight), (float)(sumY / totalWeight));
                }
            }
            finally
            {
                bmp.UnlockBits(data);
                if (temp != null) temp.Dispose();
            }

            return null;
        }
    }
}
