using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
//using AForge.Video;
//using AForge.Video.DirectShow;
using OpenCvSharp;
using OpenCvSharp.Extensions;
//using OpenCvSharp.Aruco;

namespace laser_gui_test.Data
{
    public class CameraManager : IDisposable
    {
        private static CameraManager? _instance;
        public static CameraManager Instance => _instance ??= new CameraManager();

        // private FilterInfoCollection? _videoDevices; // Removed AForge
        private VideoCapture? _capture;
        private Task? _captureTask;
        private CancellationTokenSource? _cts;
        private List<DirectShowDeviceInfo> _devices = new List<DirectShowDeviceInfo>();
        
        public event Action<Bitmap>? FrameReceived;
        public event Action? CameraStopped;
        
        public CalibrationData Calibration { get; set; } = new CalibrationData();
        public List<CapturedFrame> CapturedFrames { get; private set; } = new List<CapturedFrame>();
        private object _framesLock = new object();
        
        
        public bool IsRunning => _capture != null && _capture.IsOpened();

        public CameraManager()
        {
            LoadCalibration();
        }

        public List<string> GetAvailableDevices()
        {
            _devices = DeviceEnumerator.GetAllDevices();
            return _devices.Select(d => d.Name).ToList();
        }

        public void StartCamera(int deviceIndex)
        {
            StopCamera();

            if (_devices == null || deviceIndex < 0 || deviceIndex >= _devices.Count)
                return;
            
            _cts = new CancellationTokenSource();
            _captureTask = Task.Run(() => CaptureLoop(deviceIndex, _cts.Token));
        }

        private void CaptureLoop(int deviceIndex, CancellationToken token)
        {
            VideoCapture? localCapture = null;
            try
            {
                localCapture = new VideoCapture(deviceIndex, VideoCaptureAPIs.DSHOW);
                
                // Expose to Start/Stop logic if needed, but risky. 
                // Let's use IsRunning to check thread status only.
                // Or safely assign to field.
                lock (this)
                {
                    _capture = localCapture;
                }

                if (!localCapture.IsOpened())
                {
                     System.Diagnostics.Debug.WriteLine($"Failed to open camera index {deviceIndex}");
                     return;
                }

                using var mat = new Mat();
                
                while (!token.IsCancellationRequested && localCapture.IsOpened())
                {
                    if (localCapture.Read(mat) && !mat.Empty())
                    {
                        if (token.IsCancellationRequested) break;
                        
                        var bmp = BitmapConverter.ToBitmap(mat);
                        FrameReceived?.Invoke(bmp);
                    }
                    else
                    {
                        Task.Delay(10).Wait(token);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Capture Loop Error: {ex.Message}");
            }
            finally
            {
                lock (this)
                {
                    if (_capture == localCapture) _capture = null;
                }
                
                localCapture?.Release();
                localCapture?.Dispose();
            }
        }

        public void StopCamera()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts = null; // Detach
                CameraStopped?.Invoke();
                
                // Note: We don't join the thread (Wait) to avoid UI Blocking.
                // The thread cleans up itself.
            }
        }

        /*
        private void OnNewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            // Removed Old Handler
        }
        */
        
        private void LoadCalibration()
        {
            // Load from file (json)
            // TODO: Implement persistence
        }

        public void SaveCalibration()
        {
             // Save to file
             // TODO: Implement persistence
        }

        public Dictionary<int, PointF[]> DetectMarkers(Bitmap bmp)
        {
            var result = new Dictionary<int, PointF[]>();
            /* ArUco detection temporarily disabled due to OpenCvSharp namespace issues.
            try
            {
                // Convert Bitmap to Mat
                using var mat = BitmapConverter.ToMat(bmp);
                using var gray = new Mat();
                Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY);

                // Define ArUco Dictionary (standard 4x4_50 or user configurable?)
                // Let's use DICT_4X4_50 as a default standard
                // using var dictionary = CvAruco.GetPredefinedDictionary(PredefinedDictionaryName.Dict4X4_50);
                // using var detectorParameters = DetectorParameters.Create();
                
                // CvAruco.DetectMarkers(gray, dictionary, out var corners, out var ids, detectorParameters, out var rejected);
                
                // if (ids != null && ids.Length > 0)
                // {
                //    for (int i = 0; i < ids.Length; i++)
                //    {
                //        var id = ids[i];
                //        var cornerPoints = corners[i]; // Point2f[]
                //        
                //        // Convert to standard PointF array
                //        var points = new PointF[4];
                //        for(int j=0; j<4; j++)
                //        {
                //            points[j] = new PointF(cornerPoints[j].X, cornerPoints[j].Y);
                //        }
                //        result[id] = points;
                //    }
                // }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ArUco Error: {ex.Message}");
            }
            */
            System.Diagnostics.Debug.WriteLine("ArUco Detection Not Implemented yet.");
            return result;
        }

        public void ComputeHomography(PointF[] imagePoints, PointF[] worldPoints)
        {
            if (imagePoints.Length != 4 || worldPoints.Length != 4) return;

            try
            {
                // Convert to Point2f for OpenCV
                var src = new Point2f[4];
                var dst = new Point2f[4];
                for(int i=0; i<4; i++)
                {
                    src[i] = new Point2f(imagePoints[i].X, imagePoints[i].Y);
                    dst[i] = new Point2f(worldPoints[i].X, worldPoints[i].Y); 
                }

                using var mat = Cv2.FindHomography(InputArray.Create(src), InputArray.Create(dst));
                if (!mat.Empty())
                {
                     // Save Homography to CalibrationData
                     // Current CalibrationData implementation?
                     // We need to persist this.
                     
                     // For now, let's extract the basic transform (Scale/Offset/Rotation) if possible, 
                     // OR just use the 4 point Warp if we support it in rendering.
                     // The current Renderer supports simple Affine (Translate/Scale). Homography allows perspective.
                     
                     // If the camera is 90 degrees top down, Affine is enough.
                     // A full Homography needs a shader or Mesh warp in rendering.
                     // Or GDI+ Warp? GDI+ DrawImage supports 3 points (Parallelogram).
                     // Homography is 4 points (Perspective).
                     
                     // If we assume a parallelogram (affine equivalent), we can take top-left, top-right, bottom-left.
                     // Let's assume the user clicks 4 points forming a rect.
                }
            }
            catch (Exception ex)
            {
                 System.Diagnostics.Debug.WriteLine($"Homography Error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            StopCamera();
        }

        public void StartScan()
        {
            // TODO: Implement Scanning Logic
            // 1. Calculate Grid
            // 2. Create Job
            // 3. Run Job
            System.Windows.Forms.MessageBox.Show("Scanning Logic Not Implemented Yet");
        }
    }
}
