using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using EngineIO;

namespace Controllers
{
    public sealed class AssemblerAnalogSnapshot
    {
        public State LidsState { get; set; }
        public State BasesState { get; set; }
        public int Counter { get; set; }
        public bool LidAtPlace { get; set; }
        public bool BaseAtPlace { get; set; }
        public bool PartLeaving { get; set; }
        public bool ItemDetected { get; set; }
        public bool Grab { get; set; }
        public float PositionX { get; set; }
        public float PositionZ { get; set; }
        public float SetX { get; set; }
        public float SetZ { get; set; }

        public static AssemblerAnalogSnapshot Read(Controller controller)
        {
            MemoryMap.Instance.Update();
            State ReadState(string name)
            {
                FieldInfo field = controller.GetType().GetField(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null || field.FieldType != typeof(State))
                    throw new InvalidOperationException($"Missing controller state field '{name}'.");
                return (State)field.GetValue(controller);
            }
            bool Bit(string name, MemoryType type) =>
                (MemoryMap.Instance.GetBit(name, type)
                    ?? throw new InvalidOperationException($"Missing bit tag '{name}'.")).Value;
            float Float(string name, MemoryType type) =>
                (MemoryMap.Instance.GetFloat(name, type)
                    ?? throw new InvalidOperationException($"Missing float tag '{name}'.")).Value;

            return new AssemblerAnalogSnapshot
            {
                LidsState = ReadState("stateLids"),
                BasesState = ReadState("stateBases"),
                Counter = (MemoryMap.Instance.GetInt("Counter", MemoryType.Output)
                    ?? throw new InvalidOperationException("Missing integer tag 'Counter'.")).Value,
                LidAtPlace = Bit("Lid at place", MemoryType.Input),
                BaseAtPlace = Bit("Base at place", MemoryType.Input),
                PartLeaving = Bit("Part leaving", MemoryType.Input),
                ItemDetected = Bit("Item detected", MemoryType.Input),
                Grab = Bit("Grab", MemoryType.Output),
                PositionX = Float("X", MemoryType.Input),
                PositionZ = Float("Z", MemoryType.Input),
                SetX = Float("Set X", MemoryType.Output),
                SetZ = Float("Set Z", MemoryType.Output)
            };
        }
    }

    public sealed class AssemblerAnalogRecoveryDecision
    {
        public State LidsState { get; set; }
        public State BasesState { get; set; }
        public int Counter { get; set; }
        public bool IsPlaceholder { get; set; }
        public bool StateIdentificationSatisfied { get; set; }
        public bool ReverseBaseWithoutLid { get; set; }
        public bool ClearBaseBeforeReverse { get; set; }
        public bool RecoverSeparatedLidAndBase { get; set; }
        public bool RecoverStackedLids { get; set; }
        public bool RecoverHeldLidWaitForBase { get; set; }
        public bool RecoverTwoBasesTwoLids { get; set; }
        public bool RecoverAlternatingLidBase { get; set; }
        public bool RecoverHeldAssemblyTwoBases { get; set; }
        public bool RecoverMisplacedAssembly { get; set; }
        public bool RecoverBaseAndLid { get; set; }
        public bool RepositionBase { get; set; }
        public string Reason { get; set; }
    }

    public static class AssemblerAnalogRecoveryStateDecider
    {
        public static bool EnableStateIdentification = false;

        public static AssemblerAnalogRecoveryDecision Decide(AssemblerAnalogSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            Debug.WriteLine($"[AssemblerAnalog snapshot] LidAtPlace={snapshot.LidAtPlace}, BaseAtPlace={snapshot.BaseAtPlace}, ItemDetected={snapshot.ItemDetected}, Grab={snapshot.Grab}");

            if (!EnableStateIdentification)
                return NoStateMatch(snapshot);

            if (snapshot.ItemDetected && snapshot.Grab && snapshot.BaseAtPlace)
                return Match(snapshot, State.State3, State.State1,
                    "ItemDetected, Grab and BaseAtPlace are true; resume with a held lid and waiting base.");

            if (!snapshot.Grab && snapshot.LidAtPlace && snapshot.BaseAtPlace)
                return Match(snapshot, State.State1, State.State1,
                    "LidAtPlace and BaseAtPlace are true and Grab is false; resume pickup with both pieces present.");

            if (!snapshot.Grab && !snapshot.LidAtPlace && !snapshot.BaseAtPlace)
                return Match(snapshot, State.State0, State.State0,
                    "LidAtPlace and BaseAtPlace are false and Grab is false; resume waiting for both pieces.");

            // Remaining situations require recovery-module identification.
            return NoStateMatch(snapshot);
        }

        static AssemblerAnalogRecoveryDecision Match(AssemblerAnalogSnapshot snapshot,
            State lids, State bases, string reason)
        {
            return new AssemblerAnalogRecoveryDecision
            {
                LidsState = lids,
                BasesState = bases,
                Counter = snapshot.Counter,
                StateIdentificationSatisfied = true,
                IsPlaceholder = false,
                Reason = reason
            };
        }

        static AssemblerAnalogRecoveryDecision NoStateMatch(AssemblerAnalogSnapshot snapshot)
        {
            return new AssemblerAnalogRecoveryDecision
            {
                Counter = snapshot.Counter,
                StateIdentificationSatisfied = false,
                IsPlaceholder = true,
                Reason = "No state match; defer to recovery module identification."
            };
        }
    }

    public static class AssemblerAnalogRecoveryScriptWriter
    {
        // Conveyor 2 follows the same naming convention; adjust these to the scene's exact tags.
        public static float BaseRepositionPickupX = 8.6f;
        public static float BaseRepositionPickupZ = 9f;
        public static float BaseAndLidPickupX = 0.7f;
        public static float BaseAndLidPickupZ = 9f;
        public static float MisplacedAssemblyPickupX = 10f;
        public static string BaseForward1Tag = "Belt Conveyor (2m) 3 (+)";
        public static string BaseReverse1Tag = "Belt Conveyor (2m) 3 (-)";
        public static string BaseForward2Tag = "Belt Conveyor (2m) 2 (+)";
        public static string BaseReverse2Tag = "Belt Conveyor (2m) 2 (-)";
        public static string SeparatedPartsForward2Tag = "Belt Conveyor (6m) 0 (+)";
        public static string SeparatedPartsReverse2Tag = "Belt Conveyor (6m) 0 (-)";
        public static string TwoPairsForward2Tag = "Belt Conveyor (6m) 0 (+)";
        public static string TwoPairsReverse2Tag = "Belt Conveyor (6m) 0 (-)";

        public static (string Path, string ClassName) Write(string nominalSourcePath, string outputFolder,
            string sourceControllerName, AssemblerAnalogSnapshot snapshot, AssemblerAnalogRecoveryDecision decision)
        {
            if (!decision.StateIdentificationSatisfied && !decision.ReverseBaseWithoutLid
                && !decision.RecoverSeparatedLidAndBase && !decision.RecoverStackedLids
                && !decision.RecoverHeldLidWaitForBase && !decision.RecoverTwoBasesTwoLids && !decision.RecoverAlternatingLidBase && !decision.RecoverHeldAssemblyTwoBases && !decision.RecoverMisplacedAssembly && !decision.RecoverBaseAndLid && !decision.RepositionBase)
                throw new InvalidOperationException("Cannot generate recovery: no state or recovery module matched.");
            if (!Regex.IsMatch(sourceControllerName, @"^[A-Za-z_]\w*$"))
                throw new ArgumentException("Invalid controller name.", nameof(sourceControllerName));
            if (Array.IndexOf(new[] { State.State0, State.State1, State.State2,
                    State.State3, State.State4, State.State5 }, decision.LidsState) < 0
                || Array.IndexOf(new[] { State.State0, State.State1, State.State2 }, decision.BasesState) < 0)
                throw new ArgumentException("Recovery decision contains an unsupported state.", nameof(decision));

            string source = File.ReadAllText(nominalSourcePath);
            string forward2Tag = (decision.RecoverTwoBasesTwoLids || decision.RecoverAlternatingLidBase) ? TwoPairsForward2Tag
                : decision.RecoverSeparatedLidAndBase ? SeparatedPartsForward2Tag : BaseForward2Tag;
            // Recovery bindings use the scene's directional belt tags even if the
            // nominal controller still uses its original display names.
            source = source.Replace("GetBit(\"Bases conveyor 1\", MemoryType.Output)",
                "GetBit(" + Literal(BaseForward1Tag) + ", MemoryType.Output)");
            source = source.Replace("GetBit(\"Bases conveyor 2\", MemoryType.Output)",
                "GetBit(" + Literal(forward2Tag) + ", MemoryType.Output)");
            string className = "Recovery_" + sourceControllerName;
            Regex constructor = new Regex(@"public\s+AssemblerAnalog\s*\(\s*\)\s*\{[^{}]*\}");
            if (constructor.Matches(source).Count != 1)
                throw new InvalidOperationException("Expected exactly one AssemblerAnalog constructor.");

            // Replace the reset constructor and stop conveyors during recovery initialization.
            // New timer objects start fresh; elapsed pre-pause timer time is not reconstructed.
            string initialization = $@"public {className}()
        {{
            stateLids = State.{decision.LidsState};
            stateBases = State.{decision.BasesState};
            counter.Value = {decision.Counter.ToString(CultureInfo.InvariantCulture)};
            lidsConveyor1.Value = false;
            lidsConveyor2.Value = false;
            basesConveyor1.Value = false;
            basesConveyor2.Value = false;
            rtLidAtPlace.CLK({Bool(snapshot.LidAtPlace)});
            rtBaseAtPlace.CLK({Bool(snapshot.BaseAtPlace)});
            ftPartLeaving.CLK({Bool(snapshot.PartLeaving)});
            grabTimer.PT = 1000;
            grabTimer.IN = stateLids == State.State2;
            watchDogTimer.PT = 3000;
            watchDogTimer.IN = stateLids == State.State4;
            if ({Bool(decision.RecoverStackedLids || decision.RecoverHeldLidWaitForBase)})
            {{
                stackedLidsRecovery = new {(decision.RecoverHeldLidWaitForBase
                    ? "AssemblerAnalogStackedLidsSeparateBasesRecoveryModule"
                    : "AssemblerAnalogStackedLidsAdjacentBasesRecoveryModule")}(
                    {Literal(BaseForward1Tag)}, {Literal(BaseReverse1Tag)},
                    {Literal(BaseForward2Tag)}, {Literal(BaseReverse2Tag)});
                grabTimer.IN = false;
                watchDogTimer.IN = false;
            }}
            if ({Bool(decision.RepositionBase)})
            {{
                repositionBaseRecovery = new AssemblerAnalogRepositionBaseRecoveryModule(
                    {Literal(BaseForward1Tag)}, {Literal(BaseReverse1Tag)},
                    {Literal(BaseForward2Tag)}, {Literal(BaseReverse2Tag)},
                    {BaseRepositionPickupX.ToString("R", CultureInfo.InvariantCulture)}f,
                    {BaseRepositionPickupZ.ToString("R", CultureInfo.InvariantCulture)}f);
                grabTimer.IN = false;
                watchDogTimer.IN = false;
            }}
            if ({Bool(decision.RecoverBaseAndLid)})
            {{
                baseAndLidRecovery = new AssemblerAnalogRepositionLidRecoveryModule(
                    {Literal(BaseForward1Tag)}, {Literal(BaseReverse1Tag)},
                    {Literal(BaseForward2Tag)}, {Literal(BaseReverse2Tag)},
                    {BaseAndLidPickupX.ToString("R", CultureInfo.InvariantCulture)}f,
                    {BaseAndLidPickupZ.ToString("R", CultureInfo.InvariantCulture)}f);
                grabTimer.IN = false;
                watchDogTimer.IN = false;
            }}
            if ({Bool(decision.RecoverMisplacedAssembly)})
            {{
                misplacedAssemblyRecovery = new AssemblerAnalogCorrectAssemblyRecoveryModule(
                    {Literal(BaseForward1Tag)}, {Literal(BaseReverse1Tag)},
                    {Literal(BaseForward2Tag)}, {Literal(BaseReverse2Tag)},
                    {MisplacedAssemblyPickupX.ToString("R", CultureInfo.InvariantCulture)}f);
                grabTimer.IN = false;
                watchDogTimer.IN = false;
            }}
            if ({Bool(decision.RecoverHeldAssemblyTwoBases)})
            {{
                heldAssemblyRecovery = new AssemblerAnalogHeldAssemblyBaseWithoutLidRecoveryModule(
                    {Literal(BaseForward1Tag)}, {Literal(BaseReverse1Tag)},
                    {Literal(BaseForward2Tag)}, {Literal(BaseReverse2Tag)});
                grabTimer.IN = false;
                watchDogTimer.IN = false;
            }}
            if ({Bool(decision.RecoverAlternatingLidBase)})
            {{
                alternatingPartsRecovery = new AssemblerAnalogFourPiecesLBLBRecoveryModule(
                    {Literal(BaseForward1Tag)}, {Literal(BaseReverse1Tag)},
                    {Literal(TwoPairsForward2Tag)}, {Literal(TwoPairsReverse2Tag)});
                grabTimer.IN = false;
                watchDogTimer.IN = false;
            }}
            if ({Bool(decision.RecoverTwoBasesTwoLids)})
            {{
                twoPairsRecovery = new AssemblerAnalogFourPiecesBBLLRecoveryModule(
                    {Literal(BaseForward1Tag)}, {Literal(BaseReverse1Tag)},
                    {Literal(TwoPairsForward2Tag)}, {Literal(TwoPairsReverse2Tag)});
                grabTimer.IN = false;
                watchDogTimer.IN = false;
            }}
            if ({Bool(decision.RecoverSeparatedLidAndBase)})
            {{
                separatedPartsRecovery = new AssemblerAnalogSeparatedLidAndBaseRecoveryModule(
                    {Literal(BaseForward1Tag)}, {Literal(BaseReverse1Tag)},
                    {Literal(SeparatedPartsForward2Tag)}, {Literal(SeparatedPartsReverse2Tag)});
                grabTimer.IN = false;
                watchDogTimer.IN = false;
            }}
            if ({Bool(decision.ReverseBaseWithoutLid)})
            {{
                MemoryBit forward1 = RequireRecoveryBit({Literal(BaseForward1Tag)});
                MemoryBit forward2 = RequireRecoveryBit({Literal(BaseForward2Tag)});
                MemoryBit reverse1 = RequireRecoveryBit({Literal(BaseReverse1Tag)});
                MemoryBit reverse2 = RequireRecoveryBit({Literal(BaseReverse2Tag)});
                reverse1.Value = false;
                reverse2.Value = false;
                forward1.Value = false;
                forward2.Value = false;
                setX.Value = {snapshot.PositionX.ToString("R", CultureInfo.InvariantCulture)}f;
                setZ.Value = 0f;
                reverseBaseRecovery = new {(decision.ClearBaseBeforeReverse
                    ? "AssemblerAnalogFirstBaseWithoutLidRecoveryModule"
                    : "AssemblerAnalogSecondBaseWithoutLidRecoveryModule")}(
                    {Bool(snapshot.BaseAtPlace)},
                    () => {{ basesConveyor1.Value = false; basesConveyor2.Value = false;
                             forward1.Value = false; forward2.Value = false; }},
                    value => reverse1.Value = value,
                    value => reverse2.Value = value);
            }}
            pickupPositioningPhase = {((decision.LidsState == State.State1 && decision.BasesState == State.State1
                && snapshot.LidAtPlace && snapshot.BaseAtPlace && !snapshot.Grab
                && !decision.ReverseBaseWithoutLid) ? 1 : 0)};
            if (pickupPositioningPhase != 0)
            {{
                // Hold X while retracting Z; do not move sideways while lowered.
                setX.Value = {snapshot.PositionX.ToString("R", CultureInfo.InvariantCulture)}f;
                setZ.Value = 0f;
            }}
        }}";
            source = constructor.Replace(source, _ => initialization);
            // A lid may have arrived before recovery or while the arm was busy.
            // Accept its current presence when state 0 is ready for another pickup.
            Regex lidReady = new Regex(@"\bif\s*\(\s*rtLidAtPlace\.Q\s*\)");
            if (lidReady.Matches(source).Count != 1)
                throw new InvalidOperationException("Expected exactly one lid arrival transition.");
            source = lidReady.Replace(source,
                "if (lidAtPlace.Value && System.Math.Abs(positionX.Value - 1.1f) <= PickupPositionTolerance)");
            Regex discharge = new Regex(@"else if \(stateBases == State\.State2\)\s*\{\s*stopBlade2\.Value = false;\s*basesConveyor1\.Value = true;\s*basesConveyor2\.Value = true;\s*if \(ftPartLeaving\.Q\)\s*stateBases = State\.State0;\s*\}");
            if (discharge.Matches(source).Count != 1)
                throw new InvalidOperationException("Expected exactly one base discharge block.");
            source = discharge.Replace(source, @"else if (stateBases == State.State2)
            {
                stopBlade2.Value = false;
                basesConveyor1.Value = true;
                basesConveyor2.Value = true;

                if (dischargeTracker.Update(true, partLeaving.Value))
                    stateBases = State.State0;
            }");
            source = source.Replace("//Bases", @"//Bases
            if (stateBases != State.State2)
                dischargeTracker.Update(false, partLeaving.Value);");
            Regex execute = new Regex(@"public\s+override\s+void\s+Execute\(int elapsedMilliseconds\)");
            if (execute.Matches(source).Count != 1)
                throw new InvalidOperationException("Expected exactly one Execute method.");
            source = execute.Replace(source, @"private int pickupPositioningPhase;
        private readonly AssemblerAnalogDischargeTracker dischargeTracker = new AssemblerAnalogDischargeTracker();
        private IAssemblerAnalogBaseRecoveryModule reverseBaseRecovery;
        private AssemblerAnalogSeparatedLidAndBaseRecoveryModule separatedPartsRecovery;
        private AssemblerAnalogFourPiecesBBLLRecoveryModule twoPairsRecovery;
        private AssemblerAnalogFourPiecesLBLBRecoveryModule alternatingPartsRecovery;
        private AssemblerAnalogHeldAssemblyBaseWithoutLidRecoveryModule heldAssemblyRecovery;
        private AssemblerAnalogCorrectAssemblyRecoveryModule misplacedAssemblyRecovery;
        private AssemblerAnalogRepositionLidRecoveryModule baseAndLidRecovery;
        private AssemblerAnalogRepositionBaseRecoveryModule repositionBaseRecovery;
        private AssemblerAnalogStackedLidsAdjacentBasesRecoveryModule stackedLidsRecovery;
        private bool reverseBaseGripperRetracted;
        private const float PickupPositionTolerance = 0.1f;

        private static MemoryBit RequireRecoveryBit(string name)
        {
            return MemoryMap.Instance.GetBit(name, MemoryType.Output)
                ?? throw new System.InvalidOperationException(""Missing reverse recovery output tag: "" + name);
        }

        public void Dispose()
        {
            reverseBaseRecovery?.Stop();
            separatedPartsRecovery?.Dispose();
            twoPairsRecovery?.Dispose();
            alternatingPartsRecovery?.Dispose();
            heldAssemblyRecovery?.Dispose();
            misplacedAssemblyRecovery?.Dispose();
            baseAndLidRecovery?.Dispose();
            repositionBaseRecovery?.Dispose();
            stackedLidsRecovery?.Dispose();
        }

        public override void Execute(int elapsedMilliseconds)
        {
            if (stackedLidsRecovery != null)
            {
                stackedLidsRecovery.Execute(elapsedMilliseconds);
                if (stackedLidsRecovery.ModuleComplete)
                {
                    AssemblerAnalogRecoveryDecision continuation = stackedLidsRecovery.CreateContinuationDecision();
                    stateLids = continuation.LidsState;
                    stateBases = continuation.BasesState;
                    grabTimer.IN = false;
                    watchDogTimer.IN = false;
                    rtLidAtPlace.CLK(lidAtPlace.Value);
                    rtBaseAtPlace.CLK(baseAtPlace.Value);
                    ftPartLeaving.CLK(partLeaving.Value);
                    stackedLidsRecovery.Dispose();
                    stackedLidsRecovery = null;
                }
                return;
            }

            if (repositionBaseRecovery != null)
            {
                repositionBaseRecovery.Execute(elapsedMilliseconds);
                if (repositionBaseRecovery.ModuleComplete)
                {
                    AssemblerAnalogRecoveryDecision continuation = repositionBaseRecovery.CreateContinuationDecision();
                    stateLids = continuation.LidsState;
                    stateBases = continuation.BasesState;
                    grabTimer.IN = false;
                    watchDogTimer.IN = false;
                    rtLidAtPlace.CLK(lidAtPlace.Value);
                    rtBaseAtPlace.CLK(baseAtPlace.Value);
                    ftPartLeaving.CLK(partLeaving.Value);
                    repositionBaseRecovery.Dispose();
                    repositionBaseRecovery = null;
                }
                return;
            }

            if (baseAndLidRecovery != null)
            {
                baseAndLidRecovery.Execute(elapsedMilliseconds);
                if (baseAndLidRecovery.ModuleComplete)
                {
                    AssemblerAnalogRecoveryDecision continuation = baseAndLidRecovery.CreateContinuationDecision();
                    stateLids = continuation.LidsState;
                    stateBases = continuation.BasesState;
                    grabTimer.IN = false;
                    watchDogTimer.IN = false;
                    rtLidAtPlace.CLK(lidAtPlace.Value);
                    rtBaseAtPlace.CLK(baseAtPlace.Value);
                    ftPartLeaving.CLK(partLeaving.Value);
                    baseAndLidRecovery.Dispose();
                    baseAndLidRecovery = null;
                }
                return;
            }

            if (misplacedAssemblyRecovery != null)
            {
                misplacedAssemblyRecovery.Execute(elapsedMilliseconds);
                if (misplacedAssemblyRecovery.ModuleComplete)
                {
                    AssemblerAnalogRecoveryDecision continuation = misplacedAssemblyRecovery.CreateContinuationDecision();
                    stateLids = continuation.LidsState;
                    stateBases = continuation.BasesState;
                    grabTimer.IN = false;
                    watchDogTimer.IN = false;
                    rtLidAtPlace.CLK(lidAtPlace.Value);
                    rtBaseAtPlace.CLK(baseAtPlace.Value);
                    ftPartLeaving.CLK(partLeaving.Value);
                    misplacedAssemblyRecovery.Dispose();
                    misplacedAssemblyRecovery = null;
                }
                return;
            }

            if (heldAssemblyRecovery != null)
            {
                heldAssemblyRecovery.Execute(elapsedMilliseconds);
                if (heldAssemblyRecovery.ModuleComplete)
                {
                    AssemblerAnalogRecoveryDecision continuation = heldAssemblyRecovery.CreateContinuationDecision();
                    stateLids = continuation.LidsState;
                    stateBases = continuation.BasesState;
                    grabTimer.IN = false;
                    watchDogTimer.IN = false;
                    rtLidAtPlace.CLK(lidAtPlace.Value);
                    rtBaseAtPlace.CLK(baseAtPlace.Value);
                    ftPartLeaving.CLK(partLeaving.Value);
                    heldAssemblyRecovery.Dispose();
                    heldAssemblyRecovery = null;
                }
                return;
            }

            if (alternatingPartsRecovery != null)
            {
                alternatingPartsRecovery.Execute(elapsedMilliseconds);
                if (alternatingPartsRecovery.ModuleComplete)
                {
                    AssemblerAnalogRecoveryDecision continuation = alternatingPartsRecovery.CreateContinuationDecision();
                    stateLids = continuation.LidsState;
                    stateBases = continuation.BasesState;
                    grabTimer.IN = false;
                    watchDogTimer.IN = false;
                    rtLidAtPlace.CLK(lidAtPlace.Value);
                    rtBaseAtPlace.CLK(baseAtPlace.Value);
                    ftPartLeaving.CLK(partLeaving.Value);
                    alternatingPartsRecovery.Dispose();
                    alternatingPartsRecovery = null;
                }
                return;
            }

            if (twoPairsRecovery != null)
            {
                twoPairsRecovery.Execute(elapsedMilliseconds);
                if (twoPairsRecovery.ModuleComplete)
                {
                    AssemblerAnalogRecoveryDecision continuation = twoPairsRecovery.CreateContinuationDecision();
                    stateLids = continuation.LidsState;
                    stateBases = continuation.BasesState;
                    grabTimer.IN = false;
                    watchDogTimer.IN = false;
                    rtLidAtPlace.CLK(lidAtPlace.Value);
                    rtBaseAtPlace.CLK(baseAtPlace.Value);
                    ftPartLeaving.CLK(partLeaving.Value);
                    twoPairsRecovery.Dispose();
                    twoPairsRecovery = null;
                }
                return;
            }

            if (separatedPartsRecovery != null)
            {
                separatedPartsRecovery.Execute(elapsedMilliseconds);
                if (separatedPartsRecovery.ModuleComplete)
                {
                    AssemblerAnalogRecoveryDecision continuation = separatedPartsRecovery.CreateContinuationDecision();
                    stateLids = continuation.LidsState;
                    stateBases = continuation.BasesState;
                    grabTimer.IN = false;
                    watchDogTimer.IN = false;
                    rtLidAtPlace.CLK(lidAtPlace.Value);
                    rtBaseAtPlace.CLK(baseAtPlace.Value);
                    ftPartLeaving.CLK(partLeaving.Value);
                    separatedPartsRecovery.Dispose();
                    separatedPartsRecovery = null;
                }
                return;
            }

            if (reverseBaseRecovery != null)
            {
                lidsConveyor1.Value = false;
                lidsConveyor2.Value = false;
                setZ.Value = 0f;
                if (!reverseBaseGripperRetracted)
                {
                    reverseBaseRecovery.Stop();
                    if (System.Math.Abs(positionZ.Value) > PickupPositionTolerance)
                        return;
                    reverseBaseGripperRetracted = true;
                }
                stopBlade2.Value = false;
                reverseBaseRecovery.Execute(baseAtPlace.Value);
                if (reverseBaseRecovery.ModuleComplete)
                {
                    AssemblerAnalogRecoveryDecision continuation =
                        reverseBaseRecovery.CreateContinuationDecision(counter.Value);
                    stateLids = continuation.LidsState;
                    stateBases = continuation.BasesState;
                    grabTimer.IN = false;
                    watchDogTimer.IN = false;
                    rtLidAtPlace.CLK(lidAtPlace.Value);
                    rtBaseAtPlace.CLK(baseAtPlace.Value);
                    ftPartLeaving.CLK(partLeaving.Value);
                    reverseBaseRecovery = null;
                }
                return;
            }

            if (pickupPositioningPhase != 0)
            {
                lidsConveyor1.Value = false;
                lidsConveyor2.Value = false;
                basesConveyor1.Value = false;
                basesConveyor2.Value = false;
                if (grab.Value)
                    throw new System.InvalidOperationException(""Pickup positioning requires an empty gripper."");

                setZ.Value = 0f;
                if (pickupPositioningPhase == 1)
                {
                    if (System.Math.Abs(positionZ.Value) <= PickupPositionTolerance)
                    {
                        pickupPositioningPhase = 2;
                        setX.Value = 1.1f;
                    }
                }
                else
                {
                    setX.Value = 1.1f;
                    if (System.Math.Abs(positionX.Value - 1.1f) <= PickupPositionTolerance)
                    {
                        pickupPositioningPhase = 0;
                    }
                }
                // Resume the original state logic on the following scan.
                return;
            }
            ExecuteRecoveryCycle(elapsedMilliseconds);
        }

        private void ExecuteRecoveryCycle(int elapsedMilliseconds)");
            source = Regex.Replace(source, @"\bclass\s+AssemblerAnalog\b", "class " + className);
            source = Regex.Replace(source, @"(:\s*Controller)\b", "$1, System.IDisposable");
            source = "// Generated by Program_Recovery from the original AssemblerAnalog control logic.\n"
                + "// Identification placeholder: " + Bool(decision.IsPlaceholder) + "\n" + source;
            Directory.CreateDirectory(outputFolder);
            string path = System.IO.Path.Combine(outputFolder, className + ".cs");
            File.WriteAllText(path, source);
            return (path, className);
        }

        static string Bool(bool value) => value ? "true" : "false";
        static string Literal(string value) => "@\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
