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
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Reflection;

namespace Controllers
{
    class Program_FaultInjection
    {
        static Recorder _rec;
        private sealed class CsvSignal
        {
            public string Name { get; }
            public Func<string> ReadValue { get; }

            public CsvSignal(string name, Func<string> readValue)
            {
                Name = name;
                ReadValue = readValue;
            }
        }

        /// <summary>
        /// Cycle time in milliseconds.
        /// </summary>
        public const int CycleTime = 8;
        public const int CsvLogIntervalExecutions = 10;

        // Naming schema
        static string sceneName = "PickPlaceXYZ";
        static string manipulationRoot = $@"D:\Code\factoryio-sdk-master\factoryio-sdk-master\samples\Controllers\Manipulations";
        static string videoRoot = $@"D:\Code\factoryio-sdk-master\factoryio-sdk-master\samples\Controllers\Videos\FaultInjection";
        static string screenshotRoot = @"D:\Code\factoryio-sdk-master\factoryio-sdk-master\samples\Controllers\Videos\images\position";
        static string csvRoot = @"D:\Code\factoryio-sdk-master\factoryio-sdk-master\samples\Controllers\Logs\FaultInjection";

        /// <summary>
        /// The idea of this sample is to demonstrate that Microsoft Visual Studio can be used as a soft PLC to
        /// control FACTORY I/O (requires Ultimate Edition).
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args)
        {
            string manipulationFolder = Path.Combine(manipulationRoot, sceneName);
            string videoFolder = Path.Combine(videoRoot, sceneName);
            string screenshotFolder = Path.Combine(screenshotRoot, sceneName);
            string csvFolder = Path.Combine(csvRoot, sceneName);

            string[] manipulationFiles = Directory.GetFiles(manipulationFolder, "*.cs");
            System.Diagnostics.Debug.WriteLine($"Found {manipulationFiles.Length} manipulation classes.");

            //Stopwatch used to measure elapsed time between cycles
            Stopwatch stopwatch = new Stopwatch();

            //MemoryBit used to switch FACTORY I/O between edit and run mode
            MemoryBit start = MemoryMap.Instance.GetBit(MemoryMap.BitCount - 16, MemoryType.Output);

            //MemoryBit used to detect if FACTORY I/O is edit or run mode
            MemoryBit running = MemoryMap.Instance.GetBit(MemoryMap.BitCount - 16, MemoryType.Input);

            foreach (var manipulationFile in manipulationFiles)
            {
                string name = Path.GetFileNameWithoutExtension(manipulationFile);
                string videoPath = Path.Combine(videoFolder, $"{name}.mp4");
                if (File.Exists(videoPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[SKIP] Video already exists for {name}, skipping controller.");
                    continue;
                }

                string currentScreenshotFolder = Path.Combine(screenshotFolder, name);
                Directory.CreateDirectory(currentScreenshotFolder);
                Directory.CreateDirectory(csvFolder);

                string fullClassName = $"Controllers.{name}";
                Type controllerType = Type.GetType(fullClassName);
                string csvPath = Path.Combine(csvFolder, $"{name}.csv");

                CreateRecording(videoFolder, name);

                //Forcing a rising edge on the start MemoryBit so FACTORY I/O can detect it
                SwitchToRun(start);

                Controller controller = (Controller)Activator.CreateInstance(controllerType);
                var loggedSignals = DiscoverLoggedSignals(manipulationFile, controller);
                System.Diagnostics.Debug.WriteLine(string.Format("Running controller: {0}", controller.GetType().Name));

                stopwatch.Start();

                Thread.Sleep(CycleTime);

                int executionCount = 0;
                int screenshotCount = 0;
                Stopwatch runStopwatch = Stopwatch.StartNew();

                using (StreamWriter csvWriter = new StreamWriter(csvPath, false))
                {
                    WriteCsvHeader(csvWriter, loggedSignals);

                    while (!controller.stopSignal)
                    {
                        //Update the memory map before executing the controller
                        MemoryMap.Instance.Update();

                        if (running.Value)
                        {
                            stopwatch.Stop();

                            controller.executionCount = executionCount;

                            controller.Execute((int)stopwatch.ElapsedMilliseconds);

                            if (executionCount % CsvLogIntervalExecutions == 0)
                            {
                                WriteCsvRow(csvWriter, runStopwatch.ElapsedMilliseconds, executionCount, loggedSignals);
                            }

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

                    WriteCsvRow(csvWriter, runStopwatch.ElapsedMilliseconds, executionCount, loggedSignals);
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
        static List<CsvSignal> DiscoverLoggedSignals(string controllerSourcePath, Controller controller)
        {
            List<CsvSignal> signals = new List<CsvSignal>();

            string source = File.ReadAllText(controllerSourcePath);
            Regex signalPattern = new Regex(
                @"Memory(?<signalType>Bit|Float)\s+(?<fieldName>\w+)\s*=\s*MemoryMap\.Instance\.Get(?<getterType>Bit|Float)\(""(?<memoryName>[^""]+)"",\s*MemoryType\.(?<memoryType>Input|Output)\);");
            HashSet<string> seenNames = new HashSet<string>(signals.Select(signal => signal.Name));

            foreach (Match match in signalPattern.Matches(source))
            {
                string signalType = match.Groups["signalType"].Value;
                string getterType = match.Groups["getterType"].Value;
                if (signalType != getterType)
                    continue;

                string fieldName = match.Groups["fieldName"].Value;
                if (!seenNames.Add(fieldName))
                    continue;

                string memoryName = match.Groups["memoryName"].Value;
                MemoryType memoryType = (MemoryType)Enum.Parse(typeof(MemoryType), match.Groups["memoryType"].Value);

                if (signalType == "Bit")
                    TryAddMemoryBit(signals, fieldName, memoryName, memoryType);
                else
                    TryAddMemoryFloat(signals, fieldName, memoryName, memoryType);
            }

            AddControllerFieldSignal<int>(signals, controller, "counter", value => value.ToString(CultureInfo.InvariantCulture));

            return signals;
        }

        static void AddControllerFieldSignal<T>(List<CsvSignal> signals, Controller controller, string fieldName, Func<T, string> formatValue)
        {
            FieldInfo field = controller.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(T))
                return;

            signals.Add(new CsvSignal(fieldName, () => formatValue((T)field.GetValue(controller))));
        }

        static void AddBitSignal(List<CsvSignal> signals, string csvName, MemoryBit bit)
        {
            signals.Add(new CsvSignal(csvName, () => bit.Value ? "1" : "0"));
        }

        static void AddFloatSignal(List<CsvSignal> signals, string csvName, MemoryFloat memoryFloat)
        {
            signals.Add(new CsvSignal(csvName, () => memoryFloat.Value.ToString("G", CultureInfo.InvariantCulture)));
        }

        static void TryAddMemoryBit(List<CsvSignal> signals, string csvName, string memoryName, MemoryType memoryType)
        {
            try
            {
                MemoryBit bit = MemoryMap.Instance.GetBit(memoryName, memoryType);
                if (bit == null)
                {
                    Debug.WriteLine($"Skipping CSV log bit '{memoryName}': MemoryMap returned null.");
                    return;
                }

                AddBitSignal(signals, csvName, bit);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Skipping CSV log bit '{memoryName}': {ex.Message}");
            }
        }

        static void TryAddMemoryFloat(List<CsvSignal> signals, string csvName, string memoryName, MemoryType memoryType)
        {
            try
            {
                MemoryFloat memoryFloat = MemoryMap.Instance.GetFloat(memoryName, memoryType);
                if (memoryFloat == null)
                {
                    Debug.WriteLine($"Skipping CSV log float '{memoryName}': MemoryMap returned null.");
                    return;
                }

                AddFloatSignal(signals, csvName, memoryFloat);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Skipping CSV log float '{memoryName}': {ex.Message}");
            }
        }

        static void WriteCsvHeader(StreamWriter writer, List<CsvSignal> signals)
        {
            string header = "timestamp_ms,execution_count," + string.Join(",", signals.Select(signal => EscapeCsv(signal.Name)));
            writer.WriteLine(header);
            writer.Flush();
        }

        static void WriteCsvRow(StreamWriter writer, long elapsedMs, int executionCount, List<CsvSignal> signals)
        {
            string[] values = signals
                .Select(signal => EscapeCsv(signal.ReadValue()))
                .ToArray();

            writer.WriteLine($"{elapsedMs},{executionCount},{string.Join(",", values)}");
            writer.Flush();
        }

        static string EscapeCsv(string value)
        {
            if (value.Contains(",") || value.Contains("\""))
                return $"\"{value.Replace("\"", "\"\"")}\"";

            return value;
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
