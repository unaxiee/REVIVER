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
using ScreenRecorderLib;

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
        static string sceneName = "ProductionLine";
        public const int MaxExecutions = 10000;
        static readonly bool EnableCsvLogging = false;
        static readonly bool EnableRecording = true;
        static readonly bool RunAllManipulations = false;
        // Exact manipulation filenames, with or without .cs.
        // When nonempty, this list takes priority over variable/value selections.
        static readonly string[] SelectedManipulationFiles =
        {
            "ProductionLine_feedLidMaterial_true_to_false_L78.cs"
        };
        // Run every variant for these variables, across all values and source lines.
        // Leave empty to use the variable/value tuple below instead.
        static readonly string[] SelectedManipulatedVariables =
        {
            "feedLidMaterial"
        };
        // True replaces the variant's MP4; false preserves it and adds _1, _2, etc.
        static readonly bool OverwriteRecordings = true;

        // (variable, nominal value, manipulation); null means any value.
        // States: ("stateBases", State.State0, State.State31).
        // Inputs have no nominal state: ("ftPartLeaving", null, "neg").
        // Input manipulation accepts "neg", "true", "false" (or bool true/false).
        // Output negations: ("basesConveyor1", null, null) or ("basesConveyor1", null, "neg").
        // Float inputs: ("positionX", null, null) selects all; ("positionX", null, -1f) selects -1.
        // Float outputs: ("setX", 1.1f, null) selects every replacement of 1.1f.
        static readonly (string Variable, object Nominal, object Manipulated) SelectedManipulation =
            ("setX", 0f, null);
        // Optional source line restriction; null includes all matching lines.
        static readonly int? SelectedSourceLine = 132;
        static string manipulationRoot = $@"D:\Code\factoryio-sdk-master\factoryio-sdk-master\samples\Controllers\Manipulations";
        static string csvRoot = @"D:\Code\factoryio-sdk-master\factoryio-sdk-master\samples\Controllers\Logs\FaultInjection";
        static string videoRoot = @"D:\Code\factoryio-sdk-master\factoryio-sdk-master\samples\Controllers\Videos\FaultInjection";

        /// <summary>
        /// The idea of this sample is to demonstrate that Microsoft Visual Studio can be used as a soft PLC to
        /// control FACTORY I/O (requires Ultimate Edition).
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args)
        {
            string sceneManipulationFolder = Path.Combine(manipulationRoot, sceneName);
            // Fixed per-variant paths allow StreamWriter (append: false) to overwrite prior logs.
            string csvFolder = Path.Combine(csvRoot, sceneName);
            string videoFolder = Path.Combine(videoRoot, sceneName);

            string[] manipulationFiles = GetFaultInjectionManipulationFiles(sceneManipulationFolder);
            System.Diagnostics.Debug.WriteLine($"Found {manipulationFiles.Length} manipulation classes.");
            if (manipulationFiles.Length == 0)
            {
                string selection = RunAllManipulations ? "all variants"
                    : SelectedManipulationFiles.Length > 0
                    ? "files: " + string.Join(", ", SelectedManipulationFiles)
                    : SelectedManipulatedVariables.Length > 0
                    ? "variables: " + string.Join(", ", SelectedManipulatedVariables)
                    : $"{SelectedManipulation} at source line {SelectedSourceLine?.ToString() ?? "any"}";
                throw new InvalidOperationException($"No manipulations matched {selection} in {sceneManipulationFolder}.");
            }

            //Stopwatch used to measure elapsed time between cycles
            Stopwatch stopwatch = new Stopwatch();

            //MemoryBit used to switch FACTORY I/O between edit and run mode
            MemoryBit start = MemoryMap.Instance.GetBit(MemoryMap.BitCount - 16, MemoryType.Output);

            //MemoryBit used to detect if FACTORY I/O is edit or run mode
            MemoryBit running = MemoryMap.Instance.GetBit(MemoryMap.BitCount - 16, MemoryType.Input);

            try
            {
                foreach (var manipulationFile in manipulationFiles)
                {
                    string name = Path.GetFileNameWithoutExtension(manipulationFile);
                    string relativeFolder = Path.GetDirectoryName(Path.GetRelativePath(sceneManipulationFolder, manipulationFile));
                    string currentCsvFolder = Path.Combine(csvFolder, relativeFolder);
                    if (EnableCsvLogging)
                        Directory.CreateDirectory(currentCsvFolder);

                    string fullClassName = $"Controllers.{name}";
                    Type controllerType = Type.GetType(fullClassName);
                    string csvPath = Path.Combine(currentCsvFolder, $"{name}.csv");

                    if (controllerType == null)
                    {
                        Debug.WriteLine($"[SKIP] Could not find controller type {fullClassName}.");
                        continue;
                    }

                    string videoPath = null;
                    if (EnableRecording)
                    {
                        Directory.CreateDirectory(videoFolder);
                        videoPath = GetRecordingPath(videoFolder, name);
                        Debug.WriteLine($"Recording screen to {videoPath}");
                    }

                    Controller controller = (Controller)Activator.CreateInstance(controllerType);
                    var loggedSignals = EnableCsvLogging ? DiscoverLoggedSignals(manipulationFile, controller) : null;
                    System.Diagnostics.Debug.WriteLine(string.Format("Running controller: {0}", controller.GetType().Name));

                    stopwatch.Restart();

                    Thread.Sleep(CycleTime);

                    int executionCount = 0;
                    Stopwatch runStopwatch = Stopwatch.StartNew();
                    StreamWriter csvWriter = null;
                    using ScreenRecording recording = EnableRecording ? new ScreenRecording() : null;

                    try
                    {
                        recording?.Start(videoPath);
                        // Start recording before switching the scene to run mode.
                        SwitchToRun(start);
                        stopwatch.Restart();
                        runStopwatch.Restart();
                        if (EnableCsvLogging)
                        {
                            csvWriter = new StreamWriter(csvPath, false);
                            WriteCsvHeader(csvWriter, loggedSignals);
                        }

                        while (!controller.stopSignal)
                        {
                            recording?.ThrowIfFailed();
                            //Update the memory map before executing the controller
                            MemoryMap.Instance.Update();

                            if (running.Value)
                            {
                                stopwatch.Stop();

                                controller.executionCount = executionCount;

                                controller.Execute((int)stopwatch.ElapsedMilliseconds);

                                if (EnableCsvLogging && executionCount % CsvLogIntervalExecutions == 0)
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

                            if (executionCount >= MaxExecutions)
                                break;
                        }

                        if (EnableCsvLogging)
                            WriteCsvRow(csvWriter, runStopwatch.ElapsedMilliseconds, executionCount, loggedSignals);
                    }
                    finally
                    {
                        csvWriter?.Dispose();
                        Shutdown(start);
                    }

                    System.Diagnostics.Debug.WriteLine($"Executed {executionCount} times");

                }

            }
            finally
            {
                MemoryMap.Instance.Dispose();
            }
        }

        static string GetRecordingPath(string folder, string name)
        {
            string path = Path.Combine(folder, name + ".mp4");
            if (OverwriteRecordings)
            {
                if (File.Exists(path))
                    File.Delete(path);
                return path;
            }

            int suffix = 1;
            while (File.Exists(path))
                path = Path.Combine(folder, $"{name}_{suffix++}.mp4");
            return path;
        }

        // Stop() finalizes MP4 asynchronously; wait before disposing or starting another run.
        private sealed class ScreenRecording : IDisposable
        {
            readonly Recorder recorder;
            readonly ManualResetEventSlim started = new ManualResetEventSlim();
            readonly ManualResetEventSlim finished = new ManualResetEventSlim();
            string error;
            bool requested;

            public ScreenRecording()
            {
                recorder = Recorder.CreateRecorder(new RecorderOptions
                {
                    AudioOptions = new AudioOptions { IsAudioEnabled = false }
                });
                recorder.OnStatusChanged += (sender, e) =>
                {
                    if (e.Status == RecorderStatus.Recording)
                        started.Set();
                };
                recorder.OnRecordingComplete += (sender, e) => finished.Set();
                recorder.OnRecordingFailed += (sender, e) =>
                {
                    error = e.Error;
                    started.Set();
                    finished.Set();
                };
            }

            public void Start(string path)
            {
                requested = true;
                recorder.Record(path);
                if (!started.Wait(TimeSpan.FromSeconds(15)))
                    throw new TimeoutException("Screen recording did not start within 15 seconds.");
                ThrowIfFailed();
            }

            public void ThrowIfFailed()
            {
                string recordingError = Volatile.Read(ref error);
                if (recordingError != null)
                    throw new InvalidOperationException($"Screen recording failed: {recordingError}");
            }

            public void Dispose()
            {
                try
                {
                    if (requested && !finished.IsSet)
                    {
                        recorder.Stop();
                        if (!finished.Wait(TimeSpan.FromSeconds(30)))
                            throw new TimeoutException("Screen recording did not finish within 30 seconds.");
                    }
                    ThrowIfFailed();
                }
                finally
                {
                    recorder.Dispose();
                    started.Dispose();
                    finished.Dispose();
                }
            }
        }

        static string[] GetFaultInjectionManipulationFiles(string sceneManipulationFolder)
        {
            if (!Directory.Exists(sceneManipulationFolder))
                throw new DirectoryNotFoundException(sceneManipulationFolder);
            if (RunAllManipulations)
            {
                return Directory.GetFiles(sceneManipulationFolder, sceneName + "_*.cs", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            }
            if (SelectedManipulationFiles.Length > 0)
                return SelectedManipulationFiles.Select(file =>
                {
                    if (Path.GetFileName(file) != file)
                        throw new ArgumentException("Select a filename, not a path: " + file);
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (!name.StartsWith(sceneName + "_", StringComparison.Ordinal))
                        throw new ArgumentException("Manipulation does not belong to " + sceneName + ": " + file);
                    string path = Path.Combine(sceneManipulationFolder, name + ".cs");
                    if (!File.Exists(path))
                        throw new FileNotFoundException("Selected manipulation not found.", path);
                    return path;
                }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (SelectedManipulatedVariables.Length == 0 && string.IsNullOrWhiteSpace(SelectedManipulation.Variable))
                throw new InvalidOperationException("Configure the variable in SelectedManipulation before running.");
            if (!Directory.Exists(sceneManipulationFolder))
                throw new DirectoryNotFoundException(sceneManipulationFolder);

            Regex filePattern = new Regex("^" + Regex.Escape(sceneName)
                + @"_(?<variable>\w+?)_(?:(?<nominal>State\d+|true|false)_to_(?<manipulated>State\d+|true|false)|(?<manipulated>neg|true|false))_L\d+$");
            Regex outputPattern = new Regex("^" + Regex.Escape(sceneName)
                + @"_(?<variable>\w+)_L\d+$");
            Regex floatInputPattern = new Regex("^" + Regex.Escape(sceneName)
                + @"_(?<variable>\w+)_to_(?<manipulated>(?:neg_)?\d+(?:_\d+)?f)_L\d+$");
            Regex floatOutputPattern = new Regex("^" + Regex.Escape(sceneName)
                + @"_(?<variable>\w+?)_(?<nominal>(?:neg_)?\d+(?:_\d+)?f)_to_(?<manipulated>(?:neg_)?\d+(?:_\d+)?f)_L\d+$");
            string[] files = Directory.GetFiles(sceneManipulationFolder, "*.cs", SearchOption.TopDirectoryOnly);

            bool MatchesValue(string token, object selected)
            {
                if (selected == null)
                    return true;
                if (selected is float || selected is double || selected is decimal
                    || selected is int || selected is long)
                {
                    return token.EndsWith("f", StringComparison.Ordinal)
                        && decimal.TryParse(token.Substring(0, token.Length - 1)
                            .Replace("neg_", "-").Replace("_", "."),
                            NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value)
                        && value == Convert.ToDecimal(selected, CultureInfo.InvariantCulture);
                }
                return token == (selected is bool bit ? (bit ? "true" : "false") : selected.ToString());
            }

            bool MatchesSelection(string path)
            {
                Match match = filePattern.Match(Path.GetFileNameWithoutExtension(path));
                if (!match.Success)
                    match = floatOutputPattern.Match(Path.GetFileNameWithoutExtension(path));
                if (!match.Success)
                    match = floatInputPattern.Match(Path.GetFileNameWithoutExtension(path));
                bool isOutput = !match.Success;
                if (isOutput)
                    match = outputPattern.Match(Path.GetFileNameWithoutExtension(path));
                string fileManipulation = isOutput ? "neg" : match.Groups["manipulated"].Value;
                if (SelectedManipulatedVariables.Length > 0)
                    return match.Success && SelectedManipulatedVariables.Contains(
                        match.Groups["variable"].Value, StringComparer.Ordinal);
                return match.Success && match.Groups["variable"].Value == SelectedManipulation.Variable
                    && (!SelectedSourceLine.HasValue || Path.GetFileNameWithoutExtension(path)
                        .EndsWith($"_L{SelectedSourceLine.Value}", StringComparison.Ordinal))
                    && MatchesValue(match.Groups["nominal"].Value, SelectedManipulation.Nominal)
                    && MatchesValue(fileManipulation, SelectedManipulation.Manipulated);
            }

            return files.Where(MatchesSelection)
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
                @"Memory(?<signalType>Bit|Float|Int)\s+(?<fieldName>\w+)\s*=\s*MemoryMap\.Instance\.Get(?<getterType>Bit|Float|Int)\(""(?<memoryName>[^""]+)"",\s*MemoryType\.(?<memoryType>Input|Output)\);");
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
                else if (signalType == "Float")
                    TryAddMemoryFloat(signals, fieldName, memoryName, memoryType);
                else
                {
                    MemoryInt memoryInt = MemoryMap.Instance.GetInt(memoryName, memoryType);
                    signals.Add(new CsvSignal(fieldName, () => memoryInt.Value.ToString(CultureInfo.InvariantCulture)));
                }
            }

            AddControllerFieldSignal<int>(signals, controller, "counter", value => value.ToString(CultureInfo.InvariantCulture));
            AddControllerFieldSignal<State>(signals, controller, "stateLids", value => value.ToString());
            AddControllerFieldSignal<State>(signals, controller, "stateBases", value => value.ToString());

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
