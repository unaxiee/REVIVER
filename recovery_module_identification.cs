//-----------------------------------------------------------------------------
// FACTORY I/O (SDK)
//
// Stage 2 recovery module identification for PickPlaceXYZ recovery.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

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
}
