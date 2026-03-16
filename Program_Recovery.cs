//-----------------------------------------------------------------------------
// FACTORY I/O (SDK)
//
// Copyright (C) Real Games. All rights reserved.
//-----------------------------------------------------------------------------

using System;
using System.Threading;
using System.Diagnostics;

using ScreenRecorderLib;

using EngineIO;
using System.IO;
using System.Linq;

namespace Controllers
{
    class Program_Recovery
    {
        static Recorder _rec;

        /// <summary>
        /// Cycle time in milliseconds.
        /// </summary>
        public const int CycleTime = 8;

        // Naming schema
        static string sceneName = "PickPlaceXYZ";
        static string caseTest = "test"; // optional
        static string manipulationRoot = @"D:\Code\factoryio-sdk-master\factoryio-sdk-master\samples\Controllers\Manipulations";
        static string videoRoot = @"D:\Code\factoryio-sdk-master\factoryio-sdk-master\samples\Controllers\Videos\Recovery";

        /// <summary>
        /// The idea of this sample is to demonstrate that Microsoft Visual Studio can be used as a soft PLC to
        /// control FACTORY I/O (requires Ultimate Edition).
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args)
        {
            string manipulationFolder = BuildPath(manipulationRoot, sceneName, caseTest);
            string videoFolder = BuildPath(videoRoot, sceneName, caseTest);

            string[] manipulationFiles = Directory.GetFiles(manipulationFolder, "*.cs");
            var classNames = manipulationFiles
                .Select(Path.GetFileNameWithoutExtension)
                .ToList();

            Debug.WriteLine($"Found {classNames.Count} manipulation classes.");

            foreach (var name in classNames)
            {
                Stopwatch stopwatch = new Stopwatch();
                MemoryBit start = MemoryMap.Instance.GetBit(MemoryMap.BitCount - 16, MemoryType.Output);
                MemoryBit pause = MemoryMap.Instance.GetBit(MemoryMap.BitCount - 48, MemoryType.Output);
                MemoryBit running = MemoryMap.Instance.GetBit(MemoryMap.BitCount - 16, MemoryType.Input);

                string videoPath = Path.Combine(videoFolder, $"{name}.mp4");
                if (File.Exists(videoPath))
                {
                    Debug.WriteLine($"[SKIP] Video already exists for {name}");
                    continue;
                }

                SwitchToRun(start);

                string fullClassName = $"Controllers.{name}";
                Type controllerType = Type.GetType(fullClassName);
                Controller controller = (Controller)Activator.CreateInstance(controllerType);

                string controllerName = controller.GetType().Name;
                Debug.WriteLine($"Running controller: {controllerName}");

                CreateRecording(videoFolder, controllerName);

                stopwatch.Start();
                Thread.Sleep(CycleTime);

                int executionCount = 0;

                while (!controller.stopSignal)
                {
                    MemoryMap.Instance.Update();

                    if (running.Value)
                    {
                        stopwatch.Stop();
                        controller.Execute((int)stopwatch.ElapsedMilliseconds);
                        executionCount++;
                        stopwatch.Restart();
                    }

                    if (executionCount == 6000)
                    {
                        Debug.WriteLine("Pauing scene...");
                        pause.Value = true;
                        MemoryMap.Instance.Update();
                        Thread.Sleep(500);

                        Debug.WriteLine("Switching controller...");
                        string recoveryControllerName = $"Controllers.Recovery_{controllerName}";
                        Type recoveryType = Type.GetType(recoveryControllerName);
                        controller = (Controller)Activator.CreateInstance(recoveryType);

                        Debug.WriteLine("Resuming scene...");
                        pause.Value = false;
                        MemoryMap.Instance.Update();
                        Thread.Sleep(500);
                    }
                    
                    Thread.Sleep(CycleTime);
                }

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
            {
                return Path.Combine(root, scene);
            }
            else
            {
                return Path.Combine(root, scene, caseTest);
            }
        }
    }
}
