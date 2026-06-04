//-----------------------------------------------------------------------------
// FACTORY I/O (SDK)
//
// Test harness for recovering PickPlaceXYZ after a pause.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

using ScreenRecorderLib;

using EngineIO;

namespace Controllers
{
    class Program_Recovery
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

        sealed class ManipulationRun
        {
            public string FilePath { get; set; }
            public string Label { get; set; }
            public string Split { get; set; }
        }

        static Recorder _rec;

        public const int CycleTime = 8;
        public const int CsvLogIntervalExecutions = 10;
        public const int PauseAtExecutionCount = 2000;
        public const int MaxRecoveryExecutions = 4000;
        public const bool EnableRecording = false;
        public const bool EnableManipulatedControllerLogging = true;

        static readonly string controllerRoot = @"D:\Code\factoryio-sdk-master\factoryio-sdk-master\samples\Controllers";
        static readonly string sceneName = "PickPlaceXYZ";
        static readonly string manipulationRoot = Path.Combine(controllerRoot, "Manipulations");
        static readonly string recoveryScriptRoot = Path.Combine(controllerRoot, "Recovery");
        static readonly string recoveryOriginalLogRoot = Path.Combine(controllerRoot, "Logs", "Recovery", "original");
        static readonly string recoveryEnhancedLogRoot = Path.Combine(controllerRoot, "Logs", "Recovery", "enhanced");
        static readonly string enhanceCsvScriptPath = Path.Combine(controllerRoot, "Logs", "enhance_csv_with_box_positions.py");
        static readonly string videoRoot = Path.Combine(controllerRoot, "Videos", "Recovery");
        static readonly string inventoryCsvPath = Path.Combine(controllerRoot, "clusters_csv_inventory.csv");
        static readonly string[] specificInventoryLabels =
        {
        
        };
        static readonly string[] specificInventorySplits =
        {
        
        };
        static readonly string[] specificManipulationNames =
        {
            "PickPlaceXYZ_spX_3_1f_to_3_12f_L128",
            "PickPlaceXYZ_spX_3_1f_to_2_08f_L121",
            "PickPlaceXYZ_spX_3_1f_to_2_36f_L121",
            "PickPlaceXYZ_spX_3_1f_to_2_71f_L121",
            "PickPlaceXYZ_spX_3_1f_to_3_14f_L121"
        };
        static readonly string[] specificManipulationNameFragments =
        {

        };
        static readonly string[] excludedManipulationNameFragments =
        {
        
        };

        static void Main(string[] args)
        {
            string manipulationFolder = Path.Combine(manipulationRoot, sceneName);
            ManipulationRun[] manipulationRuns = ReadInventoryManipulationRuns(manipulationFolder);
            Debug.WriteLine($"Found {manipulationRuns.Length} manipulation classes.");
            InitializeRecoveryTimingLog();

            MemoryBit start = MemoryMap.Instance.GetBit(MemoryMap.BitCount - 16, MemoryType.Output);
            MemoryBit pause = MemoryMap.Instance.GetBit(MemoryMap.BitCount - 48, MemoryType.Output);
            MemoryBit running = MemoryMap.Instance.GetBit(MemoryMap.BitCount - 16, MemoryType.Input);

            foreach (ManipulationRun manipulationRun in manipulationRuns)
            {
                string manipulationFile = manipulationRun.FilePath;
                string recoveryCase = manipulationRun.Label;
                string split = manipulationRun.Split;
                string controllerName = Path.GetFileNameWithoutExtension(manipulationFile);
                string relativeFolder = Path.GetDirectoryName(Path.GetRelativePath(manipulationFolder, manipulationFile));
                string originalCsvFolder = Path.Combine(recoveryOriginalLogRoot, relativeFolder ?? string.Empty);
                string enhancedCsvFolder = Path.Combine(recoveryEnhancedLogRoot, relativeFolder ?? string.Empty);
                Directory.CreateDirectory(originalCsvFolder);
                Directory.CreateDirectory(enhancedCsvFolder);

                string originalCsvPath = Path.Combine(originalCsvFolder, $"{controllerName}.csv");
                string enhancedCsvPath = Path.Combine(enhancedCsvFolder, $"{controllerName}.csv");

                Type controllerType = Type.GetType($"Controllers.{controllerName}");
                if (controllerType == null)
                {
                    Debug.WriteLine($"[SKIP] Could not find type Controllers.{controllerName}");
                    continue;
                }

                SwitchToRun(start);

                if (EnableRecording)
                    CreateRecording(Path.Combine(videoRoot, sceneName), controllerName);

                try
                {
                    Controller controller = (Controller)Activator.CreateInstance(controllerType);
                    List<CsvSignal> loggedSignals = EnableManipulatedControllerLogging
                        ? DiscoverLoggedSignals(manipulationFile, controller)
                        : new List<CsvSignal>();
                    Debug.WriteLine($"Running manipulated controller: {controllerName}, label={recoveryCase}");

                    Stopwatch stopwatch = Stopwatch.StartNew();
                    Stopwatch runStopwatch = Stopwatch.StartNew();
                    int executionCount = 0;
                    StreamWriter csvWriter = null;

                    try
                    {
                        if (EnableManipulatedControllerLogging)
                        {
                            csvWriter = new StreamWriter(originalCsvPath, false);
                            WriteCsvHeader(csvWriter, loggedSignals);
                        }

                        while (!controller.stopSignal && executionCount < PauseAtExecutionCount)
                        {
                            MemoryMap.Instance.Update();

                            if (running.Value)
                            {
                                stopwatch.Stop();
                                controller.executionCount = executionCount;
                                controller.Execute((int)stopwatch.ElapsedMilliseconds);

                                if (executionCount % CsvLogIntervalExecutions == 0)
                                    WriteCsvRowIfEnabled(csvWriter, runStopwatch.ElapsedMilliseconds, executionCount, loggedSignals);

                                if (controller.captureSignal)
                                    controller.captureSignal = false;

                                executionCount++;
                                stopwatch.Restart();
                            }

                            Thread.Sleep(CycleTime);
                        }

                        WriteCsvRowIfEnabled(csvWriter, runStopwatch.ElapsedMilliseconds, executionCount, loggedSignals);
                    }
                    finally
                    {
                        csvWriter?.Dispose();
                    }

                    if (EnableManipulatedControllerLogging)
                        EnhanceRecoveryLog(originalCsvPath, enhancedCsvPath);

                    pause.Value = true;
                    MemoryMap.Instance.Update();
                    Thread.Sleep(500);

                    Stopwatch setupStopwatch = Stopwatch.StartNew();

                    PickPlaceXYZSnapshot snapshot = PickPlaceXYZSnapshot.Read(controller);

                    Stopwatch stateIdentificationStopwatch = Stopwatch.StartNew();
                    PickPlaceXYZRecoveryDecision decision = PickPlaceXYZRecoveryStateDecider.Decide(snapshot, recoveryCase);
                    stateIdentificationStopwatch.Stop();

                    TimeSpan recoveryModuleIdentificationElapsed = TimeSpan.Zero;
                    if (!decision.StateIdentificationSatisfied)
                    {
                        Stopwatch recoveryModuleIdentificationStopwatch = Stopwatch.StartNew();
                        PickPlaceXYZRecoveryModuleDecision moduleDecision =
                            PickPlaceXYZRecoveryModuleIdentifier.Decide(snapshot, decision, recoveryCase, controllerName);
                        decision = moduleDecision.RecoveryDecision;
                        recoveryModuleIdentificationStopwatch.Stop();
                        recoveryModuleIdentificationElapsed = recoveryModuleIdentificationStopwatch.Elapsed;

                        if (moduleDecision.ClassificationFailed)
                        {
                            Debug.WriteLine($"[SKIP] Recovery module classification failed for {controllerName}: {moduleDecision.Reason}");
                            setupStopwatch.Stop();
                            AppendRecoveryTimingLog(
                                controllerName,
                                split,
                                recoveryCase,
                                "classify failed",
                                stateIdentificationStopwatch.Elapsed,
                                recoveryModuleIdentificationElapsed,
                                TimeSpan.Zero,
                                TimeSpan.Zero,
                                TimeSpan.Zero,
                                BuildFailedClassificationRobustnessLog(recoveryCase, moduleDecision.Classification));

                            pause.Value = false;
                            MemoryMap.Instance.Update();
                            Thread.Sleep(500);
                            Shutdown(start);
                            continue;
                        }

                        if (IsUnexpectedRecoveryModuleDecision(recoveryCase, decision.RecoveryModule))
                        {
                            Debug.WriteLine(
                                $"[SKIP] Recovery module mismatch for {controllerName}: " +
                                $"label={recoveryCase}, selected={decision.RecoveryModule}. {moduleDecision.Reason}");
                            setupStopwatch.Stop();
                            AppendRecoveryTimingLog(
                                controllerName,
                                split,
                                recoveryCase,
                                decision.RecoveryModule.ToString(),
                                stateIdentificationStopwatch.Elapsed,
                                recoveryModuleIdentificationElapsed,
                                TimeSpan.Zero,
                                TimeSpan.Zero,
                                TimeSpan.Zero,
                                BuildMismatchRobustnessLog(recoveryCase, decision.RecoveryModule, moduleDecision.Classification));

                            pause.Value = false;
                            MemoryMap.Instance.Update();
                            Thread.Sleep(500);
                            Shutdown(start);
                            continue;
                        }
                    }

                    Stopwatch scriptWriteStopwatch = Stopwatch.StartNew();
                    (string recoveryScriptPath, string recoveryClassName) = WriteRecoveryScript(controllerName, snapshot, decision);
                    scriptWriteStopwatch.Stop();

                    Debug.WriteLine(
                        $"Recovery decision for {controllerName}: pickingState={decision.PickingState}, " +
                        $"grabState={decision.GrabState}, counter={decision.Counter}, reason={decision.Reason}");

                    Stopwatch compilationStopwatch = Stopwatch.StartNew();
                    Controller recoveryController = CreateRuntimeCompiledRecoveryController(
                        recoveryScriptPath,
                        recoveryClassName,
                        decision);
                    compilationStopwatch.Stop();
                    setupStopwatch.Stop();

                    pause.Value = false;
                    MemoryMap.Instance.Update();
                    Thread.Sleep(500);

                    stopwatch.Restart();
                    int recoveryExecutions = 0;

                    while (!recoveryController.stopSignal && recoveryExecutions < MaxRecoveryExecutions)
                    {
                        MemoryMap.Instance.Update();

                        if (running.Value)
                        {
                            stopwatch.Stop();
                            recoveryController.executionCount = recoveryExecutions;
                            recoveryController.Execute((int)stopwatch.ElapsedMilliseconds);
                            recoveryExecutions++;
                            stopwatch.Restart();
                        }

                        Thread.Sleep(CycleTime);
                    }

                    Debug.WriteLine($"Recovery executed {recoveryExecutions} scans for {controllerName}.");

                    AppendRecoveryTimingLog(
                        controllerName,
                        split,
                        recoveryCase,
                        decision.RecoveryModule.ToString(),
                        stateIdentificationStopwatch.Elapsed,
                        recoveryModuleIdentificationElapsed,
                        scriptWriteStopwatch.Elapsed,
                        compilationStopwatch.Elapsed,
                        GetAdditionalRecoveryModuleExecutionElapsed(recoveryController));

                    Shutdown(start);
                }
                finally
                {
                    if (EnableRecording)
                        EndRecording();
                }
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
            Thread.Sleep(500);
        }

        static void CreateRecording(string videoFolder, string videoName)
        {
            Directory.CreateDirectory(videoFolder);
            string videoPath = Path.Combine(videoFolder, $"{videoName}.mp4");
            _rec = Recorder.CreateRecorder();
            _rec.OnRecordingComplete += Rec_OnRecordingComplete;
            _rec.OnRecordingFailed += Rec_OnRecordingFailed;
            _rec.OnStatusChanged += Rec_OnStatusChanged;
            Debug.WriteLine($"Recording started: {videoPath}");
            _rec.Record(videoPath);
        }

        static void EndRecording()
        {
            if (_rec == null)
                return;

            Debug.WriteLine("Stopping recording...");
            _rec.Stop();
            _rec = null;
            Thread.Sleep(500);
        }

        static void Rec_OnRecordingComplete(object sender, RecordingCompleteEventArgs e)
        {
            Debug.WriteLine($"Recording complete: {e.FilePath}");
        }

        static void Rec_OnRecordingFailed(object sender, RecordingFailedEventArgs e)
        {
            Debug.WriteLine($"Recording failed: {e.Error}");
        }

        static void Rec_OnStatusChanged(object sender, RecordingStatusEventArgs e)
        {
            Debug.WriteLine($"Recording status: {e.Status}");
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

        static void TryAddMemoryBit(List<CsvSignal> signals, string csvName, string memoryName, MemoryType memoryType)
        {
            try
            {
                MemoryBit bit = MemoryMap.Instance.GetBit(memoryName, memoryType);
                if (bit == null)
                {
                    Debug.WriteLine($"Skipping recovery CSV log bit '{memoryName}': MemoryMap returned null.");
                    return;
                }

                signals.Add(new CsvSignal(csvName, () => bit.Value ? "1" : "0"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Skipping recovery CSV log bit '{memoryName}': {ex.Message}");
            }
        }

        static void TryAddMemoryFloat(List<CsvSignal> signals, string csvName, string memoryName, MemoryType memoryType)
        {
            try
            {
                MemoryFloat memoryFloat = MemoryMap.Instance.GetFloat(memoryName, memoryType);
                if (memoryFloat == null)
                {
                    Debug.WriteLine($"Skipping recovery CSV log float '{memoryName}': MemoryMap returned null.");
                    return;
                }

                signals.Add(new CsvSignal(csvName, () => memoryFloat.Value.ToString("G", CultureInfo.InvariantCulture)));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Skipping recovery CSV log float '{memoryName}': {ex.Message}");
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

        static void WriteCsvRowIfEnabled(StreamWriter writer, long elapsedMs, int executionCount, List<CsvSignal> signals)
        {
            if (!EnableManipulatedControllerLogging || writer == null)
                return;

            WriteCsvRow(writer, elapsedMs, executionCount, signals);
        }

        static void EnhanceRecoveryLog(string originalCsvPath, string enhancedCsvPath)
        {
            if (!File.Exists(enhanceCsvScriptPath))
            {
                Debug.WriteLine($"Skipping enhanced recovery CSV: script not found at {enhanceCsvScriptPath}");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(enhancedCsvPath));

            string code =
                "import sys\n" +
                "from enhance_csv_with_box_positions import enhance_csv_with_box_positions\n" +
                $"success, message, _ = enhance_csv_with_box_positions({PythonString(originalCsvPath)}, {PythonString(enhancedCsvPath)})\n" +
                "print(message)\n" +
                "sys.exit(0 if success else 1)\n";

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "python",
                WorkingDirectory = Path.GetDirectoryName(enhanceCsvScriptPath),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(code);

            try
            {
                using (Process process = Process.Start(startInfo))
                {
                    string stdout = process.StandardOutput.ReadToEnd();
                    string stderr = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (!string.IsNullOrWhiteSpace(stdout))
                        Debug.WriteLine(stdout);
                    if (!string.IsNullOrWhiteSpace(stderr))
                        Debug.WriteLine(stderr);

                    if (process.ExitCode != 0)
                        Debug.WriteLine($"Enhanced recovery CSV generation failed for {Path.GetFileName(originalCsvPath)} with exit code {process.ExitCode}.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Enhanced recovery CSV generation failed for {Path.GetFileName(originalCsvPath)}: {ex.Message}");
            }
        }

        static string PythonString(string value)
        {
            return "'" + value.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
        }

        static ManipulationRun[] ReadInventoryManipulationRuns(string manipulationFolder)
        {
            if (!File.Exists(inventoryCsvPath))
                throw new FileNotFoundException("Could not find clusters_csv_inventory.csv for recovery input.", inventoryCsvPath);

            Dictionary<string, string> manipulationFilesByName = Directory
                .GetFiles(manipulationFolder, "*.cs", SearchOption.AllDirectories)
                .GroupBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            return File.ReadLines(inventoryCsvPath)
                .Skip(1)
                .Select(ParseInventoryRow)
                .Where(row => !string.IsNullOrWhiteSpace(row.FileName))
                .Where(row => MatchesSpecificLabels(row.Label))
                .Where(row => MatchesSpecificSplits(row.Split))
                .Where(row => MatchesSpecificManipulationNames(Path.GetFileNameWithoutExtension(row.FileName)))
                .Where(row => MatchesSpecificNameFragments(Path.GetFileNameWithoutExtension(row.FileName)))
                .Where(row => DoesNotMatchExcludedNameFragments(Path.GetFileNameWithoutExtension(row.FileName)))
                .Select(row => new ManipulationRun
                {
                    FilePath = ResolveManipulationFilePath(
                        manipulationFilesByName,
                        Path.ChangeExtension(row.FileName, ".cs")),
                    Label = row.Label,
                    Split = row.Split
                })
                .Where(run =>
                {
                    if (!string.IsNullOrWhiteSpace(run.FilePath) && File.Exists(run.FilePath))
                        return true;

                    Debug.WriteLine("[SKIP] clusters_csv_inventory.csv references missing manipulation file.");
                    return false;
                })
                .GroupBy(run => run.FilePath)
                .Select(group => group.First())
                .ToArray();
        }

        static string ResolveManipulationFilePath(
            Dictionary<string, string> manipulationFilesByName,
            string manipulationFileName)
        {
            if (manipulationFilesByName.TryGetValue(manipulationFileName, out string path))
                return path;

            return null;
        }

        static bool MatchesSpecificLabels(string label)
        {
            if (specificInventoryLabels.Length == 0)
                return true;

            return specificInventoryLabels.Any(specificLabel =>
                string.Equals(label, specificLabel, StringComparison.OrdinalIgnoreCase));
        }

        static bool MatchesSpecificSplits(string split)
        {
            if (specificInventorySplits.Length == 0)
                return true;

            return specificInventorySplits.Any(specificSplit =>
                string.Equals(split, specificSplit, StringComparison.OrdinalIgnoreCase));
        }

        static bool MatchesSpecificManipulationNames(string manipulationName)
        {
            if (specificManipulationNames.Length == 0)
                return true;

            return specificManipulationNames.Any(specificName =>
                string.Equals(manipulationName, specificName, StringComparison.OrdinalIgnoreCase));
        }

        static bool MatchesSpecificNameFragments(string manipulationName)
        {
            if (specificManipulationNameFragments.Length == 0)
                return true;

            return specificManipulationNameFragments.All(fragment =>
                manipulationName.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        static bool DoesNotMatchExcludedNameFragments(string manipulationName)
        {
            if (excludedManipulationNameFragments.Length == 0)
                return true;

            return !excludedManipulationNameFragments.Any(fragment =>
                manipulationName.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        static (string FileName, string Split, string Label) ParseInventoryRow(string line)
        {
            string[] columns = line.Split(',');
            if (columns.Length < 3)
                return (string.Empty, string.Empty, string.Empty);

            return (columns[0].Trim(), columns[1].Trim(), columns[2].Trim());
        }

        static void InitializeRecoveryTimingLog()
        {
            Directory.CreateDirectory(recoveryScriptRoot);
            string path = Path.Combine(recoveryScriptRoot, "recovery_timing.csv");
            EnsureCsvHeader(path, "manipulation,split,label,recovery_module,state_identification_ms,recovery_module_identification_ms,script_write_ms,compilation_load_ms,recovery_execution_ms,expected_module_robustness,selected_module_robustness,max_robustness_module,max_robustness");
        }

        static void EnsureCsvHeader(string path, string header)
        {
            if (File.Exists(path) && new FileInfo(path).Length > 0)
            {
                if (File.ReadLines(path).Any(line => line == header))
                    return;

                File.AppendAllText(path, header + Environment.NewLine);
                return;
            }

            File.WriteAllText(path, header + Environment.NewLine);
        }

        static void AppendRecoveryTimingLog(
            string manipulationName,
            string split,
            string label,
            string recoveryModule,
            TimeSpan stateIdentificationElapsed,
            TimeSpan recoveryModuleIdentificationElapsed,
            TimeSpan scriptWriteElapsed,
            TimeSpan compilationElapsed,
            TimeSpan recoveryExecutionElapsed,
            RecoveryRobustnessLog robustnessLog = null)
        {
            string path = Path.Combine(recoveryScriptRoot, "recovery_timing.csv");
            bool hasAdditionalRecoveryModule =
                recoveryModule != RecoveryModule.BenignResume.ToString() &&
                recoveryModule != "classify failed";
            string[] values =
            {
                manipulationName,
                split,
                label,
                recoveryModule,
                stateIdentificationElapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
                recoveryModuleIdentificationElapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
                scriptWriteElapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
                compilationElapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
                hasAdditionalRecoveryModule
                    ? recoveryExecutionElapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture)
                    : string.Empty,
                FormatNullableFloat(robustnessLog?.ExpectedModuleRobustness),
                FormatNullableFloat(robustnessLog?.SelectedModuleRobustness),
                robustnessLog?.MaxRobustnessModule ?? string.Empty,
                FormatNullableFloat(robustnessLog?.MaxRobustness)
            };

            File.AppendAllText(path, string.Join(",", values.Select(EscapeCsv)) + Environment.NewLine);
        }

        sealed class RecoveryRobustnessLog
        {
            public float? ExpectedModuleRobustness { get; set; }
            public float? SelectedModuleRobustness { get; set; }
            public string MaxRobustnessModule { get; set; }
            public float? MaxRobustness { get; set; }
        }

        static RecoveryRobustnessLog BuildMismatchRobustnessLog(
            string label,
            RecoveryModule selectedModule,
            PickPlaceXYZRecoveryModuleClassification classification)
        {
            return new RecoveryRobustnessLog
            {
                ExpectedModuleRobustness = GetRobustness(classification, ClassNameForLabel(label)),
                SelectedModuleRobustness = GetRobustness(classification, ClassNameForRecoveryModule(selectedModule)),
                MaxRobustnessModule = classification?.BestClassName ?? string.Empty,
                MaxRobustness = classification?.BestRobustness
            };
        }

        static RecoveryRobustnessLog BuildFailedClassificationRobustnessLog(
            string label,
            PickPlaceXYZRecoveryModuleClassification classification)
        {
            return new RecoveryRobustnessLog
            {
                ExpectedModuleRobustness = GetRobustness(classification, ClassNameForLabel(label)),
                MaxRobustnessModule = classification?.BestClassName ?? string.Empty,
                MaxRobustness = classification?.BestRobustness
            };
        }

        static float? GetRobustness(PickPlaceXYZRecoveryModuleClassification classification, string className)
        {
            if (classification?.RobustnessByClass == null || string.IsNullOrWhiteSpace(className))
                return null;

            if (classification.RobustnessByClass.TryGetValue(className, out float robustness))
                return robustness;

            return null;
        }

        static string FormatNullableFloat(float? value)
        {
            return value.HasValue
                ? value.Value.ToString("G", CultureInfo.InvariantCulture)
                : string.Empty;
        }

        static bool IsUnexpectedRecoveryModuleDecision(string label, RecoveryModule recoveryModule)
        {
            RecoveryModule? expectedModule = ExpectedRecoveryModuleForLabel(label);
            return expectedModule.HasValue && recoveryModule != expectedModule.Value;
        }

        static RecoveryModule? ExpectedRecoveryModuleForLabel(string label)
        {
            switch (label)
            {
                case "complete":
                    return RecoveryModule.BenignResume;
                case "overflow":
                    return RecoveryModule.Overflow;
                case "underflow":
                    return RecoveryModule.Underflow;
                case "misalignment_beltconveyor":
                    return RecoveryModule.MisalignmentBeltConveyor;
                case "misalignment_first_box":
                    return RecoveryModule.MisalignmentFirstBox;
                case "misalignment_second_box":
                    return RecoveryModule.MisalignmentSecondBox;
                case "misalignment_third_box":
                    return RecoveryModule.MisalignmentThirdBox;
                default:
                    return null;
            }
        }

        static string ClassNameForLabel(string label)
        {
            switch (label)
            {
                case "overflow":
                case "underflow":
                case "misalignment_beltconveyor":
                case "misalignment_first_box":
                case "misalignment_second_box":
                case "misalignment_third_box":
                    return label;
                default:
                    return null;
            }
        }

        static string ClassNameForRecoveryModule(RecoveryModule recoveryModule)
        {
            switch (recoveryModule)
            {
                case RecoveryModule.Overflow:
                    return "overflow";
                case RecoveryModule.Underflow:
                    return "underflow";
                case RecoveryModule.MisalignmentBeltConveyor:
                    return "misalignment_beltconveyor";
                case RecoveryModule.MisalignmentFirstBox:
                    return "misalignment_first_box";
                case RecoveryModule.MisalignmentSecondBox:
                    return "misalignment_second_box";
                case RecoveryModule.MisalignmentThirdBox:
                    return "misalignment_third_box";
                default:
                    return null;
            }
        }

        static string EscapeCsv(string value)
        {
            if (value.Contains(",") || value.Contains("\"") || value.Contains(Environment.NewLine))
                return $"\"{value.Replace("\"", "\"\"")}\"";

            return value;
        }

        static TimeSpan GetAdditionalRecoveryModuleExecutionElapsed(Controller controller)
        {
            Recovery_PickPlaceXYZ recoveryController = controller as Recovery_PickPlaceXYZ;
            if (recoveryController == null)
                return TimeSpan.Zero;

            return recoveryController.AdditionalRecoveryModuleExecutionElapsed;
        }

        static (string Path, string ClassName) WriteRecoveryScript(
            string sourceControllerName,
            PickPlaceXYZSnapshot snapshot,
            PickPlaceXYZRecoveryDecision decision)
        {
            string className = $"Recovery_{sourceControllerName}";
            Directory.CreateDirectory(recoveryScriptRoot);
            string path = Path.Combine(recoveryScriptRoot, $"{className}.cs");

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("// Auto-generated by Program_TestRecovery.");
            builder.AppendLine("// This class is compiled at runtime and loaded after Factory I/O is paused.");
            builder.AppendLine();
            builder.AppendLine("namespace Controllers");
            builder.AppendLine("{");
            builder.AppendLine($"    public sealed class {className} : Recovery_PickPlaceXYZ");
            builder.AppendLine("    {");
            builder.AppendLine($"        public {className}()");
            builder.AppendLine("            : base(new PickPlaceXYZRecoveryDecision");
            builder.AppendLine("            {");
            builder.AppendLine($"                PickingState = State.{decision.PickingState},");
            builder.AppendLine($"                GrabState = State.{decision.GrabState},");
            builder.AppendLine($"                Counter = {decision.Counter.ToString(CultureInfo.InvariantCulture)},");
            builder.AppendLine($"                ExitBox = {decision.ExitBox.ToString(CultureInfo.InvariantCulture)},");
            builder.AppendLine($"                StopExitBox = {decision.StopExitBox.ToString(CultureInfo.InvariantCulture)},");
            builder.AppendLine($"                StateIdentificationSatisfied = {decision.StateIdentificationSatisfied.ToString().ToLowerInvariant()},");
            builder.AppendLine($"                RecoveryModule = RecoveryModule.{decision.RecoveryModule},");
            if (decision.OverrideSpZ)
            {
                builder.AppendLine("                OverrideSpZ = true,");
                builder.AppendLine($"                RecoverySpZ = {decision.RecoverySpZ.ToString("G", CultureInfo.InvariantCulture)}f,");
            }
            if (decision.SafeGrabCompletionThreshold != 6)
                builder.AppendLine($"                SafeGrabCompletionThreshold = {decision.SafeGrabCompletionThreshold.ToString(CultureInfo.InvariantCulture)},");
            if (decision.GrabReleaseOperations != null && decision.GrabReleaseOperations.Length > 0)
            {
                builder.AppendLine("                GrabReleaseOperations = new[]");
                builder.AppendLine("                {");

                foreach (PickPlaceXYZGrabReleaseOperation operation in decision.GrabReleaseOperations)
                {
                    builder.AppendLine(
                        "                    new PickPlaceXYZGrabReleaseOperation(" +
                        $"{Fmt(operation.PickupX)}f, {Fmt(operation.PickupY)}f, {Fmt(operation.PickupZ)}f, " +
                        $"{Fmt(operation.PlaceX)}f, {Fmt(operation.PlaceY)}f, {Fmt(operation.PlaceZ)}f, " +
                        $"{operation.GrabCValue.ToString().ToLowerInvariant()}, " +
                        $"{operation.ReleaseCValue.ToString().ToLowerInvariant()}),");
                }

                builder.AppendLine("                },");
            }
            builder.AppendLine($"                Reason = @\"{decision.Reason.Replace("\"", "\"\"")}\"");
            builder.AppendLine("            })");
            builder.AppendLine("        {");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        // Paused snapshot used to make the decision:");
            builder.AppendLine($"        // partAtPlace={snapshot.PartAtPlace}, boxAtPlace={snapshot.BoxAtPlace}, detected={snapshot.Detected}");
            builder.AppendLine($"        // partConveyorForward={snapshot.PartConveyorForward}, boxConveyorForward={snapshot.BoxConveyorForward}, exitConveyor={snapshot.ExitConveyor}");
            builder.AppendLine($"        // grab={snapshot.Grab}, c={snapshot.C}");
            builder.AppendLine($"        // spX={Fmt(snapshot.SpX)}, spY={Fmt(snapshot.SpY)}, spZ={Fmt(snapshot.SpZ)}");
            builder.AppendLine($"        // posX={Fmt(snapshot.PosX)}, posY={Fmt(snapshot.PosY)}, posZ={Fmt(snapshot.PosZ)}");
            builder.AppendLine($"        // counter={snapshot.Counter}, exitBox={snapshot.ExitBox}");
            builder.AppendLine("    }");
            builder.AppendLine("}");

            File.WriteAllText(path, builder.ToString());
            return (path, className);
        }

        static Controller CreateRuntimeCompiledRecoveryController(
            string recoveryScriptPath,
            string recoveryClassName,
            PickPlaceXYZRecoveryDecision fallbackDecision)
        {
            try
            {
                string assemblyPath = CompileRecoveryScript(recoveryScriptPath, recoveryClassName);
                Assembly assembly = Assembly.LoadFrom(assemblyPath);
                Type recoveryType = assembly.GetType($"Controllers.{recoveryClassName}", true);
                Debug.WriteLine($"Loaded runtime-compiled recovery controller: {recoveryType.FullName}");
                return (Controller)Activator.CreateInstance(recoveryType);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Runtime compilation failed for {recoveryClassName}: {ex}");
                Debug.WriteLine("Falling back to compiled generic Recovery_PickPlaceXYZ.");
                return new Recovery_PickPlaceXYZ(fallbackDecision);
            }
        }

        static string CompileRecoveryScript(string recoveryScriptPath, string recoveryClassName)
        {
            string scriptFolder = Path.GetDirectoryName(recoveryScriptPath);
            string buildFolder = Path.Combine(scriptFolder, ".runtime_build", recoveryClassName);
            Directory.CreateDirectory(buildFolder);

            string projectPath = Path.Combine(buildFolder, $"{recoveryClassName}.csproj");
            string outputFolder = Path.Combine(buildFolder, "bin");
            string currentAssemblyPath = Assembly.GetExecutingAssembly().Location;

            File.WriteAllText(projectPath, BuildRecoveryProjectXml(currentAssemblyPath, recoveryScriptPath));

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{projectPath}\" -c Debug -o \"{outputFolder}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (Process process = Process.Start(startInfo))
            {
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                    throw new InvalidOperationException(
                        $"dotnet build failed with exit code {process.ExitCode}.{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");

                Debug.WriteLine(stdout);
                if (!string.IsNullOrWhiteSpace(stderr))
                    Debug.WriteLine(stderr);
            }

            string assemblyPath = Path.Combine(outputFolder, $"{recoveryClassName}.dll");
            if (!File.Exists(assemblyPath))
                throw new FileNotFoundException("Runtime-compiled recovery assembly was not produced.", assemblyPath);

            return assemblyPath;
        }

        static string BuildRecoveryProjectXml(string currentAssemblyPath, string recoveryScriptPath)
        {
            return $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net5.0-windows</TargetFramework>
    <OutputType>Library</OutputType>
    <PlatformTarget>x64</PlatformTarget>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include=""{EscapeXml(recoveryScriptPath)}"" />
    <Reference Include=""Controllers"">
      <HintPath>{EscapeXml(currentAssemblyPath)}</HintPath>
    </Reference>
  </ItemGroup>
</Project>
";
        }

        static string EscapeXml(string value)
        {
            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }

        static string Fmt(float value)
        {
            return value.ToString("G", CultureInfo.InvariantCulture);
        }
    }

    public class Recovery_PickPlaceXYZ : Controller
    {
        PickPlaceXYZBenignRecoveryController benignController;
        PickPlaceXYZOverflowRecoveryModuleController overflowController;
        PickPlaceXYZMisalignmentBeltConveyorRecoveryModuleController misalignmentBeltConveyorController;
        PickPlaceXYZUnderflowRecoveryModuleController underflowController;
        PickPlaceXYZMisalignmentBoxRecoveryModuleController misalignmentBoxController;
        Controller activeController;
        long additionalRecoveryModuleExecutionMilliseconds;

        public bool AdditionalRecoveryModuleTransferred { get; private set; }

        public TimeSpan AdditionalRecoveryModuleExecutionElapsed
        {
            get { return TimeSpan.FromMilliseconds(additionalRecoveryModuleExecutionMilliseconds); }
        }

        public Recovery_PickPlaceXYZ(PickPlaceXYZRecoveryDecision decision)
        {
            if (decision.RecoveryModule == RecoveryModule.Overflow)
            {
                overflowController = new PickPlaceXYZOverflowRecoveryModuleController(decision);
                activeController = overflowController;
            }
            else if (decision.RecoveryModule == RecoveryModule.MisalignmentBeltConveyor)
            {
                misalignmentBeltConveyorController =
                    new PickPlaceXYZMisalignmentBeltConveyorRecoveryModuleController(decision);
                activeController = misalignmentBeltConveyorController;
            }
            else if (decision.RecoveryModule == RecoveryModule.Underflow)
            {
                underflowController =
                    new PickPlaceXYZUnderflowRecoveryModuleController(decision);
                activeController = underflowController;
            }
            else if (IsMisalignmentBoxModule(decision.RecoveryModule))
            {
                misalignmentBoxController =
                    new PickPlaceXYZMisalignmentBoxRecoveryModuleController(decision);
                activeController = misalignmentBoxController;
            }
            else
            {
                benignController = new PickPlaceXYZBenignRecoveryController(decision);
                activeController = benignController;
            }
        }

        public override void Execute(int elapsedMilliseconds)
        {
            bool additionalRecoveryModuleActive =
                activeController == overflowController
                && overflowController != null
                && !overflowController.ModuleComplete;
            additionalRecoveryModuleActive =
                additionalRecoveryModuleActive
                || (activeController == misalignmentBeltConveyorController
                    && misalignmentBeltConveyorController != null
                    && !misalignmentBeltConveyorController.ModuleComplete);
            additionalRecoveryModuleActive =
                additionalRecoveryModuleActive
                || (activeController == underflowController
                    && underflowController != null
                    && !underflowController.ModuleComplete);
            additionalRecoveryModuleActive =
                additionalRecoveryModuleActive
                || (activeController == misalignmentBoxController
                    && misalignmentBoxController != null
                    && !misalignmentBoxController.ModuleComplete);

            activeController.executionCount = executionCount;
            activeController.Execute(elapsedMilliseconds);

            if (additionalRecoveryModuleActive)
                additionalRecoveryModuleExecutionMilliseconds += elapsedMilliseconds;

            if (activeController == overflowController && overflowController.ModuleComplete)
            {
                benignController = new PickPlaceXYZBenignRecoveryController(
                    overflowController.CreateContinuationDecision());
                activeController = benignController;
                AdditionalRecoveryModuleTransferred = true;
                Debug.WriteLine("Overflow recovery module complete; control transferred to benign PickPlaceXYZ controller.");
                return;
            }

            if (activeController == misalignmentBeltConveyorController
                && misalignmentBeltConveyorController.ModuleComplete)
            {
                benignController = new PickPlaceXYZBenignRecoveryController(
                    misalignmentBeltConveyorController.CreateContinuationDecision());
                activeController = benignController;
                AdditionalRecoveryModuleTransferred = true;
                Debug.WriteLine("Misalignment belt conveyor recovery module complete; control transferred to benign PickPlaceXYZ controller.");
                return;
            }

            if (activeController == underflowController
                && underflowController.ModuleComplete)
            {
                benignController = new PickPlaceXYZBenignRecoveryController(
                    underflowController.CreateContinuationDecision());
                activeController = benignController;
                AdditionalRecoveryModuleTransferred = true;
                Debug.WriteLine("Underflow recovery module complete; control transferred to benign PickPlaceXYZ controller.");
                return;
            }

            if (activeController == misalignmentBoxController
                && misalignmentBoxController.ModuleComplete)
            {
                benignController = new PickPlaceXYZBenignRecoveryController(
                    misalignmentBoxController.CreateContinuationDecision());
                activeController = benignController;
                AdditionalRecoveryModuleTransferred = true;
                Debug.WriteLine("Misalignment box recovery module complete; control transferred to benign PickPlaceXYZ controller.");
                return;
            }

            stopSignal = activeController.stopSignal;
        }

        static bool IsMisalignmentBoxModule(RecoveryModule recoveryModule)
        {
            return recoveryModule == RecoveryModule.MisalignmentFirstBox
                || recoveryModule == RecoveryModule.MisalignmentSecondBox
                || recoveryModule == RecoveryModule.MisalignmentThirdBox;
        }
    }

    public class PickPlaceXYZBenignRecoveryController : Controller
    {
        protected MemoryBit partConveyorForward = MemoryMap.Instance.GetBit("Belt Conveyor (4m) 1 (+)", MemoryType.Output);
        protected MemoryBit partConveyorBackward = MemoryMap.Instance.GetBit("Belt Conveyor (4m) 1 (-)", MemoryType.Output);
        protected MemoryBit boxConveyorForward = MemoryMap.Instance.GetBit("Roller Conveyor (6m) 1 (+)", MemoryType.Output);
        protected MemoryBit boxConveyorBackward = MemoryMap.Instance.GetBit("Roller Conveyor (6m) 1 (-)", MemoryType.Output);
        protected MemoryBit exitConveyor = MemoryMap.Instance.GetBit("Exit conveyor", MemoryType.Output);
        protected MemoryBit grab = MemoryMap.Instance.GetBit("Grab", MemoryType.Output);
        protected MemoryBit c = MemoryMap.Instance.GetBit("C +", MemoryType.Output);
        protected MemoryFloat spX = MemoryMap.Instance.GetFloat("SP X", MemoryType.Output);
        protected MemoryFloat spY = MemoryMap.Instance.GetFloat("SP Y", MemoryType.Output);
        protected MemoryFloat spZ = MemoryMap.Instance.GetFloat("SP Z", MemoryType.Output);

        protected MemoryBit partAtPlace = MemoryMap.Instance.GetBit("Part at place", MemoryType.Input);
        protected MemoryBit boxAtPlace = MemoryMap.Instance.GetBit("Box at place", MemoryType.Input);
        protected MemoryBit detected = MemoryMap.Instance.GetBit("Detected", MemoryType.Input);
        protected MemoryFloat posX = MemoryMap.Instance.GetFloat("X", MemoryType.Input);
        protected MemoryFloat posY = MemoryMap.Instance.GetFloat("Y", MemoryType.Input);
        protected MemoryFloat posZ = MemoryMap.Instance.GetFloat("Z", MemoryType.Input);

        protected RTRIG rtPartAtPlace = new RTRIG();
        protected RTRIG rtBoxAtPlace = new RTRIG();

        protected FTRIG ftPartAtPlace = new FTRIG();
        protected FTRIG ftBoxAtPlace = new FTRIG();

        protected State pickingState = State.State0;
        protected State grabState = State.State0;

        protected TON grabTimer = new TON();

        protected int counter;
        protected int exitBox;
        readonly int stopExitBox;

        public PickPlaceXYZBenignRecoveryController(PickPlaceXYZRecoveryDecision decision)
        {
            pickingState = decision.PickingState;
            grabState = decision.GrabState;
            counter = decision.Counter;
            exitBox = decision.ExitBox;
            stopExitBox = decision.StopExitBox;
            grabTimer.PT = 1000;

            if (decision.OverrideSpZ)
                spZ.Value = decision.RecoverySpZ;
        }

        public override void Execute(int elapsedMilliseconds)
        {
            ExecutePickPlaceCycle(elapsedMilliseconds);

            if (exitBox >= stopExitBox)
                stopSignal = true;
        }

        protected void ExecutePickPlaceCycle(int elapsedMilliseconds)
        {
            ftPartAtPlace.CLK(!partAtPlace.Value);
            ftBoxAtPlace.CLK(!boxAtPlace.Value);

            rtPartAtPlace.CLK(!partAtPlace.Value);
            rtBoxAtPlace.CLK(!boxAtPlace.Value);

            partConveyorForward.Value = false;
            partConveyorBackward.Value = false;
            boxConveyorForward.Value = false;
            boxConveyorBackward.Value = false;
            exitConveyor.Value = false;

            if (pickingState == State.State0)
            {
                grabState = State.State0;

                if (!partAtPlace.Value && !boxAtPlace.Value && counter < 3)
                    pickingState = State.State1;
            }
            else if (pickingState == State.State1)
            {
                c.Value = false;

                spX.Value = 8.3f;
                spY.Value = 5.5f;

                if (Near(posX.Value, spX.Value, 0.01f) && Near(posY.Value, spY.Value, 0.01f))
                {
                    grabState = State.State1;
                    pickingState = State.State2;
                }
            }
            else if (pickingState == State.State2)
            {
                if (grabState == State.State0)
                    pickingState = State.State3;
            }
            else if (pickingState == State.State3)
            {
                if (counter == 0)
                {
                    spX.Value = 3.1f;
                    spY.Value = 3.8f;
                }
                else if (counter == 1)
                {
                    spX.Value = 3.1f;
                    spY.Value = 6.7f;
                }
                else if (counter == 2)
                {
                    c.Value = true;

                    spX.Value = 3.1f;
                    spY.Value = 5.3f;
                }

                if (Near(posX.Value, spX.Value, 0.01f) && Near(posY.Value, spY.Value, 0.01f))
                    pickingState = State.State4;
            }
            else if (pickingState == State.State4)
            {
                if (counter == 0 || counter == 1)
                {
                    spZ.Value = 10f;
                }
                else if (counter == 2)
                {
                    spZ.Value = 5f;
                }

                if (Near(posZ.Value, spZ.Value, 0.01f))
                {
                    grab.Value = false;

                    counter++;

                    pickingState = State.State5;
                }
            }
            else if (pickingState == State.State5)
            {
                spZ.Value = 0f;

                if (Near(posZ.Value, spZ.Value, 0.01f))
                    pickingState = State.State0;
            }

            if (grabState == State.State0)
            {
            }
            else if (grabState == State.State1)
            {
                spZ.Value = 5.3f;

                if (detected.Value)
                {
                    spZ.Value = posZ.Value;
                    grabState = State.State2;
                }
            }
            else if (grabState == State.State2)
            {
                grab.Value = true;

                grabTimer.IN = true;

                if (grabTimer.Q)
                {
                    grabTimer.IN = false;
                    grabState = State.State3;
                }
            }
            else if (grabState == State.State3)
            {
                spZ.Value = 0f;

                if (Near(spZ.Value, posZ.Value, 0.01f))
                    grabState = State.State0;
            }

            if (partAtPlace.Value)
                partConveyorForward.Value = true;
            if (counter == 3)
            {
                boxConveyorForward.Value = true;
                exitConveyor.Value = true;

                if (ftBoxAtPlace.Q)
                {
                    counter = 0;
                    exitConveyor.Value = false;
                    exitBox++;
                }
            }
            else
            {
                if (boxAtPlace.Value)
                {
                    boxConveyorForward.Value = true;
                    exitConveyor.Value = true;
                }
            }

        }

        bool Near(float val1, float val2, float delta)
        {
            return Math.Abs(val1 - val2) < delta;
        }
    }

    public sealed class PickPlaceXYZOverflowRecoveryModuleController : Controller
    {
        enum OverflowRecoveryState
        {
            LiftToHomeZ,
            SafePickPlace,
            Complete
        }

        MemoryBit partConveyorForward = MemoryMap.Instance.GetBit("Belt Conveyor (4m) 1 (+)", MemoryType.Output);
        MemoryBit partConveyorBackward = MemoryMap.Instance.GetBit("Belt Conveyor (4m) 1 (-)", MemoryType.Output);
        MemoryBit boxConveyorForward = MemoryMap.Instance.GetBit("Roller Conveyor (6m) 1 (+)", MemoryType.Output);
        MemoryBit boxConveyorBackward = MemoryMap.Instance.GetBit("Roller Conveyor (6m) 1 (-)", MemoryType.Output);
        MemoryBit exitConveyor = MemoryMap.Instance.GetBit("Exit conveyor", MemoryType.Output);
        MemoryBit grab = MemoryMap.Instance.GetBit("Grab", MemoryType.Output);
        MemoryBit c = MemoryMap.Instance.GetBit("C +", MemoryType.Output);
        MemoryFloat spX = MemoryMap.Instance.GetFloat("SP X", MemoryType.Output);
        MemoryFloat spY = MemoryMap.Instance.GetFloat("SP Y", MemoryType.Output);
        MemoryFloat spZ = MemoryMap.Instance.GetFloat("SP Z", MemoryType.Output);

        MemoryBit partAtPlace = MemoryMap.Instance.GetBit("Part at place", MemoryType.Input);
        MemoryBit boxAtPlace = MemoryMap.Instance.GetBit("Box at place", MemoryType.Input);
        MemoryBit detected = MemoryMap.Instance.GetBit("Detected", MemoryType.Input);
        MemoryFloat posX = MemoryMap.Instance.GetFloat("X", MemoryType.Input);
        MemoryFloat posY = MemoryMap.Instance.GetFloat("Y", MemoryType.Input);
        MemoryFloat posZ = MemoryMap.Instance.GetFloat("Z", MemoryType.Input);

        RTRIG rtPartAtPlace = new RTRIG();
        RTRIG rtBoxAtPlace = new RTRIG();

        FTRIG ftPartAtPlace = new FTRIG();
        FTRIG ftBoxAtPlace = new FTRIG();

        State pickingState = State.State0;
        State grabState = State.State0;

        TON grabTimer = new TON();

        int counter;
        int exitBox;
        State previousGrabState;
        int safeGrabMomentCount;
        int safeGrabCompletionThreshold = 6;
        OverflowRecoveryState recoveryState = OverflowRecoveryState.LiftToHomeZ;

        public bool ModuleComplete { get; private set; }

        public PickPlaceXYZOverflowRecoveryModuleController(PickPlaceXYZRecoveryDecision decision)
        {
            pickingState = decision.PickingState;
            grabState = decision.GrabState;
            counter = decision.Counter;
            exitBox = decision.ExitBox;
            previousGrabState = grabState;
            safeGrabCompletionThreshold = decision.SafeGrabCompletionThreshold;
            grabTimer.PT = 1000;
        }

        public override void Execute(int elapsedMilliseconds)
        {
            ftPartAtPlace.CLK(!partAtPlace.Value);
            ftBoxAtPlace.CLK(!boxAtPlace.Value);

            rtPartAtPlace.CLK(!partAtPlace.Value);
            rtBoxAtPlace.CLK(!boxAtPlace.Value);

            partConveyorForward.Value = false;
            partConveyorBackward.Value = false;
            boxConveyorForward.Value = false;
            boxConveyorBackward.Value = false;
            exitConveyor.Value = false;

            if (recoveryState == OverflowRecoveryState.LiftToHomeZ)
            {
                ExecuteLiftToHomeZ();
                return;
            }

            if (recoveryState == OverflowRecoveryState.Complete)
            {
                ModuleComplete = true;
                return;
            }

            if (pickingState == State.State0)
            {
                grabState = State.State0;

                if (!partAtPlace.Value && !boxAtPlace.Value && counter < 3)
                    pickingState = State.State1;
            }
            else if (pickingState == State.State1)
            {
                c.Value = false;

                spX.Value = 8.3f;
                spY.Value = 5.5f;

                if (Near(posX.Value, spX.Value, 0.01f) && Near(posY.Value, spY.Value, 0.01f))
                {
                    grabState = State.State1;
                    pickingState = State.State2;
                }
            }
            else if (pickingState == State.State2)
            {
                if (grabState == State.State0)
                    pickingState = State.State3;
            }
            else if (pickingState == State.State3)
            {
                if (counter == 0)
                {
                    spX.Value = 3.1f;
                    spY.Value = 3.8f;
                }
                else if (counter == 1)
                {
                    spX.Value = 3.1f;
                    spY.Value = 6.7f;
                }
                else if (counter == 2)
                {
                    c.Value = true;

                    spX.Value = 3.1f;
                    spY.Value = 5.3f;
                }

                if (Near(posX.Value, spX.Value, 0.01f) && Near(posY.Value, spY.Value, 0.01f))
                    pickingState = State.State4;
            }
            else if (pickingState == State.State4)
            {
                if (counter == 0 || counter == 1)
                {
                    spZ.Value = 10f;
                }
                else if (counter == 2)
                {
                    spZ.Value = 5f;
                }

                if (Near(posZ.Value, spZ.Value, 0.01f))
                {
                    grab.Value = false;

                    counter++;

                    pickingState = State.State5;
                }
            }
            else if (pickingState == State.State5)
            {
                spZ.Value = 0f;

                if (Near(posZ.Value, spZ.Value, 0.01f))
                    pickingState = State.State0;
            }

            if (grabState == State.State0)
            {
            }
            else if (grabState == State.State1)
            {
                spZ.Value = 5.3f;

                if (detected.Value)
                {
                    spZ.Value = posZ.Value;
                    grabState = State.State2;
                }
            }
            else if (grabState == State.State2)
            {
                grab.Value = true;

                grabTimer.IN = true;

                if (grabTimer.Q)
                {
                    grabTimer.IN = false;
                    grabState = State.State3;
                }
            }
            else if (grabState == State.State3)
            {
                spZ.Value = 0f;

                if (Near(spZ.Value, posZ.Value, 0.01f))
                    grabState = State.State0;
            }

            if (grabState == State.State2)
            {
                partConveyorForward.Value = false;
                partConveyorBackward.Value = true;
            }
            else if (partAtPlace.Value)
            {
                partConveyorForward.Value = true;
            }

            if (counter == 3)
            {
                boxConveyorForward.Value = true;
                exitConveyor.Value = true;

                if (ftBoxAtPlace.Q)
                {
                    counter = 0;
                    exitConveyor.Value = false;
                    exitBox++;
                }
            }
            else
            {
                if (boxAtPlace.Value)
                {
                    boxConveyorForward.Value = true;
                    exitConveyor.Value = true;
                }
            }

            UpdateSafeGrabCount();
        }

        void ExecuteLiftToHomeZ()
        {
            spZ.Value = 0f;

            if (!Near(posZ.Value, spZ.Value, 0.01f))
            {
                partConveyorForward.Value = false;
                partConveyorBackward.Value = true;
                return;
            }

            recoveryState = OverflowRecoveryState.SafePickPlace;
        }

        public PickPlaceXYZRecoveryDecision CreateContinuationDecision()
        {
            return new PickPlaceXYZRecoveryDecision
            {
                PickingState = pickingState,
                GrabState = grabState,
                Counter = counter,
                ExitBox = exitBox,
                StopExitBox = 2,
                StateIdentificationSatisfied = true,
                RecoveryModule = RecoveryModule.BenignResume,
                Reason = "Continuation from completed overflow recovery module."
            };
        }

        void UpdateSafeGrabCount()
        {
            bool completedGrabTransition =
                previousGrabState == State.State2
                && grabState == State.State3;
            previousGrabState = grabState;

            if (!completedGrabTransition)
                return;

            safeGrabMomentCount++;
            Debug.WriteLine($"Overflow recovery safe grab completion {safeGrabMomentCount}/{safeGrabCompletionThreshold}.");

            if (safeGrabMomentCount >= safeGrabCompletionThreshold)
            {
                recoveryState = OverflowRecoveryState.Complete;
                ModuleComplete = true;
            }
        }

        bool Near(float val1, float val2, float delta)
        {
            return Math.Abs(val1 - val2) < delta;
        }
    }

    public sealed class PickPlaceXYZMisalignmentBeltConveyorRecoveryModuleController : Controller
    {
        MemoryBit partConveyorForward = MemoryMap.Instance.GetBit("Belt Conveyor (4m) 1 (+)", MemoryType.Output);
        MemoryBit partConveyorBackward = MemoryMap.Instance.GetBit("Belt Conveyor (4m) 1 (-)", MemoryType.Output);
        MemoryBit boxConveyorForward = MemoryMap.Instance.GetBit("Roller Conveyor (6m) 1 (+)", MemoryType.Output);
        MemoryBit boxConveyorBackward = MemoryMap.Instance.GetBit("Roller Conveyor (6m) 1 (-)", MemoryType.Output);
        MemoryBit exitConveyor = MemoryMap.Instance.GetBit("Exit conveyor", MemoryType.Output);
        MemoryBit grab = MemoryMap.Instance.GetBit("Grab", MemoryType.Output);
        MemoryFloat spZ = MemoryMap.Instance.GetFloat("SP Z", MemoryType.Output);
        MemoryFloat posZ = MemoryMap.Instance.GetFloat("Z", MemoryType.Input);

        MemoryBit partAtPlace = MemoryMap.Instance.GetBit("Part at place", MemoryType.Input);

        int counter;
        int exitBox;
        int stopExitBox;

        public bool ModuleComplete { get; private set; }

        public PickPlaceXYZMisalignmentBeltConveyorRecoveryModuleController(PickPlaceXYZRecoveryDecision decision)
        {
            counter = decision.Counter;
            exitBox = decision.ExitBox;
            stopExitBox = decision.StopExitBox;
        }

        public override void Execute(int elapsedMilliseconds)
        {
            partConveyorForward.Value = false;
            partConveyorBackward.Value = false;
            boxConveyorForward.Value = false;
            boxConveyorBackward.Value = false;
            exitConveyor.Value = false;
            grab.Value = false;

            spZ.Value = 0f;

            if (!Near(posZ.Value, 0f, 0.01f))
                return;

            if (partAtPlace.Value)
            {
                ModuleComplete = true;
                return;
            }

            partConveyorBackward.Value = true;
        }

        bool Near(float val1, float val2, float delta)
        {
            return Math.Abs(val1 - val2) < delta;
        }

        public PickPlaceXYZRecoveryDecision CreateContinuationDecision()
        {
            return new PickPlaceXYZRecoveryDecision
            {
                PickingState = State.State0,
                GrabState = State.State0,
                Counter = counter,
                ExitBox = exitBox,
                StopExitBox = 1,
                StateIdentificationSatisfied = true,
                RecoveryModule = RecoveryModule.BenignResume,
                Reason = "Continuation from completed misalignment_beltconveyor recovery module."
            };
        }
    }

    public sealed class PickPlaceXYZUnderflowRecoveryModuleController : Controller
    {
        MemoryBit partConveyorForward = MemoryMap.Instance.GetBit("Belt Conveyor (4m) 1 (+)", MemoryType.Output);
        MemoryBit partConveyorBackward = MemoryMap.Instance.GetBit("Belt Conveyor (4m) 1 (-)", MemoryType.Output);
        MemoryBit boxConveyorForward = MemoryMap.Instance.GetBit("Roller Conveyor (6m) 1 (+)", MemoryType.Output);
        MemoryBit boxConveyorBackward = MemoryMap.Instance.GetBit("Roller Conveyor (6m) 1 (-)", MemoryType.Output);
        MemoryBit exitConveyor = MemoryMap.Instance.GetBit("Exit conveyor", MemoryType.Output);
        MemoryBit grab = MemoryMap.Instance.GetBit("Grab", MemoryType.Output);
        MemoryBit c = MemoryMap.Instance.GetBit("C +", MemoryType.Output);

        MemoryBit boxAtPlace = MemoryMap.Instance.GetBit("Box at place", MemoryType.Input);
        FTRIG rtBoxAtPlace = new FTRIG();

        readonly State pickingState;
        readonly State grabState;
        readonly int counter;
        readonly int exitBox;
        readonly int stopExitBox;
        readonly bool overrideCFalse;
        bool movingForwardToLoadingArea;

        public bool ModuleComplete { get; private set; }

        public PickPlaceXYZUnderflowRecoveryModuleController(PickPlaceXYZRecoveryDecision decision)
        {
            pickingState = decision.PickingState;
            grabState = decision.GrabState;
            counter = decision.Counter;
            exitBox = decision.ExitBox;
            stopExitBox = decision.StopExitBox;
            overrideCFalse = decision.PickingState == State.State3;
        }

        public override void Execute(int elapsedMilliseconds)
        {
            rtBoxAtPlace.CLK(!boxAtPlace.Value);

            partConveyorForward.Value = false;
            partConveyorBackward.Value = false;
            boxConveyorForward.Value = false;
            boxConveyorBackward.Value = false;
            exitConveyor.Value = false;

            if (overrideCFalse)
                c.Value = false;

            if (!movingForwardToLoadingArea)
            {
                if (rtBoxAtPlace.Q)
                    movingForwardToLoadingArea = true;
                else
                {
                    boxConveyorBackward.Value = true;
                    return;
                }
            }

            if (boxAtPlace.Value)
            {
                ModuleComplete = true;
                return;
            }

            boxConveyorForward.Value = true;
        }

        public PickPlaceXYZRecoveryDecision CreateContinuationDecision()
        {
            return new PickPlaceXYZRecoveryDecision
            {
                PickingState = pickingState,
                GrabState = grabState,
                Counter = counter,
                ExitBox = exitBox,
                StopExitBox = stopExitBox,
                StateIdentificationSatisfied = true,
                RecoveryModule = RecoveryModule.BenignResume,
                Reason = "Continuation from completed underflow recovery module."
            };
        }
    }

    public sealed class PickPlaceXYZMisalignmentBoxRecoveryModuleController : Controller
    {
        enum GrabReleaseStep
        {
            MoveAbovePickup,
            LowerToPickup,
            CloseGripper,
            LiftAfterPickup,
            MoveAbovePlace,
            LowerToPlace,
            OpenGripper,
            LiftAfterPlace
        }

        MemoryBit partConveyorForward = MemoryMap.Instance.GetBit("Belt Conveyor (4m) 1 (+)", MemoryType.Output);
        MemoryBit partConveyorBackward = MemoryMap.Instance.GetBit("Belt Conveyor (4m) 1 (-)", MemoryType.Output);
        MemoryBit boxConveyorForward = MemoryMap.Instance.GetBit("Roller Conveyor (6m) 1 (+)", MemoryType.Output);
        MemoryBit boxConveyorBackward = MemoryMap.Instance.GetBit("Roller Conveyor (6m) 1 (-)", MemoryType.Output);
        MemoryBit exitConveyor = MemoryMap.Instance.GetBit("Exit conveyor", MemoryType.Output);
        MemoryBit grab = MemoryMap.Instance.GetBit("Grab", MemoryType.Output);
        MemoryBit c = MemoryMap.Instance.GetBit("C +", MemoryType.Output);
        MemoryFloat spX = MemoryMap.Instance.GetFloat("SP X", MemoryType.Output);
        MemoryFloat spY = MemoryMap.Instance.GetFloat("SP Y", MemoryType.Output);
        MemoryFloat spZ = MemoryMap.Instance.GetFloat("SP Z", MemoryType.Output);
        MemoryFloat posX = MemoryMap.Instance.GetFloat("X", MemoryType.Input);
        MemoryFloat posY = MemoryMap.Instance.GetFloat("Y", MemoryType.Input);
        MemoryFloat posZ = MemoryMap.Instance.GetFloat("Z", MemoryType.Input);
        MemoryBit boxAtPlace = MemoryMap.Instance.GetBit("Box at place", MemoryType.Input);
        FTRIG rtBoxAtPlace = new FTRIG();

        readonly PickPlaceXYZGrabReleaseOperation[] operations;
        readonly int counter;
        readonly int exitBox;
        readonly int stopExitBox;
        int operationIndex;
        TON grabTimer = new TON();
        bool movingForwardToLoadingArea;
        bool rollerConveyorRecoveryComplete;
        GrabReleaseStep step = GrabReleaseStep.MoveAbovePickup;

        public bool ModuleComplete { get; private set; }

        public PickPlaceXYZMisalignmentBoxRecoveryModuleController(PickPlaceXYZRecoveryDecision decision)
        {
            operations = decision.GrabReleaseOperations ?? new PickPlaceXYZGrabReleaseOperation[0];
            counter = decision.Counter;
            exitBox = decision.ExitBox;
            stopExitBox = decision.StopExitBox;
            grabTimer.PT = 1000;
        }

        public override void Execute(int elapsedMilliseconds)
        {
            rtBoxAtPlace.CLK(!boxAtPlace.Value);

            partConveyorForward.Value = false;
            partConveyorBackward.Value = false;
            boxConveyorForward.Value = false;
            boxConveyorBackward.Value = false;
            exitConveyor.Value = false;
            c.Value = false;

            // Temporarily bypass the initial roller conveyor recovery phase.
            // if (!rollerConveyorRecoveryComplete)
            // {
            //     ExecuteRollerConveyorRecovery();
            //     return;
            // }

            if (operationIndex >= operations.Length)
            {
                ModuleComplete = true;
                return;
            }

            if (ExecuteGrabReleaseOperation(operations[operationIndex], elapsedMilliseconds))
            {
                operationIndex++;
                step = GrabReleaseStep.MoveAbovePickup;
            }
        }

        void ExecuteRollerConveyorRecovery()
        {
            grab.Value = false;
            spZ.Value = 0f;

            if (!movingForwardToLoadingArea)
            {
                if (rtBoxAtPlace.Q)
                    movingForwardToLoadingArea = true;
                else
                {
                    boxConveyorBackward.Value = true;
                    return;
                }
            }

            if (boxAtPlace.Value)
            {
                rollerConveyorRecoveryComplete = true;
                return;
            }

            boxConveyorForward.Value = true;
        }

        bool ExecuteGrabReleaseOperation(PickPlaceXYZGrabReleaseOperation operation, int elapsedMilliseconds)
        {
            if (step == GrabReleaseStep.MoveAbovePickup)
            {
                grab.Value = false;
                c.Value = operation.GrabCValue;
                MoveTo(operation.PickupX, operation.PickupY, 0f);

                if (Near(posX.Value, operation.PickupX, 0.01f)
                    && Near(posY.Value, operation.PickupY, 0.01f)
                    && Near(posZ.Value, 0f, 0.01f))
                    step = GrabReleaseStep.LowerToPickup;

                return false;
            }

            if (step == GrabReleaseStep.LowerToPickup)
            {
                grab.Value = false;
                c.Value = operation.GrabCValue;
                MoveTo(operation.PickupX, operation.PickupY, operation.PickupZ);

                if (Near(posZ.Value, operation.PickupZ, 0.01f))
                    step = GrabReleaseStep.CloseGripper;

                return false;
            }

            if (step == GrabReleaseStep.CloseGripper)
            {
                grab.Value = true;
                c.Value = operation.GrabCValue;
                grabTimer.IN = true;

                if (grabTimer.Q)
                {
                    grabTimer.IN = false;
                    step = GrabReleaseStep.LiftAfterPickup;
                }

                return false;
            }

            if (step == GrabReleaseStep.LiftAfterPickup)
            {
                grab.Value = true;
                c.Value = operation.ReleaseCValue;
                MoveTo(operation.PickupX, operation.PickupY, 0f);

                if (Near(posZ.Value, 0f, 0.01f))
                    step = GrabReleaseStep.MoveAbovePlace;

                return false;
            }

            if (step == GrabReleaseStep.MoveAbovePlace)
            {
                grab.Value = true;
                c.Value = operation.ReleaseCValue;
                MoveTo(operation.PlaceX, operation.PlaceY, 0f);

                if (Near(posX.Value, operation.PlaceX, 0.01f)
                    && Near(posY.Value, operation.PlaceY, 0.01f)
                    && Near(posZ.Value, 0f, 0.01f))
                    step = GrabReleaseStep.LowerToPlace;

                return false;
            }

            if (step == GrabReleaseStep.LowerToPlace)
            {
                grab.Value = true;
                c.Value = operation.ReleaseCValue;
                MoveTo(operation.PlaceX, operation.PlaceY, operation.PlaceZ);

                if (Near(posZ.Value, operation.PlaceZ, 0.01f))
                    step = GrabReleaseStep.OpenGripper;

                return false;
            }

            if (step == GrabReleaseStep.OpenGripper)
            {
                grab.Value = false;
                c.Value = operation.ReleaseCValue;
                step = GrabReleaseStep.LiftAfterPlace;

                return false;
            }

            grab.Value = false;
            c.Value = false;
            MoveTo(operation.PlaceX, operation.PlaceY, 0f);

            return Near(posZ.Value, 0f, 0.01f);
        }

        void MoveTo(float x, float y, float z)
        {
            spX.Value = x;
            spY.Value = y;
            spZ.Value = z;
        }

        bool Near(float val1, float val2, float delta)
        {
            return Math.Abs(val1 - val2) < delta;
        }

        public PickPlaceXYZRecoveryDecision CreateContinuationDecision()
        {
            return new PickPlaceXYZRecoveryDecision
            {
                PickingState = State.State0,
                GrabState = State.State0,
                Counter = 3,
                ExitBox = exitBox,
                StopExitBox = stopExitBox,
                StateIdentificationSatisfied = true,
                RecoveryModule = RecoveryModule.BenignResume,
                Reason = "Continuation from completed misalignment box recovery module."
            };
        }
    }

}
