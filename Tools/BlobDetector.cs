/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace grbl_burn_em.Tools
{
    public class BlobDetector
    {
        public class Blob
        {
            public float X { get; set; }
            public float Y { get; set; }
            public int Area { get; set; }
        }

        public static List<Blob> DetectBlobs(Bitmap bmp, int threshold = 100, int minArea = 5, int maxArea = 10000)
        {
            var blobs = new List<Blob>();
            if (bmp == null) return blobs;

            int w = bmp.Width;
            int h = bmp.Height;

            BitmapData data = bmp.LockBits(
                new Rectangle(0, 0, w, h),
                ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb
            );

            try
            {
                int stride = data.Stride;
                int byteCount = stride * h;
                byte[] pixels = new byte[byteCount];
                Marshal.Copy(data.Scan0, pixels, 0, byteCount);

                bool[] visited = new bool[w * h];
                
                // Simple Connected Components
                // Iterate pixels
                for (int y = 0; y < h; y++)
                {
                    int rowOffset = y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        int idx = y * w + x;
                        if (visited[idx]) continue;

                        int pIdx = rowOffset + x * 3;
                        byte b = pixels[pIdx];
                        byte g = pixels[pIdx + 1];
                        byte r = pixels[pIdx + 2];
                        
                        // Luminance
                        int lum = (r + g + b) / 3;

                        if (lum < threshold) // Dark spot
                        {
                            // Start filling
                            var blob = FloodFill(pixels, visited, w, h, stride, x, y, threshold);
                            if (blob.Area >= minArea && blob.Area <= maxArea)
                            {
                                blobs.Add(blob);
                            }
                        }
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            return blobs;
        }

        private static Blob FloodFill(byte[] pixels, bool[] visited, int w, int h, int stride, int startX, int startY, int threshold)
        {
            var q = new Queue<Point>();
            q.Enqueue(new Point(startX, startY));
            
            visited[startY * w + startX] = true;

            double sumX = 0;
            double sumY = 0;
            int count = 0;

            while (q.Count > 0)
            {
                var p = q.Dequeue();
                sumX += p.X;
                sumY += p.Y;
                count++;

                // 4-connectivity
                int[] dx = { 1, -1, 0, 0 };
                int[] dy = { 0, 0, 1, -1 };

                for (int i = 0; i < 4; i++)
                {
                    int nx = p.X + dx[i];
                    int ny = p.Y + dy[i];

                    if (nx >= 0 && nx < w && ny >= 0 && ny < h)
                    {
                        int nIdx = ny * w + nx;
                        if (!visited[nIdx])
                        {
                            int pIdx = ny * stride + nx * 3;
                            byte b = pixels[pIdx];
                            byte g = pixels[pIdx + 1];
                            byte r = pixels[pIdx + 2];
                            int lum = (r + g + b) / 3;

                            if (lum < threshold)
                            {
                                visited[nIdx] = true;
                                q.Enqueue(new Point(nx, ny));
                            }
                        }
                    }
                }
            }

            return new Blob
            {
                X = (float)(sumX / count),
                Y = (float)(sumY / count),
                Area = count
            };
        }
    }
}
