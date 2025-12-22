/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using System.Text.Json;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Core;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

using grbl_burn_em.Tools;

using WinRT;

namespace grbl_burn_em.Data
{
    public class CameraManager : IDisposable
    {
        private static CameraManager? _instance;
        public static CameraManager Instance => _instance ??= new CameraManager();

        private MediaCapture? _mediaCapture;
        private MediaFrameReader? _frameReader;
        // private List<DirectShowDeviceInfo> _devices = new List<DirectShowDeviceInfo>(); // Removed
        private List<DeviceInformation> _deviceInfos = new List<DeviceInformation>();
        
        public event Action<Bitmap>? FrameReceived;
        public event Action? CameraStopped;
        
        public CalibrationData Calibration { get; set; } = new CalibrationData();
        public List<CapturedFrame> CapturedFrames { get; private set; } = new List<CapturedFrame>();
        private object _framesLock = new object();
        
        private volatile bool _isRunning = false;
        public bool IsRunning => _isRunning && _mediaCapture != null;

        public CameraManager()
        {
            LoadCalibration();
        }

        public List<string> GetAvailableDevices()
        {
            // Use WinRT DeviceInformation to get IDs compatible with MediaCapture
            try 
            {
                var task = DeviceInformation.FindAllAsync(DeviceClass.VideoCapture).AsTask();
                task.Wait();
                var devices = task.Result;
                _deviceInfos = devices.ToList();
                return _deviceInfos.Select(d => d.Name).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error enumerating devices: {ex.Message}");
                return new List<string>();
            }
        }

        public void StartCamera(int deviceIndex)
        {
             StartCameraAsync(deviceIndex).Wait();
        }

        public async Task StartCameraAsync(int deviceIndex)
        {
            if (_isRunning) await StopCameraAsync();

            if (_deviceInfos == null || deviceIndex < 0 || deviceIndex >= _deviceInfos.Count)
                return;

            try
            {
                var device = _deviceInfos[deviceIndex];
                
                var settings = new MediaCaptureInitializationSettings
                {
                    VideoDeviceId = device.Id,
                    MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                    StreamingCaptureMode = StreamingCaptureMode.Video,
                    SharingMode = MediaCaptureSharingMode.SharedReadOnly
                };

                _mediaCapture = new MediaCapture();
                await _mediaCapture.InitializeAsync(settings);
                
                // Create Frame Reader
                if (_mediaCapture.FrameSources.Count == 0)
                {
                    System.Windows.Forms.MessageBox.Show("No FrameSources found on this device!", "Camera Debug");
                    return;
                }

                var frameSource = _mediaCapture.FrameSources.FirstOrDefault().Value;
                if (frameSource != null)
                {
                    // Debug info about format
                    var formats = frameSource.SupportedFormats.Select(f => $"{f.Subtype} {f.VideoFormat.Width}x{f.VideoFormat.Height}").Take(5);
                    // System.Windows.Forms.MessageBox.Show($"Selected Source: {frameSource.Id}\nFormats: {string.Join(", ", formats)}", "Camera Debug");

                    _frameReader = await _mediaCapture.CreateFrameReaderAsync(frameSource, Windows.Media.MediaProperties.MediaEncodingSubtypes.Bgra8);
                    _frameReader.FrameArrived += OnFrameArrived;
                    await _frameReader.StartAsync();
                    _isRunning = true;
                    // System.Windows.Forms.MessageBox.Show("FrameReader Started", "Camera Debug");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Camera Start Error: {ex.Message}");
                System.Windows.Forms.MessageBox.Show($"Camera Start Error: {ex.Message}\n\nStackTrace: {ex.StackTrace}", "Camera Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                _mediaCapture?.Dispose();
                _mediaCapture = null;
                _isRunning = false;
            }
        }
        
        public bool DebugDotDetection { get; set; } = false;

        private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
        {
            if (!_isRunning) return;

            using var frameReference = sender.TryAcquireLatestFrame();
            if (frameReference != null)
            {
                 var videoFrame = frameReference.VideoMediaFrame;
                 if (videoFrame != null && videoFrame.SoftwareBitmap != null)
                 {
                     using var sb = videoFrame.SoftwareBitmap;
                     
                     // Convert SoftwareBitmap to System.Drawing.Bitmap
                     Bitmap? bmp = SoftwareBitmapToBitmap(sb);
                     if (bmp != null)
                     {
                         // Debug: Detect Dots
                         if (DebugDotDetection)
                         {
                             try
                             {
                                 // Draw directly on the frame we are about to send
                                 // Pass 'bmp' as both source and debug info destination
                                 DetectDotPattern(bmp, bmp, 5, 4, CalibrationPatternType.Circles);
                             }
                             catch (Exception ex)
                             {
                                 System.Diagnostics.Debug.WriteLine($"Debug Dot Detection Failed: {ex.Message}");
                             }
                         }

                         if (FrameReceived != null)
                         {
                              FrameReceived.Invoke(bmp);
                         }
                     }
                 }
            }
        }

        private void SafeExecute(Action action)
        {
             Task.Run(action);
        }

        private bool _bitmapConversionErrorShown = false;
        private Bitmap? SoftwareBitmapToBitmap(SoftwareBitmap inputSb)
        {
            SoftwareBitmap? sbToUse = inputSb;
            bool shouldDisposeSb = false;

            try
            {
                // Ensure BGRA8
                if (inputSb.BitmapPixelFormat != BitmapPixelFormat.Bgra8 || inputSb.BitmapAlphaMode != BitmapAlphaMode.Ignore)
                {
                     // Convert
                     sbToUse = SoftwareBitmap.Convert(inputSb, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
                     shouldDisposeSb = true;
                }
                
                int w = sbToUse.PixelWidth;
                int h = sbToUse.PixelHeight;
                
                var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                
                // Use standard CopyToBuffer (Avoids unsafe COM cast issues)
                int size = w * h * 4;
                var uwpBuffer = new Windows.Storage.Streams.Buffer((uint)size);
                sbToUse.CopyToBuffer(uwpBuffer);
                
                byte[] bytes = new byte[size];
                using (var reader = DataReader.FromBuffer(uwpBuffer))
                {
                    reader.ReadBytes(bytes);
                }
                
                // Lock System.Drawing.Bitmap
                BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                
                try
                {
                    int dstStride = data.Stride;
                    int srcStride = w * 4; // BGRA is packed in the byte array
                    
                    if (dstStride == srcStride)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(bytes, 0, data.Scan0, size);
                    }
                    else
                    {
                        // Row by Row copy if stride differs (e.g. padding)
                        for (int y = 0; y < h; y++)
                        {
                            IntPtr dstPtr = data.Scan0 + (y * dstStride);
                            System.Runtime.InteropServices.Marshal.Copy(bytes, y * srcStride, dstPtr, srcStride);
                        }
                    }
                }
                finally
                {
                    bmp.UnlockBits(data);
                }
                
                return bmp;
            }
            catch (Exception ex)
            {
                if (!_bitmapConversionErrorShown)
                {
                    _bitmapConversionErrorShown = true;
                    this.SafeExecute(() => System.Windows.Forms.MessageBox.Show($"Bitmap Conversion Failed: {ex.Message}\nType: {ex.GetType().Name}\nStack: {ex.StackTrace}", "Debug Error"));
                }
                else
                {
                    // Print to output once error is shown
                    System.Diagnostics.Debug.WriteLine($"Bitmap Conversion Failed: {ex.Message}");
                }
                return null;
            }
            finally
            {
                if (shouldDisposeSb && sbToUse != null)
                {
                    sbToUse.Dispose();
                }
            }
        }



        public async Task StopCameraAsync()
        {
            _isRunning = false;
            
            if (_frameReader != null)
            {
                _frameReader.FrameArrived -= OnFrameArrived;
                await _frameReader.StopAsync(); // Await outside lock
                _frameReader.Dispose();
                _frameReader = null;
            }
            
            if (_mediaCapture != null)
            {
                _mediaCapture.Dispose();
                _mediaCapture = null;
            }
            
            CameraStopped?.Invoke();
        }

        public void StopCamera()
        {
             // Sync wrapper
             StopCameraAsync().Wait();
        }
        


        private void LoadCalibration()
        {
            try 
            {
                if (System.IO.File.Exists("calibration.json"))
                {
                    string json = System.IO.File.ReadAllText("calibration.json");
                    var data = JsonSerializer.Deserialize<CalibrationData>(json);
                    if (data != null) Calibration = data;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load calibration: {ex.Message}");
            }
        }

        public void ResetCalibration()
        {
             // Reset logic if needed
             // _allCorners.Clear();
             // _allIds.Clear();
        }

        public void SaveCalibration()
        {
             try
             {
                 var options = new JsonSerializerOptions { WriteIndented = true };
                 string json = JsonSerializer.Serialize(Calibration, options);
                 System.IO.File.WriteAllText("calibration.json", json);
             }
             catch (Exception ex)
             {
                 System.Diagnostics.Debug.WriteLine($"Failed to save calibration: {ex.Message}");
             }
        }

        // --- STUBS ---
        
        public double CalibrateCameraDots(List<Bitmap> frames, int rows, int cols, float spacingMm, CalibrationPatternType type, out double[] cameraMatrix, out double[] distCoeffs)
        {
            cameraMatrix = new double[9];
            distCoeffs = new double[5]; // Initialize with zeros (Zhang's linear method doesn't estimate distortion)
            
            if (frames.Count < 3)
            {
                System.Windows.Forms.MessageBox.Show("Need at least 3 frames for calibration.");
                return -1;
            }

            try
            {
                var calibrator = new Tools.ZhangCalibrator();
                int validFrames = 0;

                // 1. Generate World Points (Z=0)
                // Assymetric Circle Grid or similar?
                // The logical coordinates for circles grid.
                // Assuming standard row-major ordering.
                var worldPoints = new List<double[]>();
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        // X, Y. 
                        // For asymmetric grid, the spacing might be different.
                        // Impl: simple grid for now. 
                        // If asymmetric: 
                        // odd rows: 0, 1, 2...
                        // even rows: 0.5, 1.5, ...? 
                        // Standard OpenCV 'Cycles' grid usually is just grid.
                        // Let's assume standard grid for simplicity or ask user.
                        // User used 'FindCirclesGrid' with 'AsymmetricClustering'?
                        // Keep it simple: regular grid X=c*spacing, Y=r*spacing.
                        double x = c * spacingMm;
                        double y = r * spacingMm;
                        
                        // Wait, Asymmetric Circle Grid usually has offsets.
                        // If type == CalibrationPatternType.AsymmetricCirclesGrid
                        if (type == CalibrationPatternType.AsymmetricCirclesGrid)
                        {
                             x = (c * 2 + (r % 2)) * spacingMm / 2.0; // Approximation of typical asymmetric grid
                             y = r * spacingMm / 2.0;
                         }

                        worldPoints.Add(new[] { x, y });
                    }
                }

                foreach (var frame in frames)
                {
                    // 2. Detect Image Points
                    // We need points in order.
                    var pointsF = DetectDotPattern(frame, null, rows, cols, type);
                    if (pointsF != null && pointsF.Length == rows * cols)
                    {
                        var imagePoints = pointsF.Select(p => new[] { (double)p.X, (double)p.Y }).ToList();
                        calibrator.AddView(worldPoints, imagePoints);
                        validFrames++;
                    }
                }

                if (validFrames < 3)
                {
                    System.Windows.Forms.MessageBox.Show($"Only {validFrames} valid frames found. Need 3+.");
                    return -1;
                }

                // 3. Calibrate
                var result = calibrator.Calibrate();
                
                // 4. Output
                // Map K (3x3) to Array (9)
                // K is:
                // alpha gamma u0
                // 0     beta  v0
                // 0     0     1
                
                // Row Major
                cameraMatrix[0] = result.IntrinsicMatrix[0, 0];
                cameraMatrix[1] = result.IntrinsicMatrix[0, 1];
                cameraMatrix[2] = result.IntrinsicMatrix[0, 2];
                cameraMatrix[3] = result.IntrinsicMatrix[1, 0];
                cameraMatrix[4] = result.IntrinsicMatrix[1, 1];
                cameraMatrix[5] = result.IntrinsicMatrix[1, 2];
                cameraMatrix[6] = result.IntrinsicMatrix[2, 0];
                cameraMatrix[7] = result.IntrinsicMatrix[2, 1];
                cameraMatrix[8] = result.IntrinsicMatrix[2, 2];
                
                // Reprojection Error?
                // Calculate basic error
                return 0.0; // Placeholder error
            }
            catch (Exception ex)
            {
                 System.Diagnostics.Debug.WriteLine($"Calibration Failed: {ex.Message}");
                 System.Windows.Forms.MessageBox.Show($"Calibration Exception: {ex.Message}");
                 return -1;
            }
        }

        public double CalibrateCameraAruco()
        {
            System.Windows.Forms.MessageBox.Show("ArUco Calibration is disabled in this version.");
            return -1;
        }
        
        public int DotDetectionThreshold { get; set; } = 120; // Default

        public PointF[]? DetectDotPattern(Bitmap frame, Bitmap? debugDraw, int rows, int cols, CalibrationPatternType type)
        {
             // Use Custom Blob Detector
             // Frame is likely RGB or RGBA. BlobDetector handles RGB locking.
             
             // 1. Detect Blobs
             var blobs = BlobDetector.DetectBlobs(frame, threshold: (byte)DotDetectionThreshold, minArea: 5, maxArea: 5000);
             
             // 2. Filter / Detect Grid?
             // Since we removed OpenCV FindCirclesGrid, we need to manually organize blobs into a grid.
             // This is complex. 
             // Ideally we just return ALL blobs as points and let the caller visualize them?
             // Or if we need exact rows*cols for calibration, we fail if count != rows*cols.
             // But 'DetectDotPattern' is used in Calibration Loop.
             
             // For Offset Calibration (LensCalibrationForm uses it), it just looks for ANY detection?
             // Actually LensCalibrationForm expects specific pattern.
             // Since we disabled Calibration, maybe we don't need full Grid Sorting.
             // But OffsetCalibration uses 'CaptureSpotLocation' which calls 'ImageUtils.FindDarkestSpot', NOT 'DetectDotPattern'.
             // So this might be only for LensCalibrationForm.
             
             if (blobs == null) return null;
             
             var result = blobs.Select(b => new PointF(b.X, b.Y)).ToArray();
             
             if (debugDraw != null)
             {
                 using var g = Graphics.FromImage(debugDraw);
                 foreach(var b in blobs)
                 {
                     g.DrawEllipse(Pens.Red, b.X - 2, b.Y - 2, 4, 4);
                 }
             }
             
             return result;
        }

        // --- Logic ---

        public void StartScan()
        {
             var workW = AppConfiguration.Instance.WorkAreaWidth;
             var workH = AppConfiguration.Instance.WorkAreaHeight;
             var job = new GridCaptureJob();
             Task.Run(() => job.Start(workW, workH));
        }

        public void CaptureCurrentFrame(float worldX, float worldY, float width, float height)
        {
            var tcs = new TaskCompletionSource<Bitmap>();
            Action<Bitmap> handler = null!;
            handler = (bmp) => 
            {
                tcs.TrySetResult(new Bitmap(bmp));
            };
            
            FrameReceived += handler;
            
            if (tcs.Task.Wait(1000))
            {
                 FrameReceived -= handler;
                 var img = tcs.Task.Result;
                 
                 // Undistort
                 Bitmap undistorted = UndistortBitmap(img);
                 
                 lock(_framesLock)
                 {
                     CapturedFrames.Add(new CapturedFrame(undistorted, worldX, worldY, width, height));
                 }
                 img.Dispose();
            }
            else
            {
                FrameReceived -= handler;
            }
        }

        public Bitmap UndistortFrame(Bitmap frame)
        {
            return UndistortBitmap(frame);
        }

        private Bitmap UndistortBitmap(Bitmap src)
        {
            if (Calibration.CameraMatrix == null || Calibration.DistCoeffs == null || Calibration.CameraMatrix.Length != 9)
                return new Bitmap(src); // Return copy

             if (Calibration.CameraMatrix[0] == 0) return new Bitmap(src);

             int w = src.Width;
             int h = src.Height;
             
             Bitmap dst = new Bitmap(w, h, PixelFormat.Format24bppRgb);
             
             // We need 24bpp for easier array math
             Bitmap src24 = src;
             bool disposeSrc24 = false;
             if (src.PixelFormat != PixelFormat.Format24bppRgb)
             {
                 src24 = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                 using var g = Graphics.FromImage(src24);
                 g.DrawImage(src, 0, 0, w, h);
                 disposeSrc24 = true;
             }
             
             BitmapData srcData = src24.LockBits(
                new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                
             BitmapData dstData = dst.LockBits(
                new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

             try 
             {
                 int stride = srcData.Stride;
                 int bytes = Math.Abs(stride) * h;
                 byte[] srcBytes = new byte[bytes];
                 byte[] dstBytes = new byte[bytes];

                 // Copy source to managed array
                 Marshal.Copy(srcData.Scan0, srcBytes, 0, bytes);
                 
                 // Parallel loop for speed
                 Parallel.For(0, h, y => 
                 {
                     int rowOffset = y * stride;
                     
                     for (int x = 0; x < w; x++)
                     {
                         // Destination (Undistorted) -> Source (Distorted)
                         PointF srcPt = CalibrationMath.DistortPoint(new PointF(x, y), Calibration.CameraMatrix, Calibration.DistCoeffs);
                         
                         // Bilinear Interpolation
                         float sx = srcPt.X;
                         float sy = srcPt.Y;
                         
                         if (sx >= 0 && sx < w - 1 && sy >= 0 && sy < h - 1)
                         {
                             int x0 = (int)sx;
                             int y0 = (int)sy;
                             int x1 = x0 + 1;
                             int y1 = y0 + 1;
                             
                             float dx = sx - x0;
                             float dy = sy - y0;
                             
                             // Indices in srcBytes
                             int idx00 = y0 * stride + x0 * 3;
                             int idx01 = y0 * stride + x1 * 3;
                             int idx10 = y1 * stride + x0 * 3;
                             int idx11 = y1 * stride + x1 * 3;
                             
                             int dstIdx = rowOffset + x * 3;

                             for(int c=0; c<3; c++) 
                             {
                                 float val = 
                                    srcBytes[idx00 + c] * (1 - dx) * (1 - dy) +
                                    srcBytes[idx01 + c] * dx * (1 - dy) +
                                    srcBytes[idx10 + c] * (1 - dx) * dy +
                                    srcBytes[idx11 + c] * dx * dy;
                                    
                                 dstBytes[dstIdx + c] = (byte)val;
                             }
                         }
                         else
                         {
                             // Black padding
                             int dstIdx = rowOffset + x * 3;
                             dstBytes[dstIdx] = 0;
                             dstBytes[dstIdx + 1] = 0;
                             dstBytes[dstIdx + 2] = 0;
                         }
                     }
                 });

                 // Copy managed array to destination
                 Marshal.Copy(dstBytes, 0, dstData.Scan0, bytes);
             }
             finally
             {
                 dst.UnlockBits(dstData);
                 src24.UnlockBits(srcData);
                 if (disposeSrc24) src24.Dispose();
             }
             
             return dst;
        }
        
        public void ComputeHomography(PointF[] imagePoints, PointF[] worldPoints)
        {
            if (imagePoints.Length != 4 || worldPoints.Length != 4) return;

            try
            {
                var h = CalibrationMath.ComputeHomography(imagePoints, worldPoints);
                if (h != null)
                {
                    Calibration.Homography = h;
                    SaveCalibration();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Homography Error: {ex.Message}");
            }
        }

        public PointF UndistortPoint(PointF p)
        {
            return CalibrationMath.UndistortPoint(p, Calibration.CameraMatrix, Calibration.DistCoeffs);
        }
        
        /// <summary>
        /// Detect ArUco - Stubbed
        /// </summary>
        public void DetectArucoMarkers(Bitmap image, out PointF[][] corners, out int[] ids)
        {
             // Stub
             corners = new PointF[0][];
             ids = new int[0];
        }

        public void Dispose()
        {
            StopCameraAsync().Wait();
        }
    }
}
