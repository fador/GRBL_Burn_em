using System;
using System.Text.Json;
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

        public double CalibrateCameraDots(List<Mat> frames, int rows, int cols, float spacingMm, CalibrationPatternType type, out double[] cameraMatrix, out double[] distCoeffs)
        {
            cameraMatrix = new double[9];
            distCoeffs = new double[5];

            if (frames.Count == 0 || frames[0].Rows == 0) return -1;
            
            var objectPoints = new List<Point3f[]>();
            var imagePoints = new List<Point2f[]>();
            var size = new OpenCvSharp.Size(frames[0].Width, frames[0].Height);

            // Generate Object Points (Real World 3D Coords of the Pattern)
            // Z = 0
            var obj = new List<Point3f>();
            
            if (type == CalibrationPatternType.AsymmetricCircles)
            {
                for (int i = 0; i < rows; i++)
                {
                    for (int j = 0; j < cols; j++)
                    {
                        // Asymmetric Circle Grid Logic
                        // https://docs.opencv.org/4.x/d9/d0c/group__calib3d.html#gad1205c4b8de3b5bc7ed8be5e1938f7ee
                        // For asymmetric circle grid, the centers are:
                        // (2*j + i%2)*spacing, i*spacing
                         obj.Add(new Point3f((2 * j + i % 2) * spacingMm, i * spacingMm, 0));
                    }
                }
            }
            else
            {
                // Symmetric
                for (int i = 0; i < rows; i++)
                {
                    for (int j = 0; j < cols; j++)
                    {
                        obj.Add(new Point3f(j * spacingMm, i * spacingMm, 0));
                    }
                }
            }
            var objArray = obj.ToArray();

            // Detect in each frame
            var patternSize = new OpenCvSharp.Size(cols, rows);
            
            // Flags
            var flags = CalibrationFlags.None; 
            // Often helpful: CalibrationFlags.RationalModel | CalibrationFlags.ThinPrismModel if high distortion.
            // For standard lens: None or FixK3.
            
            foreach(var frame in frames)
            {
                var corners = DetectDotPattern(frame, debugDraw: null, rows, cols, type);
                if (corners != null && corners.Length == rows * cols)
                {
                    imagePoints.Add(corners);
                    objectPoints.Add(objArray);
                }
            }

            if (imagePoints.Count < 5) return -2; // Not enough valid frames

            using var camMat = Mat.Eye(3, 3, MatType.CV_64FC1).ToMat();
            using var dist = Mat.Zeros(5, 1, MatType.CV_64FC1).ToMat();
            
            Mat[] rvecs;
            Mat[] tvecs;
            
            // Convert points to Mats
            var objectPointsMats = objectPoints.Select(p => Mat.FromPixelData(p.Length, 1, MatType.CV_32FC3, p)).ToList();
            var imagePointsMats = imagePoints.Select(p => Mat.FromPixelData(p.Length, 1, MatType.CV_32FC2, p)).ToList();

            double error = Cv2.CalibrateCamera(
                objectPointsMats,
                imagePointsMats,
                size,
                camMat,
                dist,
                out rvecs,
                out tvecs,
                flags
            );

            // Dispose temporary Mats
            foreach (var m in objectPointsMats) m.Dispose();
            foreach (var m in imagePointsMats) m.Dispose();

            // Output
            for(int i=0; i<3;i++)
                for(int j=0; j<3; j++)
                    cameraMatrix[i*3+j] = camMat.At<double>(i, j);
            
            for(int i=0; i<5; i++)
                distCoeffs[i] = dist.At<double>(i, 0);

            // Cleanup Mats
            if (rvecs != null) foreach(var m in rvecs) m.Dispose();
            if (tvecs != null) foreach(var m in tvecs) m.Dispose();

            return error;
        }

        public Point2f[]? DetectDotPattern(Mat frame, Mat? debugDraw, int rows, int cols, CalibrationPatternType type)
        {
             var patternSize = new OpenCvSharp.Size(cols, rows);
             Point2f[] corners;
             
             var flags = FindCirclesGridFlags.SymmetricGrid;
             if (type == CalibrationPatternType.AsymmetricCircles) flags = FindCirclesGridFlags.AsymmetricGrid;
             else if (type == CalibrationPatternType.Circles) flags = FindCirclesGridFlags.SymmetricGrid;
             else return null; // Chessboard not implemented here yet
             
             // Convert to gray? FindCirclesGrid handles color but gray is usually safer
             using var gray = frame.CvtColor(ColorConversionCodes.BGR2GRAY);
             
             // Using SimpleBlobDetector is implicitly done by FindCirclesGrid if not custom.
             // Sometimes we need to tweak blob detector params. 
             // Default is usually okay for clear black dots on white.
             
             bool found = Cv2.FindCirclesGrid(gray, patternSize, out corners, flags);
             
             if (found && debugDraw != null)
             {
                 Cv2.DrawChessboardCorners(debugDraw, patternSize, corners, found);
             }
             
             return found ? corners : null;
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
                // Casting check
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
            Action<Bitmap> handler = null!;
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

        public void ComputeHomography(PointF[] imagePoints, PointF[] worldPoints)
        {
            if (imagePoints.Length != 4 || worldPoints.Length != 4) return;

            try
            {
                 var h = Tools.CalibrationMath.ComputeHomography(imagePoints, worldPoints);
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
            return Tools.CalibrationMath.UndistortPoint(p, Calibration.CameraMatrix, Calibration.DistCoeffs);
        }

        public void Dispose()
        {
            StopCamera();
        }


    }
}
