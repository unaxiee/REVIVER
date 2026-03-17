//-----------------------------------------------------------------------------
// FACTORY I/O (SDK)
//
// Copyright (C) Real Games. All rights reserved.
//-----------------------------------------------------------------------------

using System;
using System.Threading;
using System.Diagnostics;

using ScreenRecorderLib;

using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

using EngineIO;
using System.IO;
using System.Linq;

namespace Controllers
{
    class Program_FaultInjection
    {
        static Recorder _rec;

        /// <summary>
        /// Cycle time in milliseconds.
        /// </summary>
        public const int CycleTime = 8;

        // Naming schema
        static string sceneName = "PickPlaceXYZ";
        static string caseTest = "test";
        //static string caseTest = null;  // set to null if no subfolder
        static string manipulationRoot = $@"D:\Code\factoryio-sdk-master\factoryio-sdk-master\samples\Controllers\Manipulations";
        static string videoRoot = $@"D:\Code\factoryio-sdk-master\factoryio-sdk-master\samples\Controllers\Videos\FaultInjection";
        static string screenshotRoot = @"D:\Code\factoryio-sdk-master\factoryio-sdk-master\samples\Controllers\Videos\images\position";

        /// <summary>
        /// The idea of this sample is to demonstrate that Microsoft Visual Studio can be used as a soft PLC to
        /// control FACTORY I/O (requires Ultimate Edition).
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args)
        {
            string manipulationFolder = BuildPath(manipulationRoot, sceneName, caseTest);
            string videoFolder = BuildPath(videoRoot, sceneName, caseTest);
            string screenshotFolder = BuildPath(screenshotRoot, sceneName, caseTest);

            string[] manipulationFiles = Directory.GetFiles(manipulationFolder, "*.cs");
            var classNames = manipulationFiles
                .Select(Path.GetFileNameWithoutExtension)
                .ToList();
            System.Diagnostics.Debug.WriteLine($"Found {classNames.Count} manipulation classes.");

            //Stopwatch used to measure elapsed time between cycles
            Stopwatch stopwatch = new Stopwatch();

            //MemoryBit used to switch FACTORY I/O between edit and run mode
            MemoryBit start = MemoryMap.Instance.GetBit(MemoryMap.BitCount - 16, MemoryType.Output);

            //MemoryBit used to detect if FACTORY I/O is edit or run mode
            MemoryBit running = MemoryMap.Instance.GetBit(MemoryMap.BitCount - 16, MemoryType.Input);

            foreach (var name in classNames)
            {
                string videoPath = Path.Combine(videoFolder, $"{name}.mp4");
                if (File.Exists(videoPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[SKIP] Video already exists for {name}, skipping controller.");
                    continue;
                }

                string currentScreenshotFolder = Path.Combine(screenshotFolder, name);
                Directory.CreateDirectory(currentScreenshotFolder);

                string fullClassName = $"Controllers.{name}";
                Type controllerType = Type.GetType(fullClassName);

                CreateRecording(videoFolder, name);

                //Forcing a rising edge on the start MemoryBit so FACTORY I/O can detect it
                SwitchToRun(start);

                Controller controller = (Controller)Activator.CreateInstance(controllerType);
                System.Diagnostics.Debug.WriteLine(string.Format("Running controller: {0}", controller.GetType().Name));

                stopwatch.Start();

                Thread.Sleep(CycleTime);

                int executionCount = 0;
                int screenshotCount = 0;

                while (!controller.stopSignal)
                {
                    //Update the memory map before executing the controller
                    MemoryMap.Instance.Update();

                    if (running.Value)
                    {
                        stopwatch.Stop();

                        controller.executionCount = executionCount;

                        controller.Execute((int)stopwatch.ElapsedMilliseconds);

                        if (controller.captureSignal)
                        {
                            CaptureScreenshot(currentScreenshotFolder, name, screenshotCount);
                            screenshotCount++;
                            controller.captureSignal = false;
                        }

                        executionCount++;

                        stopwatch.Restart();
                    }

                    Thread.Sleep(CycleTime);

                    if (executionCount == 4000)
                        break;
                }

                System.Diagnostics.Debug.WriteLine($"Executed {executionCount} times");

                Shutdown(start);

                EndRecording();
            }

            MemoryMap.Instance.Dispose();
        }

        static void SwitchToRun(MemoryBit start)
        {
            start.Value = false;
            MemoryMap.Instance.Update();
            Thread.Sleep(500);

            start.Value = true;
            MemoryMap.Instance.Update();
            Thread.Sleep(500);
        }

        static void Shutdown(MemoryBit start)
        {
            start.Value = false;

            MemoryMap.Instance.Update();
        }

        static void CreateRecording(string videoFolder, string videoName)
        {
            string videoPath = Path.Combine(videoFolder, $"{videoName}.mp4");
            _rec = Recorder.CreateRecorder();
            _rec.OnRecordingComplete += Rec_OnRecordingComplete;
            _rec.OnRecordingFailed += Rec_OnRecordingFailed;
            _rec.OnStatusChanged += Rec_OnStatusChanged;
            System.Diagnostics.Debug.WriteLine("Recording started...");
            _rec.Record(videoPath);
        }

        static void EndRecording()
        {
            System.Diagnostics.Debug.WriteLine("Stopping recording...");
            _rec.Stop();
            Thread.Sleep(500);
        }

        private static void Rec_OnRecordingComplete(object sender, RecordingCompleteEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"Recording complete: {e.FilePath}");
        }

        private static void Rec_OnRecordingFailed(object sender, RecordingFailedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"Recording failed: {e.Error}");
        }

        private static void Rec_OnStatusChanged(object sender, RecordingStatusEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"Recording status: {e.Status}");
        }
        static string BuildPath(string root, string scene, string caseTest)
        {
            if (string.IsNullOrEmpty(caseTest))
                return Path.Combine(root, scene);
            else
                return Path.Combine(root, scene, caseTest);
        }

        static void CaptureScreenshot(string folder, string name, int screenshotCount)
        {
            Rectangle bounds = Screen.PrimaryScreen.Bounds;
            string fileName = $"{name}_shot{screenshotCount:D2}.png";
            string filePath = Path.Combine(folder, fileName);

            using (Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                }
                bitmap.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
            }
            Debug.WriteLine($"Screenshot saved: {filePath}");
        }
    }
}
