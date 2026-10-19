//-----------------------------------------------------------------------------
// FACTORY I/O (SDK)
//
// PickPlaceXYZ recovery module identification and recovery controller implementations.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using EngineIO;

namespace Controllers
{
    public sealed class PickPlaceXYZRecoveryDecision
    {
        public State PickingState { get; set; }
        public State GrabState { get; set; }
        public int Counter { get; set; }
        public int ExitBox { get; set; }
        public int StopExitBox { get; set; } = 1;
        public bool StateIdentificationSatisfied { get; set; }
        public RecoveryModule RecoveryModule { get; set; }
        public bool OverrideSpZ { get; set; }
        public float RecoverySpZ { get; set; }
        public int SafeGrabCompletionThreshold { get; set; } = 6;
        public PickPlaceXYZGrabReleaseOperation[] GrabReleaseOperations { get; set; }
        public string Reason { get; set; }
    }

    public struct PickPlaceXYZGrabReleaseOperation
    {
        public float PickupX { get; set; }
        public float PickupY { get; set; }
        public float PickupZ { get; set; }
        public float PlaceX { get; set; }
        public float PlaceY { get; set; }
        public float PlaceZ { get; set; }
        public bool GrabCValue { get; set; }
        public bool ReleaseCValue { get; set; }

        public PickPlaceXYZGrabReleaseOperation(
            float pickupX,
            float pickupY,
            float pickupZ,
            float placeX,
            float placeY,
            float placeZ,
            bool grabCValue,
            bool releaseCValue)
        {
            PickupX = pickupX;
            PickupY = pickupY;
            PickupZ = pickupZ;
            PlaceX = placeX;
            PlaceY = placeY;
            PlaceZ = placeZ;
            GrabCValue = grabCValue;
            ReleaseCValue = releaseCValue;
        }
    }

    public enum RecoveryModule
    {
        BenignResume,
        Overflow,
        MisalignmentBeltConveyor,
        Underflow,
        MisalignmentFirstBox,
        MisalignmentSecondBox,
        MisalignmentThirdBox
    }

    public sealed class PickPlaceXYZRecoveryModuleDecision
    {
        public PickPlaceXYZRecoveryDecision RecoveryDecision { get; set; }
        public string Reason { get; set; }
        public bool ClassificationFailed { get; set; }
        public PickPlaceXYZRecoveryModuleClassification Classification { get; set; }
    }

    public static class PickPlaceXYZRecoveryModuleIdentifier
    {
        public static PickPlaceXYZRecoveryModuleDecision Decide(
            PickPlaceXYZSnapshot snapshot,
            PickPlaceXYZRecoveryDecision stateDecision,
            string recoveryCase,
            string controllerName = null)
        {
            PickPlaceXYZRecoveryModuleClassification classification =
                PickPlaceXYZRecoveryModuleLogClassifier.TryClassify(controllerName);
            string classificationReason = classification.Reason;

            if (string.IsNullOrWhiteSpace(classification.ClassName))
                return WithClassification(
                    ClassifyFailedDecision(
                        stateDecision,
                        $"Stage 2 recovery module classification failed. {classificationReason}"),
                    classification);

            string selectedRecoveryCase = classification.ClassName;
            MisalignedBoxPositions boxPositions = MisalignedBoxPositions.FromEnhancedLog(classification.LogPath);

            if (IsOverflowRecoveryCase(stateDecision, selectedRecoveryCase))
                return WithClassification(
                    OverflowDecision(snapshot, stateDecision,
                        $"Stage 2 recovery module: overflow module selected. {classificationReason}"),
                    classification);

            if (IsMisalignmentBeltConveyorRecoveryCase(stateDecision, selectedRecoveryCase))
                return WithClassification(
                    MisalignmentBeltConveyorDecision(stateDecision,
                        $"Stage 2 recovery module: misalignment_beltconveyor module selected. {classificationReason}"),
                    classification);

            if (IsUnderflowRecoveryCase(stateDecision, selectedRecoveryCase))
                return WithClassification(
                    UnderflowDecision(snapshot, stateDecision,
                        $"Stage 2 recovery module: underflow module selected. {classificationReason}"),
                    classification);

            if (IsMisalignmentFirstBoxRecoveryCase(stateDecision, selectedRecoveryCase))
                return WithClassification(
                    MisalignmentBoxDecision(
                        stateDecision,
                        RecoveryModule.MisalignmentFirstBox,
                        BuildFirstBoxMisalignedOperations(boxPositions),
                        $"Stage 2 recovery module: misalignment_first_box module selected. {classificationReason} {boxPositions.Reason}"),
                    classification);

            if (IsMisalignmentSecondBoxRecoveryCase(stateDecision, selectedRecoveryCase))
                return WithClassification(
                    MisalignmentBoxDecision(
                        stateDecision,
                        RecoveryModule.MisalignmentSecondBox,
                        BuildSecondBoxMisalignedOperations(boxPositions),
                        $"Stage 2 recovery module: misalignment_second_box module selected. {classificationReason} {boxPositions.Reason}"),
                    classification);

            if (IsMisalignmentThirdBoxRecoveryCase(stateDecision, selectedRecoveryCase))
                return WithClassification(
                    MisalignmentBoxDecision(
                        stateDecision,
                        RecoveryModule.MisalignmentThirdBox,
                        BuildThirdBoxMisalignedOperations(boxPositions),
                        $"Stage 2 recovery module: misalignment_third_box module selected. {classificationReason} {boxPositions.Reason}"),
                    classification);

            return WithClassification(
                ClassifyFailedDecision(
                    stateDecision,
                    $"Stage 2 recovery module classification failed: unsupported class '{selectedRecoveryCase}'. {classificationReason}"),
                classification);
        }

        static bool IsOverflowRecoveryCase(
            PickPlaceXYZRecoveryDecision stateDecision,
            string recoveryCase)
        {
            return !stateDecision.StateIdentificationSatisfied && recoveryCase == "overflow";
        }

        static bool IsMisalignmentBeltConveyorRecoveryCase(
            PickPlaceXYZRecoveryDecision stateDecision,
            string recoveryCase)
        {
            return !stateDecision.StateIdentificationSatisfied && recoveryCase == "misalignment_beltconveyor";
        }

        static bool IsUnderflowRecoveryCase(
            PickPlaceXYZRecoveryDecision stateDecision,
            string recoveryCase)
        {
            return !stateDecision.StateIdentificationSatisfied && recoveryCase == "underflow";
        }

        static bool IsMisalignmentFirstBoxRecoveryCase(
            PickPlaceXYZRecoveryDecision stateDecision,
            string recoveryCase)
        {
            return !stateDecision.StateIdentificationSatisfied && recoveryCase == "misalignment_first_box";
        }

        static bool IsMisalignmentSecondBoxRecoveryCase(
            PickPlaceXYZRecoveryDecision stateDecision,
            string recoveryCase)
        {
            return !stateDecision.StateIdentificationSatisfied && recoveryCase == "misalignment_second_box";
        }

        static bool IsMisalignmentThirdBoxRecoveryCase(
            PickPlaceXYZRecoveryDecision stateDecision,
            string recoveryCase)
        {
            return !stateDecision.StateIdentificationSatisfied && recoveryCase == "misalignment_third_box";
        }

        static PickPlaceXYZRecoveryModuleDecision ModuleDecision(
            PickPlaceXYZRecoveryDecision stateDecision,
            RecoveryModule recoveryModule,
            string reason)
        {
            PickPlaceXYZRecoveryDecision recoveryDecision = CopyDecision(stateDecision);
            recoveryDecision.RecoveryModule = recoveryModule;
            recoveryDecision.Reason = $"{recoveryDecision.Reason} {reason}";

            return new PickPlaceXYZRecoveryModuleDecision
            {
                RecoveryDecision = recoveryDecision,
                Reason = reason
            };
        }

        static PickPlaceXYZRecoveryModuleDecision ClassifyFailedDecision(
            PickPlaceXYZRecoveryDecision stateDecision,
            string reason)
        {
            PickPlaceXYZRecoveryDecision recoveryDecision = CopyDecision(stateDecision);
            recoveryDecision.RecoveryModule = RecoveryModule.BenignResume;
            recoveryDecision.Reason = $"{recoveryDecision.Reason} {reason}";

            return new PickPlaceXYZRecoveryModuleDecision
            {
                RecoveryDecision = recoveryDecision,
                Reason = reason,
                ClassificationFailed = true
            };
        }

        static PickPlaceXYZRecoveryModuleDecision WithClassification(
            PickPlaceXYZRecoveryModuleDecision decision,
            PickPlaceXYZRecoveryModuleClassification classification)
        {
            decision.Classification = classification;
            return decision;
        }

        static PickPlaceXYZRecoveryModuleDecision OverflowDecision(
            PickPlaceXYZSnapshot snapshot,
            PickPlaceXYZRecoveryDecision stateDecision,
            string reason)
        {
            PickPlaceXYZRecoveryDecision recoveryDecision = CopyDecision(stateDecision);
            recoveryDecision.RecoveryModule = RecoveryModule.Overflow;
            recoveryDecision.GrabState = State.State0;
            recoveryDecision.StopExitBox = 2;
            recoveryDecision.Reason = $"{recoveryDecision.Reason} {reason}";

            if (snapshot.Grab)
            {
                recoveryDecision.PickingState = State.State3;
                recoveryDecision.SafeGrabCompletionThreshold = 2;
                recoveryDecision.Reason =
                    $"{recoveryDecision.Reason} Overflow start state override: grab is true, start from State (3, 0) with safe grab threshold = 2.";
            }
            else
            {
                recoveryDecision.PickingState = State.State0;
                recoveryDecision.SafeGrabCompletionThreshold = 4;
                recoveryDecision.Reason =
                    $"{recoveryDecision.Reason} Overflow start state override: grab is false, start from State (0, 0) with safe grab threshold = 4.";
            }

            return new PickPlaceXYZRecoveryModuleDecision
            {
                RecoveryDecision = recoveryDecision,
                Reason = reason
            };
        }

        static PickPlaceXYZRecoveryModuleDecision MisalignmentBeltConveyorDecision(
            PickPlaceXYZRecoveryDecision stateDecision,
            string reason)
        {
            PickPlaceXYZRecoveryDecision recoveryDecision = CopyDecision(stateDecision);
            recoveryDecision.RecoveryModule = RecoveryModule.MisalignmentBeltConveyor;
            recoveryDecision.Counter = 0;
            recoveryDecision.Reason =
                $"{recoveryDecision.Reason} {reason} Misalignment belt conveyor counter override: counter = 0.";

            return new PickPlaceXYZRecoveryModuleDecision
            {
                RecoveryDecision = recoveryDecision,
                Reason = reason
            };
        }

        static PickPlaceXYZRecoveryModuleDecision UnderflowDecision(
            PickPlaceXYZSnapshot snapshot,
            PickPlaceXYZRecoveryDecision stateDecision,
            string reason)
        {
            PickPlaceXYZRecoveryDecision recoveryDecision = CopyDecision(stateDecision);
            recoveryDecision.RecoveryModule = RecoveryModule.Underflow;
            recoveryDecision.PickingState = snapshot.Grab ? State.State3 : State.State0;
            recoveryDecision.GrabState = State.State0;
            recoveryDecision.Counter = 0;
            recoveryDecision.ExitBox = 0;
            recoveryDecision.Reason =
                $"{recoveryDecision.Reason} {reason} Underflow state override: grab is {(snapshot.Grab ? "true" : "false")}, resume from State ({(snapshot.Grab ? "3" : "0")}, 0). Underflow counter override: counter = 0. Underflow exitBox override: exitBox = 0.";

            return new PickPlaceXYZRecoveryModuleDecision
            {
                RecoveryDecision = recoveryDecision,
                Reason = reason
            };
        }

        static PickPlaceXYZRecoveryModuleDecision MisalignmentBoxDecision(
            PickPlaceXYZRecoveryDecision stateDecision,
            RecoveryModule recoveryModule,
            PickPlaceXYZGrabReleaseOperation[] operations,
            string reason)
        {
            PickPlaceXYZRecoveryDecision recoveryDecision = CopyDecision(stateDecision);
            recoveryDecision.RecoveryModule = recoveryModule;
            recoveryDecision.PickingState = State.State0;
            recoveryDecision.GrabState = State.State0;
            recoveryDecision.Counter = 0;
            recoveryDecision.ExitBox = 0;
            recoveryDecision.GrabReleaseOperations = operations;
            recoveryDecision.Reason =
                $"{recoveryDecision.Reason} {reason} Misalignment box recovery will run {recoveryDecision.GrabReleaseOperations.Length} configured grab-release operations before benign resume.";

            return new PickPlaceXYZRecoveryModuleDecision
            {
                RecoveryDecision = recoveryDecision,
                Reason = reason
            };
        }

        static PickPlaceXYZGrabReleaseOperation[] BuildFirstBoxMisalignedOperations(MisalignedBoxPositions boxPositions)
        {
            return new[]
            {
                new PickPlaceXYZGrabReleaseOperation(3.1f, 5.3f, 5f, 8.3f, 5.5f, 0.2f, true, false),
                new PickPlaceXYZGrabReleaseOperation(
                    boxPositions.Box0.X,
                    boxPositions.Box0.Y,
                    boxPositions.Box0.Z,
                    3.1f,
                    3.8f,
                    10f,
                    false,
                    false),
                new PickPlaceXYZGrabReleaseOperation(8.3f, 5.5f, 0.2f, 3.1f, 5.3f, 5f, false, true)
            };
        }

        static PickPlaceXYZGrabReleaseOperation[] BuildSecondBoxMisalignedOperations(MisalignedBoxPositions boxPositions)
        {
            return new[]
            {
                new PickPlaceXYZGrabReleaseOperation(3.1f, 5.3f, 5f, 8.3f, 5.5f, 0.2f, true, false),
                new PickPlaceXYZGrabReleaseOperation(
                    boxPositions.Box1.X,
                    boxPositions.Box1.Y,
                    boxPositions.Box1.Z,
                    3.1f,
                    6.7f,
                    10f,
                    false,
                    false),
                new PickPlaceXYZGrabReleaseOperation(8.3f, 5.5f, 0.2f, 3.1f, 5.3f, 5f, false, true)
            };
        }

        static PickPlaceXYZGrabReleaseOperation[] BuildThirdBoxMisalignedOperations(MisalignedBoxPositions boxPositions)
        {
            return new[]
            {
                new PickPlaceXYZGrabReleaseOperation(
                    boxPositions.Box2.X,
                    boxPositions.Box2.Y,
                    boxPositions.Box2.Z,
                    3.1f,
                    5.3f,
                    5f,
                    true,
                    true)
            };
        }

        struct BoxPosition
        {
            public float X { get; }
            public float Y { get; }
            public float Z { get; }

            public BoxPosition(float x, float y, float z)
            {
                X = x;
                Y = y;
                Z = z;
            }
        }

        sealed class MisalignedBoxPositions
        {
            public BoxPosition Box0 { get; private set; }
            public BoxPosition Box1 { get; private set; }
            public BoxPosition Box2 { get; private set; }
            public string Reason { get; private set; }

            public static MisalignedBoxPositions FromEnhancedLog(string logPath)
            {
                MisalignedBoxPositions defaults = Default();

                if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath))
                {
                    defaults.Reason = "Misalignment box inputs: no enhanced CSV was available, using default pickup coordinates.";
                    return defaults;
                }

                try
                {
                    using StreamReader reader = new StreamReader(logPath);
                    string headerLine = reader.ReadLine();
                    if (string.IsNullOrWhiteSpace(headerLine))
                    {
                        defaults.Reason = $"Misalignment box inputs: enhanced CSV '{Path.GetFileName(logPath)}' is empty, using default pickup coordinates.";
                        return defaults;
                    }

                    string lastLine = null;
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                            lastLine = line;
                    }

                    if (string.IsNullOrWhiteSpace(lastLine))
                    {
                        defaults.Reason = $"Misalignment box inputs: enhanced CSV '{Path.GetFileName(logPath)}' has no data rows, using default pickup coordinates.";
                        return defaults;
                    }

                    string[] headers = SplitCsvLine(headerLine).ToArray();
                    string[] values = SplitCsvLine(lastLine).ToArray();
                    Dictionary<string, int> columns = headers
                        .Select((name, index) => new { name, index })
                        .ToDictionary(item => item.name, item => item.index, StringComparer.OrdinalIgnoreCase);

                    return new MisalignedBoxPositions
                    {
                        Box0 = ReadBox(columns, values, "box0", defaults.Box0),
                        Box1 = ReadBox(columns, values, "box1", defaults.Box1),
                        Box2 = ReadBox(columns, values, "box2", defaults.Box2),
                        Reason = $"Misalignment box inputs loaded from last row of enhanced CSV '{Path.GetFileName(logPath)}'."
                    };
                }
                catch (Exception ex)
                {
                    defaults.Reason = $"Misalignment box inputs: failed to read enhanced CSV '{Path.GetFileName(logPath)}' ({ex.Message}), using default pickup coordinates.";
                    return defaults;
                }
            }

            static MisalignedBoxPositions Default()
            {
                return new MisalignedBoxPositions
                {
                    Box0 = new BoxPosition(3.1f, 3.8f, 10f),
                    Box1 = new BoxPosition(3.1f, 6.7f, 10f),
                    Box2 = new BoxPosition(3.1f, 5.3f, 5f),
                    Reason = "Misalignment box inputs: using default pickup coordinates."
                };
            }

            static BoxPosition ReadBox(
                Dictionary<string, int> columns,
                string[] values,
                string boxPrefix,
                BoxPosition fallback)
            {
                return new BoxPosition(
                    ReadFloat(columns, values, boxPrefix + "_x", fallback.X),
                    ReadFloat(columns, values, boxPrefix + "_y", fallback.Y),
                    ReadFloat(columns, values, boxPrefix + "_z", fallback.Z));
            }

            static float ReadFloat(
                Dictionary<string, int> columns,
                string[] values,
                string columnName,
                float fallback)
            {
                if (!columns.TryGetValue(columnName, out int index) || index >= values.Length)
                    return fallback;

                string raw = values[index];
                if (string.IsNullOrWhiteSpace(raw))
                    return fallback;

                if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                    return fallback;

                if (float.IsNaN(value) || float.IsInfinity(value))
                    return fallback;

                return value;
            }

            static IEnumerable<string> SplitCsvLine(string line)
            {
                List<string> fields = new List<string>();
                bool inQuotes = false;
                int start = 0;

                for (int i = 0; i < line.Length; i++)
                {
                    if (line[i] == '"')
                    {
                        if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                        {
                            i++;
                            continue;
                        }

                        inQuotes = !inQuotes;
                    }
                    else if (line[i] == ',' && !inQuotes)
                    {
                        fields.Add(UnescapeCsv(line.Substring(start, i - start)));
                        start = i + 1;
                    }
                }

                fields.Add(UnescapeCsv(line.Substring(start)));
                return fields;
            }

            static string UnescapeCsv(string value)
            {
                if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                    return value.Substring(1, value.Length - 2).Replace("\"\"", "\"");

                return value;
            }
        }

        static PickPlaceXYZRecoveryDecision CopyDecision(PickPlaceXYZRecoveryDecision decision)
        {
            return new PickPlaceXYZRecoveryDecision
            {
                PickingState = decision.PickingState,
                GrabState = decision.GrabState,
                Counter = decision.Counter,
                ExitBox = decision.ExitBox,
                StopExitBox = decision.StopExitBox,
                StateIdentificationSatisfied = decision.StateIdentificationSatisfied,
                RecoveryModule = decision.RecoveryModule,
                OverrideSpZ = decision.OverrideSpZ,
                RecoverySpZ = decision.RecoverySpZ,
                SafeGrabCompletionThreshold = decision.SafeGrabCompletionThreshold,
                GrabReleaseOperations = decision.GrabReleaseOperations,
                Reason = decision.Reason
            };
        }
    }

    public sealed class PickPlaceXYZRecoveryModuleClassification
    {
        public string ClassName { get; set; }
        public string LogPath { get; set; }
        public string Reason { get; set; }
        public string BestClassName { get; set; }
        public float? BestRobustness { get; set; }
        public Dictionary<string, float> RobustnessByClass { get; set; }
    }

    static class PickPlaceXYZRecoveryModuleLogClassifier
    {
        const float EvalEpsilon = 0.001f;

        static readonly string recoveryEnhancedLogRoot =
            @"D:\Code\factoryio-sdk-master\factoryio-sdk-master\samples\Controllers\Logs\Recovery\enhanced";
        static readonly string faultInjectionLogRoot =
            @"D:\Code\factoryio-sdk-master\factoryio-sdk-master\samples\Controllers\Logs\FaultInjection\PickPlaceXYZ";
        static readonly string specRoot =
            @"D:\Code\factoryio-sdk-master\factoryio-sdk-master\samples\Controllers\spec\general_4";

        static readonly string[] classes =
        {
            "overflow",
            "underflow",
            "misalignment_first_box",
            "misalignment_second_box",
            "misalignment_third_box",
            "misalignment_beltconveyor"
        };

        static readonly Dictionary<string, string> classSpecFiles = new Dictionary<string, string>
        {
            { "overflow", "fitted_overflow.json" },
            { "underflow", "fitted_underflow.json" },
            { "misalignment_first_box", "fitted_misalignment_first_box.json" },
            { "misalignment_second_box", "fitted_misalignment_second_box.json" },
            { "misalignment_third_box", "fitted_misalignment_third_box.json" },
            { "misalignment_beltconveyor", "fitted_misalignment_beltconveyor.json" }
        };

        public static PickPlaceXYZRecoveryModuleClassification TryClassify(string controllerName)
        {
            if (string.IsNullOrWhiteSpace(controllerName))
                return NoPrediction("No controller name was provided for recovery module log lookup.");

            string logPath = FindClassificationLog(controllerName);
            if (string.IsNullOrWhiteSpace(logPath))
                return NoPrediction($"No enhanced recovery or FaultInjection CSV log found for '{controllerName}'.");

            try
            {
                List<FittedSpec> specs = LoadSpecs();
                string[] signalList = specs[0].SignalList;
                CsvTrace trace = ReadTrace(logPath, signalList);
                float[] robustness = specs
                    .Select(spec => EvaluateFormula(spec.Formula, trace))
                    .ToArray();
                int bestIndex = ArgMax(robustness);
                float bestRho = robustness[bestIndex];
                Dictionary<string, float> robustnessByClass = classes
                    .Select((className, index) => new { className, rho = robustness[index] })
                    .ToDictionary(item => item.className, item => item.rho, StringComparer.OrdinalIgnoreCase);

                if (bestRho <= EvalEpsilon)
                    return NoPrediction(
                        $"Recovery log classifier found no satisfying module class for '{Path.GetFileName(logPath)}' (best={classes[bestIndex]}, rho={bestRho.ToString("G", CultureInfo.InvariantCulture)}).",
                        logPath,
                        robustnessByClass,
                        classes[bestIndex],
                        bestRho);

                return new PickPlaceXYZRecoveryModuleClassification
                {
                    ClassName = classes[bestIndex],
                    LogPath = logPath,
                    BestClassName = classes[bestIndex],
                    BestRobustness = bestRho,
                    RobustnessByClass = robustnessByClass,
                    Reason =
                        $"Recovery log classifier selected '{classes[bestIndex]}' from '{Path.GetFileName(logPath)}' with rho={bestRho.ToString("G", CultureInfo.InvariantCulture)}."
                };
            }
            catch (Exception ex)
            {
                return NoPrediction(
                    $"Recovery log classifier could not classify '{Path.GetFileName(logPath)}': {ex.Message}.",
                    logPath);
            }
        }

        static PickPlaceXYZRecoveryModuleClassification NoPrediction(
            string reason,
            string logPath = null,
            Dictionary<string, float> robustnessByClass = null,
            string bestClassName = null,
            float? bestRobustness = null)
        {
            return new PickPlaceXYZRecoveryModuleClassification
            {
                ClassName = null,
                LogPath = logPath,
                Reason = reason,
                BestClassName = bestClassName,
                BestRobustness = bestRobustness,
                RobustnessByClass = robustnessByClass
            };
        }

        static string FindClassificationLog(string controllerName)
        {
            string fileName = controllerName + ".csv";
            string recoveryLogPath = FindLog(recoveryEnhancedLogRoot, fileName);
            if (!string.IsNullOrWhiteSpace(recoveryLogPath))
                return recoveryLogPath;

            return FindLog(faultInjectionLogRoot, fileName);
        }

        static string FindLog(string root, string fileName)
        {
            if (!Directory.Exists(root))
                return null;

            return Directory
                .GetFiles(root, fileName, SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        static List<FittedSpec> LoadSpecs()
        {
            List<FittedSpec> specs = new List<FittedSpec>();
            string[] expectedSignals = null;

            foreach (string className in classes)
            {
                string specPath = Path.Combine(specRoot, classSpecFiles[className]);
                using FileStream stream = File.OpenRead(specPath);
                JsonDocument document = JsonDocument.Parse(stream);
                JsonElement stl = document.RootElement.GetProperty("stl");
                string[] signalList = stl.GetProperty("signal_list")
                    .EnumerateArray()
                    .Select(element => element.GetString())
                    .ToArray();

                if (expectedSignals == null)
                    expectedSignals = signalList;
                else if (!expectedSignals.SequenceEqual(signalList))
                    throw new InvalidOperationException($"Signal list mismatch in {Path.GetFileName(specPath)}.");

                specs.Add(new FittedSpec
                {
                    ClassName = className,
                    SignalList = signalList,
                    Formula = stl.GetProperty("formula").Clone()
                });
            }

            return specs;
        }

        static CsvTrace ReadTrace(string path, string[] signalList)
        {
            using StreamReader reader = new StreamReader(path);
            string headerLine = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(headerLine))
                throw new InvalidOperationException("CSV is empty.");

            string[] headers = SplitCsvLine(headerLine).ToArray();
            Dictionary<string, int> columnIndex = headers
                .Select((name, index) => new { name, index })
                .ToDictionary(item => item.name, item => item.index, StringComparer.OrdinalIgnoreCase);

            List<string> missing = signalList
                .Where(signal => !columnIndex.ContainsKey(signal))
                .ToList();
            if (missing.Count > 0)
                throw new InvalidOperationException($"CSV is missing required spec columns: {string.Join(", ", missing)}.");

            List<float[]> rows = new List<float[]>();
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                string[] values = SplitCsvLine(line).ToArray();
                float[] row = new float[signalList.Length];
                for (int i = 0; i < signalList.Length; i++)
                {
                    int column = columnIndex[signalList[i]];
                    string raw = column < values.Length ? values[column] : string.Empty;
                    row[i] = string.IsNullOrWhiteSpace(raw)
                        ? -1f
                        : float.Parse(raw, CultureInfo.InvariantCulture);
                }

                rows.Add(row);
            }

            if (rows.Count == 0)
                throw new InvalidOperationException("CSV has no data rows.");

            return new CsvTrace(rows.ToArray());
        }

        static IEnumerable<string> SplitCsvLine(string line)
        {
            List<string> fields = new List<string>();
            bool inQuotes = false;
            int start = 0;

            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        i++;
                        continue;
                    }

                    inQuotes = !inQuotes;
                }
                else if (line[i] == ',' && !inQuotes)
                {
                    fields.Add(UnescapeCsv(line.Substring(start, i - start)));
                    start = i + 1;
                }
            }

            fields.Add(UnescapeCsv(line.Substring(start)));
            return fields;
        }

        static string UnescapeCsv(string value)
        {
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                return value.Substring(1, value.Length - 2).Replace("\"\"", "\"");

            return value;
        }

        static float EvaluateFormula(JsonElement node, CsvTrace trace, int t = 0)
        {
            string nodeType = node.GetProperty("node_type").GetString();
            if (nodeType == "atom")
                return EvaluateAtom(node, trace, t);

            if (nodeType == "bool")
            {
                float left = EvaluateFormula(node.GetProperty("left"), trace, t);
                float right = EvaluateFormula(node.GetProperty("right"), trace, t);
                string op = node.GetProperty("op").GetString();
                if (op == "and")
                    return Math.Min(left, right);
                if (op == "or")
                    return Math.Max(left, right);
                throw new InvalidOperationException($"Unsupported bool op '{op}'.");
            }

            if (nodeType == "not")
                return -EvaluateFormula(node.GetProperty("sub"), trace, t);

            if (nodeType == "timed")
                return EvaluateTimed(node, trace, t);

            throw new InvalidOperationException($"Unsupported node_type '{nodeType}'.");
        }

        static float EvaluateAtom(JsonElement node, CsvTrace trace, int t)
        {
            int dim = node.GetProperty("dim").GetInt32();
            float threshold = (float)node.GetProperty("threshold").GetDouble();
            float value = trace.ValueAt(t, dim);
            string cmp = node.GetProperty("cmp").GetString();

            if (cmp == ">" || cmp == ">=")
                return value - threshold;
            if (cmp == "<" || cmp == "<=")
                return threshold - value;
            if (cmp == "==")
                return -Math.Abs(value - threshold);

            throw new InvalidOperationException($"Unsupported atom comparison '{cmp}'.");
        }

        static float EvaluateTimed(JsonElement node, CsvTrace trace, int t)
        {
            string op = node.GetProperty("op").GetString();
            JsonElement bounds = node.GetProperty("interval").GetProperty("value_indices");
            int start = Math.Max(0, t + bounds[0].GetInt32());
            int end = Math.Max(start, t + bounds[1].GetInt32());

            if (op == "eventually")
            {
                float best = float.NegativeInfinity;
                for (int i = start; i <= end; i++)
                    best = Math.Max(best, EvaluateFormula(node.GetProperty("sub"), trace, i));
                return best;
            }

            if (op == "always")
            {
                float best = float.PositiveInfinity;
                for (int i = start; i <= end; i++)
                    best = Math.Min(best, EvaluateFormula(node.GetProperty("sub"), trace, i));
                return best;
            }

            throw new InvalidOperationException($"Unsupported timed op '{op}'.");
        }

        static int ArgMax(float[] values)
        {
            int best = 0;
            for (int i = 1; i < values.Length; i++)
            {
                if (values[i] > values[best])
                    best = i;
            }

            return best;
        }

        sealed class FittedSpec
        {
            public string ClassName { get; set; }
            public string[] SignalList { get; set; }
            public JsonElement Formula { get; set; }
        }

        sealed class CsvTrace
        {
            readonly float[][] rows;

            public CsvTrace(float[][] rows)
            {
                this.rows = rows;
            }

            public float ValueAt(int t, int dim)
            {
                int clampedT = Math.Max(0, Math.Min(t, rows.Length - 1));
                return rows[clampedT][dim];
            }
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
