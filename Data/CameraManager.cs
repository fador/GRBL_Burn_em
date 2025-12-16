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
using OpenCvSharp.Aruco;

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



                // Define ArUco Dictionary (standard 4x4_50 or user configurable?)
               // Calibration State
        private List<Point2f[][]> _allCorners = new List<Point2f[][]>();
        private List<int[]> _allIds = new List<int[]>();
        private OpenCvSharp.Size _imageSize;

        public void ResetCalibration()
        {
            _allCorners.Clear();
            _allIds.Clear();
            // _imageSize set on first frame
        }

        public bool AddCalibrationFrame(Mat frame)
        {
            if (_imageSize == new OpenCvSharp.Size(0, 0)) _imageSize = frame.Size();
            
            DetectArucoMarkers(frame, out var corners, out var ids);
            if (ids == null || ids.Length == 0) return false;

            _allCorners.Add(corners);
            _allIds.Add(ids);
            return true;
        }

        public double CalibrateCameraAruco()
        {
             if (_allCorners.Count < 5) return -1; 
             
             // TODO: Fix OpenCvSharp ArUco Calibration bindings
             // Currently CalibrateCameraAruco / GridBoard seems missing or moved.
             // We need to implement manual object point generation and use Cv2.CalibrateCamera.
             
             /*
             var dictionary = CvAruco.GetPredefinedDictionary(PredefinedDictionaryName.Dict4X4_50);
             using var board = GridBoard.Create(5, 7, 0.04f, 0.01f, dictionary);
             double error = CvAruco.CalibrateCameraAruco(
                _allCorners.ToArray(),
                _allIds.ToArray(),
                board,
                _imageSize,
                // ...
             );
             */
             
             System.Diagnostics.Debug.WriteLine("Calibration Logic Temporarily Disabled due to API change.");
             return 0;
        }

        public void DetectArucoMarkers(Mat frame, out Point2f[][] corners, out int[] ids)
        {
            var dictionary = CvAruco.GetPredefinedDictionary(PredefinedDictionaryName.Dict4X4_50);
            var parameters = new DetectorParameters();
            CvAruco.DetectMarkers(frame, dictionary, out corners, out ids, parameters, out var rejected);
        }

        public void StartScan()
        {
             // Start the GridCaptureJob
             var workW = AppConfiguration.Instance.WorkAreaWidth;
             var workH = AppConfiguration.Instance.WorkAreaHeight;
             var job = new GridCaptureJob();
             // Fire and forget? Or track?
             Task.Run(() => job.Start(workW, workH));
        }

        public void CaptureCurrentFrame(float worldX, float worldY, float width, float height)
        {
            // We need the *latest* frame. 
            // Since we are in a CaptureLoop, we might want to grab the last processed frame?
            // Or wait for the next one?
            // Let's assume we can grab the current frame from a property if we store it?
            // Currently FrameReceived sends it out.
            // Let's modify CaptureLoop to assume reliable stream.
            
            // Better: We subscribe to FrameReceived, get one frame, then unsubscribe.
            
            var tcs = new TaskCompletionSource<Bitmap>();
            Action<Bitmap> handler = null;
            handler = (bmp) => 
            {
                tcs.TrySetResult(new Bitmap(bmp));
                FrameReceived -= handler;
            };
            
            FrameReceived += handler;
            
            if (tcs.Task.Wait(1000))
            {
                 var img = tcs.Task.Result;
                 
                 // Undistort if possible?
                 // Convert Bitmap to Mat?
                 // If we have calibration, we should convert, undistort, convert back.
                 // This is heavy.
                 
                 Mat mat = BitmapConverter.ToMat(img);
                 Mat undistorted = UndistortFrame(mat);
                 Bitmap finalImg = BitmapConverter.ToBitmap(undistorted);
                 
                 mat.Dispose();
                 undistorted.Dispose();
                 img.Dispose();
                 
                 lock(_framesLock)
                 {
                     CapturedFrames.Add(new CapturedFrame(finalImg, worldX, worldY, width, height));
                 }
                 finalImg.Dispose();
            }
            else
            {
                FrameReceived -= handler; // Timeout
            }
        }

        public Mat UndistortFrame(Mat frame)
        {
            if (Calibration.CameraMatrix == null || Calibration.DistCoeffs == null || Calibration.CameraMatrix.Length != 9 || Calibration.DistCoeffs.Length != 5)
                return frame;
                
            // Check if matrix is identity (uncalibrated)
            if (Calibration.CameraMatrix[0] == 0) return frame;

            var camMatrix = new double[3, 3];
            for(int i=0; i<3; i++)
                for(int j=0; j<3; j++)
                    camMatrix[i,j] = Calibration.CameraMatrix[i*3 + j];
            
            var distCoeffs = Calibration.DistCoeffs;
            
            var result = new Mat();
            // We can optimize this by computing maps once (InitUndistortRectifyMap) if size doesn't change.
            // For now, simple Undistort.
            Cv2.Undistort(frame, result, InputArray.Create(camMatrix), InputArray.Create(distCoeffs));
            
            return result;
        }        
                // if (ids != null && ids.Length > 0)
                // {
                //    for (int i = 0; i < ids.Length; i++)
                //    {
                //        var id = ids[i];
                // using var dictionary = CvAruco.GetPredefinedDictionary(PredefinedDictionaryName.Dict4X4_50);
                // using var detectorParameters = DetectorParameters.Create();
                
                // CvAruco.DetectMarkers(gray, dictionary, out var corners, out var ids, detectorParameters, out var rejected);
                



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


    }
}
