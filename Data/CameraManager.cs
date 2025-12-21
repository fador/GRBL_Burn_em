using System;
using System.Text.Json;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Core;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

using laser_gui_test.Tools;

namespace laser_gui_test.Data
{
    public class CameraManager : IDisposable
    {
        private static CameraManager? _instance;
        public static CameraManager Instance => _instance ??= new CameraManager();

        private MediaCapture? _mediaCapture;
        private MediaFrameReader? _frameReader;
        private List<DirectShowDeviceInfo> _devices = new List<DirectShowDeviceInfo>();
        
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
            _devices = DeviceEnumerator.GetAllDevices();
            return _devices.Select(d => d.Name).ToList();
        }

        public async void StartCamera(int deviceIndex)
        {
            await StopCameraAsync();

            if (_devices == null || deviceIndex < 0 || deviceIndex >= _devices.Count)
                return;

            try
            {
                var device = _devices[deviceIndex];
                
                // Find correct Id (Symlink/Moniker)
                // DeviceEnumerator returns MonikerString or DevicePath.
                // MediaCaptureInitSettings needs Id.
                // NOTE: Windows 10 MediaCapture requires DeviceInformation Id usually.
                // But DirectShow enumerator returns a path. Often works. 
                // However, mix of APIs might fail.
                // Let's assume standard index-based or first available if index matches.
                // Actually, to use MediaCapture correctly, we should use DeviceInformation.FindAllAsync(DeviceClass.VideoCapture).
                // But keeping GetAvailableDevices purely using DirectShowEnumerator is fine if we can match them.
                // Let's try to use the MonikerString as Id.
                
                var settings = new MediaCaptureInitializationSettings
                {
                    VideoDeviceId = device.MonikerString,
                    MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                    StreamingCaptureMode = StreamingCaptureMode.Video,
                    SharingMode = MediaCaptureSharingMode.SharedReadOnly
                };

                _mediaCapture = new MediaCapture();
                await _mediaCapture.InitializeAsync(settings);
                
                // Create Frame Reader
                var frameSource = _mediaCapture.FrameSources.FirstOrDefault().Value;
                if (frameSource != null)
                {
                    _frameReader = await _mediaCapture.CreateFrameReaderAsync(frameSource, Windows.Media.MediaProperties.MediaEncodingSubtypes.Bgra8);
                    _frameReader.FrameArrived += OnFrameArrived;
                    await _frameReader.StartAsync();
                    _isRunning = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Camera Start Error: {ex.Message}");
                _mediaCapture?.Dispose();
                _mediaCapture = null;
                _isRunning = false;
            }
        }
        
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
                         FrameReceived?.Invoke(bmp);
                     }
                 }
            }
        }

        private unsafe Bitmap? SoftwareBitmapToBitmap(SoftwareBitmap sb)
        {
            // Ensure BGRA8
            if (sb.BitmapPixelFormat != BitmapPixelFormat.Bgra8 || sb.BitmapAlphaMode != BitmapAlphaMode.Ignore)
            {
                 // Convert if necessary (MediaFrameReader was asked for Bgra8 though)
                 if (sb.BitmapPixelFormat != BitmapPixelFormat.Bgra8) 
                 {
                      var temp = SoftwareBitmap.Convert(sb, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
                      sb = temp;
                 }
            }
            
            int w = sb.PixelWidth;
            int h = sb.PixelHeight;
            
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            
            using var buffer = sb.LockBuffer(BitmapBufferAccessMode.Read);
            using var reference = buffer.CreateReference();
            
            byte* dataInBytes;
            uint capacity;
            ((IMemoryBufferByteAccess)reference).GetBuffer(out dataInBytes, out capacity);
            
            // Lock Bitmap
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            
            // Copy
            long bytes = data.Stride * h;
            // Buffer.MemoryCopy(dataInBytes, (void*)data.Scan0, bytes, bytes); // Might be safe or not
            // Manual loop or Marshal copy
            // Stride might match?
            if (data.Stride == w * 4) // BGRA = 4 bytes
            {
                System.Buffer.MemoryCopy(dataInBytes, (void*)data.Scan0, bytes, bytes);
            }
            else
            {
                // Row by Row
                // ...
                // For now assume packed
                System.Buffer.MemoryCopy(dataInBytes, (void*)data.Scan0, bytes, bytes);
            }
            
            bmp.UnlockBits(data);
            return bmp;
        }

        [ComImport]
        [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        unsafe interface IMemoryBufferByteAccess
        {
            void GetBuffer(out byte* buffer, out uint capacity);
        }

        public async Task StopCameraAsync()
        {
            _isRunning = false;
            
            if (_frameReader != null)
            {
                _frameReader.FrameArrived -= OnFrameArrived;
                await _frameReader.StopAsync();
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
            // Stub
            cameraMatrix = null!;
            distCoeffs = null!;
            System.Windows.Forms.MessageBox.Show("Camera Calibration (Circle Grid) is disabled in this version.");
            return -1;
        }

        public double CalibrateCameraAruco()
        {
            System.Windows.Forms.MessageBox.Show("ArUco Calibration is disabled in this version.");
            return -1;
        }
        
        public PointF[]? DetectDotPattern(Bitmap frame, Bitmap? debugDraw, int rows, int cols, CalibrationPatternType type)
        {
             // Use Custom Blob Detector
             // Frame is likely RGB or RGBA. BlobDetector handles RGB locking.
             
             // 1. Detect Blobs
             var blobs = BlobDetector.DetectBlobs(frame, threshold: 120, minArea: 5, maxArea: 5000);
             
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

        private unsafe Bitmap UndistortBitmap(Bitmap src)
        {
            if (Calibration.CameraMatrix == null || Calibration.DistCoeffs == null || Calibration.CameraMatrix.Length != 9)
                return new Bitmap(src); // Return copy

             if (Calibration.CameraMatrix[0] == 0) return new Bitmap(src);

             int w = src.Width;
             int h = src.Height;
             
             Bitmap dst = new Bitmap(w, h, PixelFormat.Format24bppRgb);
             
             // We need 24bpp for easier pointer math, or 32bpp. 
             // Let's force Convert src to 24bpp
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
                 byte* srcPtr = (byte*)srcData.Scan0;
                 byte* dstPtr = (byte*)dstData.Scan0;
                 int stride = srcData.Stride; // Assumes same stride
                 
                 // Parallel loop for speed
                 Parallel.For(0, h, y => 
                 {
                     byte* rowDst = dstPtr + y * stride;
                     
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
                             
                             byte* p00 = srcPtr + y0 * stride + x0 * 3;
                             byte* p01 = srcPtr + y0 * stride + x1 * 3;
                             byte* p10 = srcPtr + y1 * stride + x0 * 3;
                             byte* p11 = srcPtr + y1 * stride + x1 * 3;
                             
                             for(int c=0; c<3; c++) 
                             {
                                 float val = 
                                    p00[c] * (1 - dx) * (1 - dy) +
                                    p01[c] * dx * (1 - dy) +
                                    p10[c] * (1 - dx) * dy +
                                    p11[c] * dx * dy;
                                    
                                 rowDst[x * 3 + c] = (byte)val;
                             }
                         }
                         else
                         {
                             // Black padding
                             rowDst[x * 3] = 0;
                             rowDst[x * 3 + 1] = 0;
                             rowDst[x * 3 + 2] = 0;
                         }
                     }
                 });
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
            StopCamera();
        }
    }
}
