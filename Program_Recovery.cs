//-----------------------------------------------------------------------------
// FACTORY I/O (SDK)
//
// Test harness for recovering PickPlaceXYZ after a pause.
//-----------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

using ScreenRecorderLib;

using EngineIO;

namespace Controllers
{
    class Program_Recovery
    {
        static Recorder _rec;

        public const int CycleTime = 8;
        public const int PauseAtExecutionCount = 1000;
        public const int MaxRecoveryExecutions = 4000;
        public const bool EnableRecording = false;

        static readonly string sceneName = "PickPlaceXYZ";
        static readonly string manipulationRoot = @"D:\Code\factoryio-sdk-master\factoryio-sdk-master\samples\Controllers\Manipulations";
        static readonly string recoveryScriptRoot = @"D:\Code\factoryio-sdk-master\factoryio-sdk-master\samples\Controllers\Recovery";
        static readonly string videoRoot = @"D:\Code\factoryio-sdk-master\factoryio-sdk-master\samples\Controllers\Videos\Recovery";
        static readonly string matchCsvPath = @"D:\Code\factoryio-sdk-master\factoryio-sdk-master\samples\Controllers\match.csv";
        static readonly string[] specificManipulationNameFragments =
        {
            "spZ", "195"
        };

        static void Main(string[] args)
        {
            string manipulationFolder = Path.Combine(manipulationRoot, sceneName);
            string[] manipulationFiles = ReadSystemPauseManipulationFiles(manipulationFolder);
            Debug.WriteLine($"Found {manipulationFiles.Length} system_pause manipulation classes.");
            InitializeRecoveryManifest();
            InitializeRecoveryTimingLog();

            MemoryBit start = MemoryMap.Instance.GetBit(MemoryMap.BitCount - 16, MemoryType.Output);
            MemoryBit pause = MemoryMap.Instance.GetBit(MemoryMap.BitCount - 48, MemoryType.Output);
            MemoryBit running = MemoryMap.Instance.GetBit(MemoryMap.BitCount - 16, MemoryType.Input);

            foreach (string manipulationFile in manipulationFiles)
            {
                string controllerName = Path.GetFileNameWithoutExtension(manipulationFile);
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
                    Debug.WriteLine($"Running manipulated controller: {controllerName}");

                    Stopwatch stopwatch = Stopwatch.StartNew();
                    int executionCount = 0;

                    while (!controller.stopSignal && executionCount < PauseAtExecutionCount)
                    {
                        MemoryMap.Instance.Update();

                        if (running.Value)
                        {
                            stopwatch.Stop();
                            controller.executionCount = executionCount;
                            controller.Execute((int)stopwatch.ElapsedMilliseconds);
                            executionCount++;
                            stopwatch.Restart();
                        }

                        Thread.Sleep(CycleTime);
                    }

                    pause.Value = true;
                    MemoryMap.Instance.Update();
                    Thread.Sleep(500);

                    Stopwatch setupStopwatch = Stopwatch.StartNew();

                    Stopwatch stateIdentificationStopwatch = Stopwatch.StartNew();
                    PickPlaceXYZSnapshot snapshot = PickPlaceXYZSnapshot.Read(controller);
                    PickPlaceXYZRecoveryDecision decision = PickPlaceXYZRecoveryStateDecider.Decide(snapshot);
                    stateIdentificationStopwatch.Stop();

                    Stopwatch scriptWriteStopwatch = Stopwatch.StartNew();
                    (string recoveryScriptPath, string recoveryClassName) = WriteRecoveryScript(controllerName, snapshot, decision);
                    scriptWriteStopwatch.Stop();

                    AppendRecoveryManifest(controllerName, recoveryClassName, recoveryScriptPath, decision);

                    Debug.WriteLine(
                        $"Recovery decision for {controllerName}: pickingState={decision.PickingState}, " +
                        $"grabState={decision.GrabState}, counter={decision.Counter}, reason={decision.Reason}");

                    Stopwatch compilationStopwatch = Stopwatch.StartNew();
                    Controller recoveryController = CreateRuntimeCompiledRecoveryController(
                        recoveryScriptPath,
                        recoveryClassName,
                        decision,
                        out bool runtimeCompilationSucceeded);
                    compilationStopwatch.Stop();
                    setupStopwatch.Stop();

                    AppendRecoveryTimingLog(
                        controllerName,
                        stateIdentificationStopwatch.Elapsed,
                        scriptWriteStopwatch.Elapsed,
                        compilationStopwatch.Elapsed,
                        setupStopwatch.Elapsed,
                        runtimeCompilationSucceeded);

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

        static string[] ReadSystemPauseManipulationFiles(string manipulationFolder)
        {
            if (!File.Exists(matchCsvPath))
                throw new FileNotFoundException("Could not find match.csv for recovery filtering.", matchCsvPath);

            return File.ReadLines(matchCsvPath)
                .Skip(1)
                .Select(ParseMatchRow)
                .Where(row => row.Label == "system_pause")
                .Where(row => MatchesSpecificNameFragments(Path.GetFileNameWithoutExtension(row.CsvName)))
                .Select(row => Path.Combine(manipulationFolder, Path.ChangeExtension(row.CsvName, ".cs")))
                .Where(path =>
                {
                    if (File.Exists(path))
                        return true;

                    Debug.WriteLine($"[SKIP] match.csv references missing manipulation file: {path}");
                    return false;
                })
                .Distinct()
                .ToArray();
        }

        static bool MatchesSpecificNameFragments(string manipulationName)
        {
            if (specificManipulationNameFragments.Length == 0)
                return true;

            return specificManipulationNameFragments.All(fragment =>
                manipulationName.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        static (string CsvName, string Label) ParseMatchRow(string line)
        {
            string[] columns = line.Split(',');
            if (columns.Length < 2)
                return (string.Empty, string.Empty);

            return (columns[0].Trim(), columns[1].Trim());
        }

        static void InitializeRecoveryManifest()
        {
            Directory.CreateDirectory(recoveryScriptRoot);
            string path = Path.Combine(recoveryScriptRoot, "recovery_manifest.csv");
            EnsureCsvHeader(path, "manipulation,recovery_class,recovery_script,picking_state,grab_state,counter,exit_box,reason");
        }

        static void InitializeRecoveryTimingLog()
        {
            Directory.CreateDirectory(recoveryScriptRoot);
            string path = Path.Combine(recoveryScriptRoot, "recovery_timing.csv");
            EnsureCsvHeader(path, "manipulation,state_identification_ms,script_write_ms,compilation_load_ms,total_setup_ms,runtime_compilation_succeeded");
        }

        static void EnsureCsvHeader(string path, string header)
        {
            if (File.Exists(path) && new FileInfo(path).Length > 0)
                return;

            File.WriteAllText(path, header + Environment.NewLine);
        }

        static void AppendRecoveryManifest(
            string manipulationName,
            string recoveryClassName,
            string recoveryScriptPath,
            PickPlaceXYZRecoveryDecision decision)
        {
            string path = Path.Combine(recoveryScriptRoot, "recovery_manifest.csv");
            string[] values =
            {
                manipulationName,
                recoveryClassName,
                recoveryScriptPath,
                decision.PickingState.ToString(),
                decision.GrabState.ToString(),
                decision.Counter.ToString(CultureInfo.InvariantCulture),
                decision.ExitBox.ToString(CultureInfo.InvariantCulture),
                decision.Reason
            };

            File.AppendAllText(path, string.Join(",", values.Select(EscapeCsv)) + Environment.NewLine);
        }

        static void AppendRecoveryTimingLog(
            string manipulationName,
            TimeSpan stateIdentificationElapsed,
            TimeSpan scriptWriteElapsed,
            TimeSpan compilationElapsed,
            TimeSpan totalSetupElapsed,
            bool runtimeCompilationSucceeded)
        {
            string path = Path.Combine(recoveryScriptRoot, "recovery_timing.csv");
            string[] values =
            {
                manipulationName,
                stateIdentificationElapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
                scriptWriteElapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
                compilationElapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
                totalSetupElapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
                runtimeCompilationSucceeded ? "true" : "false"
            };

            File.AppendAllText(path, string.Join(",", values.Select(EscapeCsv)) + Environment.NewLine);
        }

        static string EscapeCsv(string value)
        {
            if (value.Contains(",") || value.Contains("\"") || value.Contains(Environment.NewLine))
                return $"\"{value.Replace("\"", "\"\"")}\"";

            return value;
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
            builder.AppendLine($"                OverrideSpZ = {decision.OverrideSpZ.ToString().ToLowerInvariant()},");
            builder.AppendLine($"                RecoverySpZ = {decision.RecoverySpZ.ToString("G", CultureInfo.InvariantCulture)}f,");
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
            PickPlaceXYZRecoveryDecision fallbackDecision,
            out bool runtimeCompilationSucceeded)
        {
            try
            {
                string assemblyPath = CompileRecoveryScript(recoveryScriptPath, recoveryClassName);
                Assembly assembly = Assembly.LoadFrom(assemblyPath);
                Type recoveryType = assembly.GetType($"Controllers.{recoveryClassName}", true);
                Debug.WriteLine($"Loaded runtime-compiled recovery controller: {recoveryType.FullName}");
                runtimeCompilationSucceeded = true;
                return (Controller)Activator.CreateInstance(recoveryType);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Runtime compilation failed for {recoveryClassName}: {ex}");
                Debug.WriteLine("Falling back to compiled generic Recovery_PickPlaceXYZ.");
                runtimeCompilationSucceeded = false;
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

        public Recovery_PickPlaceXYZ(PickPlaceXYZRecoveryDecision decision)
        {
            pickingState = decision.PickingState;
            grabState = decision.GrabState;
            counter = decision.Counter;
            exitBox = decision.ExitBox;
            grabTimer.PT = 1000;

            if (decision.OverrideSpZ)
                spZ.Value = decision.RecoverySpZ;
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

            if (exitBox == 1)
                stopSignal = true;
        }

        bool Near(float val1, float val2, float delta)
        {
            return Math.Abs(val1 - val2) < delta;
        }
    }
}
