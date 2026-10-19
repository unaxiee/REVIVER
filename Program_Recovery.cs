//-----------------------------------------------------------------------------
// FACTORY I/O (SDK)
//
// Shared recovery runner for PickPlaceXYZ and AssemblerAnalog.
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
        public const int PauseAtExecutionCount = 1000;
        public const int MaxRecoveryExecutions = 1000;
        public const bool EnableRecording = false;
        public const bool EnableManipulatedControllerLogging = false;
        public static readonly bool EnableRecoveryTimingLogging = false;

        static readonly string controllerRoot = @"D:\Code\factoryio-sdk-master\factoryio-sdk-master\samples\Controllers";
        static readonly string sceneName = "AssemblerAnalog";
        static readonly string manipulationRoot = Path.Combine(controllerRoot, "Manipulations");
        static readonly string recoveryScriptRoot = Path.Combine(controllerRoot, "Recovery", sceneName);
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
        // Filenames without .cs, for the scene selected above. Both scenes use this list.
        // PickPlaceXYZ selections must also appear in clusters_csv_inventory.csv.
        static readonly string[] specificManipulationNames =
        {
            "AssemblerAnalog_setZ_10_0f_to_neg_1f_L114"
        };
        static readonly string[] specificManipulationNameFragments =
        {

        };
        static readonly string[] excludedManipulationNameFragments =
        {
        
        };

        sealed class RecoveryScene
        {
            public Func<string, ManipulationRun[]> SelectRuns { get; set; }
            public Func<Controller, ManipulationRun, RecoverySetup> Prepare { get; set; }
            public Action<string, string> EnhanceLog { get; set; }
        }

        sealed class RecoverySetup
        {
            public Func<Controller> CreateController { get; set; }
            public string Module { get; set; }
            public TimeSpan StateIdentificationElapsed { get; set; }
            public TimeSpan ModuleIdentificationElapsed { get; set; }
            public TimeSpan ScriptWriteElapsed { get; set; }
            public RecoveryRobustnessLog Robustness { get; set; }
        }

        static RecoveryScene GetRecoveryScene(string name)
        {
            var scenes = new Dictionary<string, RecoveryScene>(StringComparer.OrdinalIgnoreCase)
            {
                ["PickPlaceXYZ"] = new RecoveryScene
                {
                    SelectRuns = ReadInventoryManipulationRuns,
                    Prepare = PreparePickPlaceXYZRecovery,
                    EnhanceLog = EnhanceRecoveryLog
                },
                ["AssemblerAnalog"] = new RecoveryScene
                {
                    SelectRuns = ReadSelectedManipulationRuns,
                    Prepare = PrepareAssemblerAnalogRecovery,
                    EnhanceLog = (original, enhanced) => { }
                }
            };
            if (!scenes.TryGetValue(name, out RecoveryScene scene))
                throw new InvalidOperationException($"Unsupported recovery scene '{name}'.");
            return scene;
        }

        static void Main(string[] args)
        {
            RecoveryScene scene = GetRecoveryScene(sceneName);
            string manipulationFolder = Path.Combine(manipulationRoot, sceneName);
            ManipulationRun[] runs = scene.SelectRuns(manipulationFolder);
            if (runs.Length == 0)
                throw new InvalidOperationException($"No recovery manipulations selected for {sceneName}.");
            // Resolve all selected classes before changing the simulation.
            foreach (ManipulationRun run in runs)
            {
                string name = Path.GetFileNameWithoutExtension(run.FilePath);
                Type type = Type.GetType("Controllers." + name, true);
                if (!typeof(Controller).IsAssignableFrom(type))
                    throw new InvalidOperationException($"{name} is not a controller.");
            }
            InitializeRecoveryTimingLog();
            MemoryBit start = MemoryMap.Instance.GetBit(MemoryMap.BitCount - 16, MemoryType.Output);
            MemoryBit pause = MemoryMap.Instance.GetBit(MemoryMap.BitCount - 48, MemoryType.Output);
            MemoryBit running = MemoryMap.Instance.GetBit(MemoryMap.BitCount - 16, MemoryType.Input);
            try
            {
                foreach (ManipulationRun run in runs)
                {
                    string name = Path.GetFileNameWithoutExtension(run.FilePath);
                    try
                    {
                        pause.Value = false;
                        SwitchToRun(start);
                        if (EnableRecording)
                            CreateRecording(Path.Combine(videoRoot, sceneName), name);
                        Controller manipulated = (Controller)Activator.CreateInstance(Type.GetType("Controllers." + name, true));
                        Debug.WriteLine($"Running {sceneName} manipulation: {name}");
                        string relativeFolder = Path.GetDirectoryName(Path.GetRelativePath(manipulationFolder, run.FilePath)) ?? string.Empty;
                        string csvPath = Path.Combine(recoveryOriginalLogRoot, sceneName, relativeFolder, name + ".csv");
                        string enhancedPath = Path.Combine(recoveryEnhancedLogRoot, sceneName, relativeFolder, name + ".csv");
                        List<CsvSignal> signals = EnableManipulatedControllerLogging
                            ? DiscoverLoggedSignals(run.FilePath, manipulated) : new List<CsvSignal>();
                        StreamWriter writer = null;
                        try
                        {
                            if (EnableManipulatedControllerLogging)
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(csvPath));
                                writer = new StreamWriter(csvPath, false);
                                WriteCsvHeader(writer, signals);
                            }
                            RunControllerScans(manipulated, running, PauseAtExecutionCount, writer, signals);
                        }
                        finally
                        {
                            writer?.Dispose();
                        }
                        if (EnableManipulatedControllerLogging)
                            scene.EnhanceLog(csvPath, enhancedPath);

                        pause.Value = true;
                        MemoryMap.Instance.Update();
                        Thread.Sleep(500);
                        RecoverySetup setup = scene.Prepare(manipulated, run);
                        if (setup.CreateController == null)
                        {
                            AppendSetupTiming(run, setup, TimeSpan.Zero, TimeSpan.Zero);
                            continue;
                        }
                        Stopwatch compilation = Stopwatch.StartNew();
                        Controller recovery = setup.CreateController();
                        compilation.Stop();
                        pause.Value = false;
                        MemoryMap.Instance.Update();
                        try
                        {
                            RunControllerScans(recovery, running, MaxRecoveryExecutions);
                        }
                        finally
                        {
                            (recovery as IDisposable)?.Dispose();
                        }
                        AppendSetupTiming(run, setup, compilation.Elapsed, GetAdditionalRecoveryModuleExecutionElapsed(recovery));
                    }
                    finally
                    {
                        pause.Value = false;
                        Shutdown(start);
                        if (EnableRecording)
                            EndRecording();
                    }
                }
            }
            finally
            {
                MemoryMap.Instance.Dispose();
            }
        }

        static ManipulationRun[] ReadSelectedManipulationRuns(string folder)
        {
            if (specificManipulationNames.Length == 0)
                throw new InvalidOperationException("Configure specificManipulationNames before running recovery.");
            return specificManipulationNames.Distinct(StringComparer.OrdinalIgnoreCase).Select(name =>
            {
                if (Path.GetFileName(name) != name || !name.StartsWith(sceneName + "_", StringComparison.Ordinal))
                    throw new ArgumentException($"Invalid {sceneName} manipulation name: {name}");
                string path = Path.Combine(folder, name + ".cs");
                if (!File.Exists(path))
                    throw new FileNotFoundException("Recovery manipulation not found.", path);
                return new ManipulationRun { FilePath = path, Label = "", Split = "" };
            }).ToArray();
        }

        static RecoverySetup PrepareAssemblerAnalogRecovery(Controller controller, ManipulationRun run)
        {
            string name = Path.GetFileNameWithoutExtension(run.FilePath);
            AssemblerAnalogSnapshot snapshot = AssemblerAnalogSnapshot.Read(controller);
            Stopwatch identification = Stopwatch.StartNew();
            AssemblerAnalogRecoveryDecision decision = AssemblerAnalogRecoveryStateDecider.Decide(snapshot);
            identification.Stop();
            Debug.WriteLine($"AssemblerAnalog identification: lids={decision.LidsState}, bases={decision.BasesState}; {decision.Reason}");
            TimeSpan moduleIdentificationElapsed = TimeSpan.Zero;
            if (!decision.StateIdentificationSatisfied)
            {
                Stopwatch moduleIdentification = Stopwatch.StartNew();
                decision = AssemblerAnalogRecoveryModuleIdentifier.Decide(snapshot, decision);
                moduleIdentification.Stop();
                moduleIdentificationElapsed = moduleIdentification.Elapsed;
                Debug.WriteLine($"AssemblerAnalog module identification: {decision.Reason}");
                if (!decision.ReverseBaseWithoutLid && !decision.RecoverSeparatedLidAndBase
                    && !decision.RecoverStackedLids && !decision.RecoverHeldLidWaitForBase
                    && !decision.RecoverTwoBasesTwoLids && !decision.RecoverAlternatingLidBase && !decision.RecoverHeldAssemblyTwoBases && !decision.RecoverMisplacedAssembly && !decision.RecoverBaseAndLid && !decision.RepositionBase)
                {
                    return new RecoverySetup
                    {
                        Module = "no module match",
                        StateIdentificationElapsed = identification.Elapsed,
                        ModuleIdentificationElapsed = moduleIdentificationElapsed
                    };
                }
            }
            Stopwatch writing = Stopwatch.StartNew();
            var script = AssemblerAnalogRecoveryScriptWriter.Write(
                Path.Combine(controllerRoot, "Scenes", "AssemblerAnalog.cs"), recoveryScriptRoot, name, snapshot, decision);
            writing.Stop();
            return new RecoverySetup
            {
                Module = decision.RepositionBase ? "RepositionBase"
                    : decision.RecoverBaseAndLid ? "RetrieveBaseAndLid"
                    : decision.RecoverMisplacedAssembly ? "MisplacedAssembly"
                    : decision.RecoverHeldAssemblyTwoBases ? "HeldAssemblyTwoBases"
                    : decision.RecoverAlternatingLidBase ? "AlternatingLidBase"
                    : decision.RecoverTwoBasesTwoLids ? "TwoBasesTwoLids"
                    : decision.RecoverHeldLidWaitForBase ? "HeldLidWaitForBase"
                    : decision.RecoverStackedLids ? "StackedLids"
                    : decision.RecoverSeparatedLidAndBase ? "SeparatedLidAndBase"
                    : decision.ReverseBaseWithoutLid
                    ? (decision.ClearBaseBeforeReverse ? "ClearBaseThenReverse" : "BaseWithoutLidReverse")
                    : "BenignResume",
                StateIdentificationElapsed = identification.Elapsed,
                ModuleIdentificationElapsed = moduleIdentificationElapsed,
                ScriptWriteElapsed = writing.Elapsed,
                CreateController = () =>
                {
                    string assemblyPath = CompileRecoveryScript(script.Path, script.ClassName);
                    Type type = Assembly.LoadFrom(assemblyPath).GetType("Controllers." + script.ClassName, true);
                    return (Controller)Activator.CreateInstance(type);
                }
            };
        }

        static RecoverySetup PreparePickPlaceXYZRecovery(Controller controller, ManipulationRun run)
        {
            string name = Path.GetFileNameWithoutExtension(run.FilePath);
            PickPlaceXYZSnapshot snapshot = PickPlaceXYZSnapshot.Read(controller);
            Stopwatch identification = Stopwatch.StartNew();
            PickPlaceXYZRecoveryDecision decision = PickPlaceXYZRecoveryStateDecider.Decide(snapshot, run.Label);
            identification.Stop();
            var setup = new RecoverySetup { StateIdentificationElapsed = identification.Elapsed };
            if (!decision.StateIdentificationSatisfied)
            {
                Stopwatch moduleIdentification = Stopwatch.StartNew();
                PickPlaceXYZRecoveryModuleDecision moduleDecision =
                    PickPlaceXYZRecoveryModuleIdentifier.Decide(snapshot, decision, run.Label, name);
                decision = moduleDecision.RecoveryDecision;
                moduleIdentification.Stop();
                setup.ModuleIdentificationElapsed = moduleIdentification.Elapsed;
                if (moduleDecision.ClassificationFailed)
                {
                    Debug.WriteLine($"[SKIP] Recovery module classification failed for {name}: {moduleDecision.Reason}");
                    setup.Module = "classify failed";
                    setup.Robustness = BuildFailedClassificationRobustnessLog(run.Label, moduleDecision.Classification);
                    return setup;
                }
                if (IsUnexpectedRecoveryModuleDecision(run.Label, decision.RecoveryModule))
                {
                    Debug.WriteLine($"[SKIP] Recovery module mismatch for {name}: {moduleDecision.Reason}");
                    setup.Module = decision.RecoveryModule.ToString();
                    setup.Robustness = BuildMismatchRobustnessLog(run.Label, decision.RecoveryModule, moduleDecision.Classification);
                    return setup;
                }
            }
            Stopwatch writing = Stopwatch.StartNew();
            var script = WriteRecoveryScript(name, snapshot, decision);
            writing.Stop();
            setup.ScriptWriteElapsed = writing.Elapsed;
            setup.Module = decision.RecoveryModule.ToString();
            setup.CreateController = () => CreateRuntimeCompiledRecoveryController(script.Path, script.ClassName, decision);
            Debug.WriteLine($"PickPlaceXYZ identification: pickingState={decision.PickingState}, grabState={decision.GrabState}; {decision.Reason}");
            return setup;
        }

        static void AppendSetupTiming(ManipulationRun run, RecoverySetup setup, TimeSpan compilation, TimeSpan additionalExecution)
        {
            AppendRecoveryTimingLog(Path.GetFileNameWithoutExtension(run.FilePath), run.Split, run.Label,
                setup.Module, setup.StateIdentificationElapsed, setup.ModuleIdentificationElapsed,
                setup.ScriptWriteElapsed, compilation, additionalExecution, setup.Robustness);
        }

        static void RunControllerScans(Controller controller, MemoryBit running, int maxExecutions,
            StreamWriter writer = null, List<CsvSignal> signals = null)
        {
            Stopwatch cycle = Stopwatch.StartNew();
            Stopwatch noScans = Stopwatch.StartNew();
            Stopwatch elapsed = Stopwatch.StartNew();
            int count = 0;
            while (!controller.stopSignal && count < maxExecutions)
            {
                MemoryMap.Instance.Update();
                if (running.Value)
                {
                    controller.executionCount = count;
                    controller.Execute((int)cycle.ElapsedMilliseconds);
                    if (writer != null && count % CsvLogIntervalExecutions == 0)
                        WriteCsvRowIfEnabled(writer, elapsed.ElapsedMilliseconds, count, signals);
                    controller.captureSignal = false;
                    count++;
                    cycle.Restart();
                    noScans.Restart();
                }
                else if (noScans.ElapsedMilliseconds > 15000)
                    throw new TimeoutException("Factory I/O has not reported running for 15 seconds.");
                Thread.Sleep(CycleTime);
            }
            MemoryMap.Instance.Update();
            if (writer != null)
                WriteCsvRowIfEnabled(writer, elapsed.ElapsedMilliseconds, count, signals);
            Debug.WriteLine($"Executed {count} scans: {controller.GetType().Name}");
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
                    if (memoryInt == null)
                        throw new InvalidOperationException($"Missing CSV log integer tag '{memoryName}'.");
                    signals.Add(new CsvSignal(fieldName, () => memoryInt.Value.ToString(CultureInfo.InvariantCulture)));
                }
            }

            AddControllerFieldSignal<int>(signals, controller, "counter", value => value.ToString(CultureInfo.InvariantCulture));
            foreach (string name in new[] { "stateLids", "stateBases", "pickingState", "grabState" })
                AddControllerFieldSignal<State>(signals, controller, name, value => value.ToString());

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
            if (!EnableRecoveryTimingLogging)
                return;
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
            if (!EnableRecoveryTimingLogging)
                return;
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
    <Reference Include=""Engine IO"">
      <HintPath>{EscapeXml(typeof(MemoryMap).Assembly.Location)}</HintPath>
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
}
