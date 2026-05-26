//-----------------------------------------------------------------------------
// FACTORY I/O (SDK)
//
// Copyright (C) Real Games. All rights reserved.
//-----------------------------------------------------------------------------

using System;
using System.Threading;
using System.Diagnostics;

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
        static string csvRoot = @"D:\Code\factoryio-sdk-master\factoryio-sdk-master\samples\Controllers\Logs\FaultInjection";

        /// <summary>
        /// The idea of this sample is to demonstrate that Microsoft Visual Studio can be used as a soft PLC to
        /// control FACTORY I/O (requires Ultimate Edition).
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args)
        {
            string sceneManipulationFolder = Path.Combine(manipulationRoot, sceneName);
            string csvFolder = Path.Combine(csvRoot, sceneName);

            string[] manipulationFiles = GetFaultInjectionManipulationFiles(sceneManipulationFolder);
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
                string relativeFolder = Path.GetDirectoryName(Path.GetRelativePath(sceneManipulationFolder, manipulationFile));
                string currentCsvFolder = Path.Combine(csvFolder, relativeFolder);
                Directory.CreateDirectory(currentCsvFolder);

                string fullClassName = $"Controllers.{name}";
                Type controllerType = Type.GetType(fullClassName);
                string csvPath = Path.Combine(currentCsvFolder, $"{name}.csv");

                if (controllerType == null)
                {
                    Debug.WriteLine($"[SKIP] Could not find controller type {fullClassName}.");
                    continue;
                }

                //Forcing a rising edge on the start MemoryBit so FACTORY I/O can detect it
                SwitchToRun(start);

                Controller controller = (Controller)Activator.CreateInstance(controllerType);
                var loggedSignals = DiscoverLoggedSignals(manipulationFile, controller);
                System.Diagnostics.Debug.WriteLine(string.Format("Running controller: {0}", controller.GetType().Name));

                stopwatch.Start();

                Thread.Sleep(CycleTime);

                int executionCount = 0;
                Stopwatch runStopwatch = Stopwatch.StartNew();
                StreamWriter csvWriter = null;

                try
                {
                    csvWriter = new StreamWriter(csvPath, false);
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
                finally
                {
                    csvWriter?.Dispose();
                }

                System.Diagnostics.Debug.WriteLine($"Executed {executionCount} times");

                Shutdown(start);
            }

            MemoryMap.Instance.Dispose();
        }

        static string[] GetFaultInjectionManipulationFiles(string sceneManipulationFolder)
        {
            string palletFolder = Path.Combine(sceneManipulationFolder, "pallet");
            if (!Directory.Exists(palletFolder))
                return new string[0];

            return Directory.GetFiles(palletFolder, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => Path.GetRelativePath(sceneManipulationFolder, path), StringComparer.OrdinalIgnoreCase)
                .ToArray();
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

    }
}
