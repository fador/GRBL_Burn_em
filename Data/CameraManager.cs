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
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Net.Sockets;
using System.IO;

using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Core;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

using System;

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
        
        public CalibrationStore CalibrationStore { get; private set; } = CalibrationStore.Load();
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
                var list = _deviceInfos.Select(d => d.Name).ToList();
                list.Add("Network Camera (Emulator)"); 
                return list;
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

            if (_deviceInfos == null) return;
            
            // Check if Network Camera (Index out of range of _deviceInfos)
            if (deviceIndex == _deviceInfos.Count)
            {
                 await ConnectToEmulatorAsync("127.0.0.1", 2346);
                 return;
            }
            
            if (deviceIndex < 0 || deviceIndex >= _deviceInfos.Count)
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

        private async Task ConnectToEmulatorAsync(string host, int port)
        {
             _isRunning = true;
             await Task.Run(async () => 
             {
                 while (_isRunning)
                 {
                     try
                     {
                         using (var client = new TcpClient())
                         {
                             await client.ConnectAsync(host, port);
                             using (var stream = client.GetStream())
                             using (var writer = new BinaryWriter(stream))
                             using (var reader = new BinaryReader(stream))
                             {
                                 while(_isRunning && client.Connected)
                                 {
                                     // Request Frame
                                     writer.Write((byte)'S');
                                     writer.Flush();
                                     
                                     // Read Length
                                     int len = reader.ReadInt32();
                                     if (len > 0)
                                     {
                                         byte[] data = reader.ReadBytes(len);
                                         using (var ms = new MemoryStream(data))
                                         {
                                              Bitmap bmp = new Bitmap(ms);
                                              FrameReceived?.Invoke(bmp);
                                         }
                                     }
                                     
                                     await Task.Delay(33); // ~30 FPS
                                 }
                             }
                         }
                     }
                     catch (Exception ex)
                     {
                         System.Diagnostics.Debug.WriteLine($"NetCam Error: {ex.Message}");
                         await Task.Delay(1000); // Retry delay
                     }
                 }
             });
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
            CalibrationStore = CalibrationStore.Load();
        }

        public void ResetCalibration()
        {
            CalibrationStore = new CalibrationStore();
        }

        public void SaveCalibration()
        {
            CalibrationStore.Save();
        }

        // --- Camera I/O ---

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

                 Bitmap frame = UndistortFrame(img);

                 lock(_framesLock)
                 {
                     CapturedFrames.Add(new CapturedFrame(frame, worldX, worldY, width, height));
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
            var intrinsics = CalibrationStore.Intrinsics;
            if (intrinsics == null || !intrinsics.IsValid)
                return new Bitmap(frame);

            try
            {
                using var mat = CameraCalibrationEngine.BitmapToMat(frame);
                var engine = new CameraCalibrationEngine(CalibrationStore.BoardConfig ?? new CharucoBoardConfig());
                using var undistorted = engine.UndistortImage(mat, intrinsics);
                return CameraCalibrationEngine.MatToBitmap(undistorted);
            }
            catch
            {
                return new Bitmap(frame);
            }
        }

        public void Dispose()
        {
            StopCameraAsync().Wait();
        }
    }
}
