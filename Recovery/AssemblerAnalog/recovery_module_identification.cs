using System;
using EngineIO;

namespace Controllers
{
    // Track a fresh leaving-sensor occupancy for each base discharge cycle.
    public sealed class AssemblerAnalogDischargeTracker
    {
        enum Phase { Inactive, ClearPrevious, WaitForArrival, WaitForDeparture }
        Phase phase;

        public bool Update(bool discharging, bool partLeaving)
        {
            if (!discharging)
            {
                phase = Phase.Inactive;
                return false;
            }
            switch (phase)
            {
                case Phase.Inactive:
                    phase = partLeaving ? Phase.ClearPrevious : Phase.WaitForArrival;
                    break;
                case Phase.ClearPrevious:
                    if (!partLeaving)
                        phase = Phase.WaitForArrival;
                    break;
                case Phase.WaitForArrival:
                    if (partLeaving)
                        phase = Phase.WaitForDeparture;
                    break;
                case Phase.WaitForDeparture:
                    if (!partLeaving)
                    {
                        phase = Phase.Inactive;
                        return true;
                    }
                    break;
            }
            return false;
        }
    }

    public static class AssemblerAnalogRecoveryModuleIdentifier
    {
        public static bool SelectSecondBaseWithoutLid = false;

        public static AssemblerAnalogRecoveryDecision Decide(
            AssemblerAnalogSnapshot snapshot, AssemblerAnalogRecoveryDecision stateDecision)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (stateDecision == null)
                throw new ArgumentNullException(nameof(stateDecision));
            if (stateDecision.StateIdentificationSatisfied)
                return stateDecision;

            if (SelectSecondBaseWithoutLid)
                return new AssemblerAnalogRecoveryDecision
                {
                    LidsState = State.State0,
                    BasesState = State.State0,
                    Counter = snapshot.Counter,
                    ReverseBaseWithoutLid = true,
                    ClearBaseBeforeReverse = false,
                    IsPlaceholder = true,
                    Reason = "Selected second-base-without-lid recovery: reverse both base conveyors, then resume (0, 0)."
                };

            if (IsBaseReposition(snapshot))
                return new AssemblerAnalogRecoveryDecision {
                    LidsState = State.State1, BasesState = State.State1,
                    Counter = snapshot.Counter, RepositionBase = true,
                    IsPlaceholder = true,
                    Reason = "Placeholder: retrieve and reposition base, return empty gripper to lid pickup; resume (1, 1)."
                };

            if (IsBaseAndLid(snapshot))
                return new AssemblerAnalogRecoveryDecision {
                    LidsState = State.State3, BasesState = State.State1,
                    Counter = snapshot.Counter, RecoverBaseAndLid = true,
                    IsPlaceholder = true,
                    Reason = "Placeholder: retrieve base and pick up lid at configured coordinates; resume (3, 1)."
                };

            if (IsMisplacedAssembly(snapshot))
                return new AssemblerAnalogRecoveryDecision {
                    LidsState = State.State5, BasesState = State.State2,
                    Counter = snapshot.Counter, RecoverMisplacedAssembly = true,
                    IsPlaceholder = true,
                    Reason = "Placeholder: retrieve misplaced assembly and correct its lid position."
                };

            if (IsHeldAssemblyTwoBases(snapshot))
                return new AssemblerAnalogRecoveryDecision {
                    LidsState = State.State0, BasesState = State.State0,
                    Counter = snapshot.Counter, RecoverHeldAssemblyTwoBases = true,
                    IsPlaceholder = true,
                    Reason = "Placeholder: return held assembly, retrieve two bases, place remaining lid and discharge two assemblies."
                };

            if (IsAlternatingLidBase(snapshot))
                return new AssemblerAnalogRecoveryDecision
                {
                    LidsState = State.State0, BasesState = State.State0,
                    Counter = snapshot.Counter, RecoverAlternatingLidBase = true,
                    IsPlaceholder = true,
                    Reason = "Placeholder: alternating lid/base pairs; park first assembly, discharge second, then first."
                };

            if (IsTwoBasesTwoLids(snapshot))
                return new AssemblerAnalogRecoveryDecision
                {
                    LidsState = State.State0, BasesState = State.State0,
                    Counter = snapshot.Counter, RecoverTwoBasesTwoLids = true,
                    IsPlaceholder = true,
                    Reason = "Placeholder: recover two loose lids and two bases, assembling both pairs."
                };

            if (IsHeldLidWaitingForBase(snapshot))
            {
                return new AssemblerAnalogRecoveryDecision
                {
                    LidsState = State.State5,
                    BasesState = State.State2,
                    Counter = snapshot.Counter,
                    RecoverHeldLidWaitForBase = true,
                    IsPlaceholder = true,
                    Reason = "Placeholder match: retract held lid, advance first assembly, wait for second base, complete placement, then resume (5, 2)."
                };
            }

            if (IsStackedLids(snapshot))
            {
                return new AssemblerAnalogRecoveryDecision
                {
                    LidsState = State.State5,
                    BasesState = State.State2,
                    Counter = snapshot.Counter,
                    RecoverStackedLids = true,
                    IsPlaceholder = true,
                    Reason = "Placeholder match: retract with held lid, advance bases for 500 ms, place lid at X=8.85, then resume (5, 2)."
                };
            }

            if (IsSeparatedLidAndBase(snapshot))
            {
                return new AssemblerAnalogRecoveryDecision
                {
                    LidsState = State.State5,
                    BasesState = State.State2,
                    Counter = snapshot.Counter,
                    RecoverSeparatedLidAndBase = true,
                    IsPlaceholder = true,
                    Reason = "Placeholder match: retrieve lid and base, place lid, retract, then discharge and resume (5, 2)."
                };
            }

            if (snapshot.BaseAtPlace)
            {
                return new AssemblerAnalogRecoveryDecision
                {
                    LidsState = State.State0,
                    BasesState = State.State0,
                    Counter = snapshot.Counter,
                    StateIdentificationSatisfied = false,
                    ReverseBaseWithoutLid = true,
                    ClearBaseBeforeReverse = true,
                    IsPlaceholder = false,
                    Reason = "Base at place is true: clear base with conveyor 1, then reverse both conveyors and resume (0, 0)."
                };
            }

            return new AssemblerAnalogRecoveryDecision
            {
                LidsState = State.State0,
                BasesState = State.State0,
                Counter = snapshot.Counter,
                StateIdentificationSatisfied = false,
                ReverseBaseWithoutLid = true,
                ClearBaseBeforeReverse = false,
                IsPlaceholder = false,
                Reason = "Base at place is false: reverse both base conveyors, then resume states (0, 0)."
            };
        }

        // TODO: Replace this test selection with a two-base/two-lid identification condition.
        static bool IsBaseReposition(AssemblerAnalogSnapshot snapshot) => false;
        static bool IsBaseAndLid(AssemblerAnalogSnapshot snapshot) => false;
        static bool IsMisplacedAssembly(AssemblerAnalogSnapshot snapshot) => true;
        static bool IsHeldAssemblyTwoBases(AssemblerAnalogSnapshot snapshot) => true;
        static bool IsAlternatingLidBase(AssemblerAnalogSnapshot snapshot) => true;
        static bool IsTwoBasesTwoLids(AssemblerAnalogSnapshot snapshot) => true;

        // TODO: Replace this test selection with the separated-lid/base detection rule.
        // Returning false restores the existing BaseAtPlace-based module selection.
        static bool IsSeparatedLidAndBase(AssemblerAnalogSnapshot snapshot) => true;
        // TODO: Replace with detection of two lids on one base. Test this module first.
        static bool IsStackedLids(AssemblerAnalogSnapshot snapshot) => true;
        // TODO: Replace with the condition for a held lid and a second base still to arrive.
        // Return false to allow the previous module-selection rules to run.
        static bool IsHeldLidWaitingForBase(AssemblerAnalogSnapshot snapshot) => false;
    }

    // The callbacks are supplied by the recovery controller once the scene's reverse
    // output tags are configured. A Boolean forward/run tag alone cannot reverse a belt.
    public sealed class AssemblerAnalogSeparatedLidAndBaseRecoveryModule : Controller, IDisposable
    {
        enum Phase
        {
            Retract, ReverseToLid, FeedLidToBlade, MoveToLid, LowerToLid, GrabLid,
            LiftLid, ReverseToBase, FeedBaseToBlade, PlaceLid, ReleaseLid, RetractAfterPlacement, Complete
        }

        // The loose lid is recovered at the base station, not the lid feeder.
        public const float StationX = 8.85f;
        public const float LidPickupZ = 9f;
        public const float PlacementZ = 10f;
        public const float PlacementReachedZ = 8.4f;
        public const float PositionTolerance = 0.1f;
        public const int GrabDelayMilliseconds = 1000;

        readonly MemoryBit forward1, forward2, reverse1, reverse2;
        readonly MemoryBit lidConveyor1, lidConveyor2, blade, grab, baseAtPlace, itemDetected;
        readonly MemoryFloat setX, setZ, positionX, positionZ;
        readonly MemoryInt counter;
        readonly float initialX;
        Phase phase;
        bool previousRetrievalBaseAtPlace;
        long grabElapsed;

        public bool ModuleComplete => phase == Phase.Complete;
        public string CurrentPhase => phase.ToString();

        public AssemblerAnalogSeparatedLidAndBaseRecoveryModule(
            string forward1Tag, string reverse1Tag, string forward2Tag, string reverse2Tag)
        {
            forward1 = Bit(forward1Tag, MemoryType.Output);
            forward2 = Bit(forward2Tag, MemoryType.Output);
            reverse1 = Bit(reverse1Tag, MemoryType.Output);
            reverse2 = Bit(reverse2Tag, MemoryType.Output);
            lidConveyor1 = Bit("Lids conveyor 1", MemoryType.Output);
            lidConveyor2 = Bit("Lids conveyor 2", MemoryType.Output);
            blade = Bit("Stop blade 2", MemoryType.Output);
            grab = Bit("Grab", MemoryType.Output);
            baseAtPlace = Bit("Base at place", MemoryType.Input);
            itemDetected = Bit("Item detected", MemoryType.Input);
            setX = Float("Set X", MemoryType.Output);
            setZ = Float("Set Z", MemoryType.Output);
            positionX = Float("X", MemoryType.Input);
            positionZ = Float("Z", MemoryType.Input);
            counter = MemoryMap.Instance.GetInt("Counter", MemoryType.Output)
                ?? throw new InvalidOperationException("Missing Counter output.");
            if (grab.Value)
                throw new InvalidOperationException("Separated-lid recovery requires an initially empty gripper.");
            initialX = positionX.Value;
            StopConveyors();
            setX.Value = initialX;
            setZ.Value = 0f;
        }

        public override void Execute(int elapsedMilliseconds)
        {
            if (ModuleComplete)
                return;
            StopConveyors();
            switch (phase)
            {
                case Phase.Retract:
                    setX.Value = initialX;
                    setZ.Value = 0f;
                    if (At(positionZ.Value, 0f))
                    {
                        previousRetrievalBaseAtPlace = baseAtPlace.Value;
                        phase = Phase.ReverseToLid;
                    }
                    break;
                case Phase.ReverseToLid:
                    setZ.Value = 0f;
                    if (ReverseUntilFallingEdge())
                        phase = Phase.FeedLidToBlade;
                    break;
                case Phase.FeedLidToBlade:
                    setZ.Value = 0f;
                    if (FeedFirstConveyorUntilPresent())
                    {
                        setX.Value = StationX;
                        phase = Phase.MoveToLid;
                    }
                    break;
                case Phase.MoveToLid:
                    blade.Value = true;
                    setZ.Value = 0f;
                    setX.Value = StationX;
                    if (At(positionX.Value, StationX) && At(positionZ.Value, 0f))
                        phase = Phase.LowerToLid;
                    break;
                case Phase.LowerToLid:
                    blade.Value = true;
                    setX.Value = StationX;
                    setZ.Value = LidPickupZ;
                    if (itemDetected.Value)
                    {
                        grab.Value = true;
                        grabElapsed = 0;
                        phase = Phase.GrabLid;
                    }
                    break;
                case Phase.GrabLid:
                    grab.Value = true;
                    grabElapsed += Math.Max(0, elapsedMilliseconds);
                    if (grabElapsed >= GrabDelayMilliseconds)
                    {
                        setZ.Value = 0f;
                        phase = Phase.LiftLid;
                    }
                    break;
                case Phase.LiftLid:
                    grab.Value = true;
                    setZ.Value = 0f;
                    if (At(positionZ.Value, 0f))
                    {
                        previousRetrievalBaseAtPlace = baseAtPlace.Value;
                        phase = Phase.ReverseToBase;
                    }
                    break;
                case Phase.ReverseToBase:
                    grab.Value = true;
                    setZ.Value = 0f;
                    if (ReverseUntilFallingEdge())
                        phase = Phase.FeedBaseToBlade;
                    break;
                case Phase.FeedBaseToBlade:
                    grab.Value = true;
                    setZ.Value = 0f;
                    if (FeedFirstConveyorUntilPresent())
                        phase = Phase.PlaceLid;
                    break;
                case Phase.PlaceLid:
                    blade.Value = true;
                    grab.Value = true;
                    setX.Value = StationX;
                    setZ.Value = PlacementZ;
                    if (At(positionX.Value, StationX) && positionZ.Value > PlacementReachedZ)
                    {
                        counter.Value++;
                        phase = Phase.ReleaseLid;
                    }
                    break;
                case Phase.ReleaseLid:
                    blade.Value = true;
                    grab.Value = false;
                    setZ.Value = 0f;
                    phase = Phase.RetractAfterPlacement;
                    break;
                case Phase.RetractAfterPlacement:
                    blade.Value = true;
                    grab.Value = false;
                    setZ.Value = 0f;
                    if (At(positionZ.Value, 0f))
                    {
                        blade.Value = false;
                        forward1.Value = true;
                        forward2.Value = true;
                        phase = Phase.Complete;
                    }
                    break;
            }
        }

        bool ReverseUntilFallingEdge()
        {
            bool fallingEdge = previousRetrievalBaseAtPlace && !baseAtPlace.Value;
            previousRetrievalBaseAtPlace = baseAtPlace.Value;
            if (fallingEdge)
            {
                blade.Value = true;
                return true;
            }
            blade.Value = false;
            reverse1.Value = true;
            reverse2.Value = true;
            return false;
        }

        bool FeedFirstConveyorUntilPresent()
        {
            blade.Value = true;
            if (baseAtPlace.Value)
                return true;
            // StopConveyors() has already disabled both reverse outputs and conveyor 2.
            forward1.Value = true;
            return false;
        }

        void StopConveyors()
        {
            forward1.Value = false;
            forward2.Value = false;
            reverse1.Value = false;
            reverse2.Value = false;
            lidConveyor1.Value = false;
            lidConveyor2.Value = false;
        }

        public void Dispose()
        {
            if (!ModuleComplete)
                StopConveyors();
            else
            {
                // Preserve the forward discharge commands during nominal handover.
                reverse1.Value = false;
                reverse2.Value = false;
            }
        }

        public AssemblerAnalogRecoveryDecision CreateContinuationDecision()
        {
            if (!ModuleComplete)
                throw new InvalidOperationException("Finish assembly and gripper retraction before resuming discharge.");
            return new AssemblerAnalogRecoveryDecision
            {
                LidsState = State.State5,
                BasesState = State.State2,
                Counter = counter.Value,
                StateIdentificationSatisfied = true,
                Reason = "Recovered and assembled lid/base, retracted gripper, lowered blade and started conveyors; resume (5, 2)."
            };
        }

        static bool At(float actual, float target) => Math.Abs(actual - target) <= PositionTolerance;
        static MemoryBit Bit(string name, MemoryType type) => MemoryMap.Instance.GetBit(name, type)
            ?? throw new InvalidOperationException("Missing bit tag: " + name);
        static MemoryFloat Float(string name, MemoryType type) => MemoryMap.Instance.GetFloat(name, type)
            ?? throw new InvalidOperationException("Missing float tag: " + name);
    }

    public interface IAssemblerAnalogBaseRecoveryModule
    {
        bool ModuleComplete { get; }
        void Execute(bool baseAtPlace);
        void Stop();
        AssemblerAnalogRecoveryDecision CreateContinuationDecision(int counter);
    }

    public sealed class AssemblerAnalogSecondBaseWithoutLidRecoveryModule : IAssemblerAnalogBaseRecoveryModule
    {
        readonly Action stopForwardConveyors;
        readonly Action<bool> setReverseConveyor1;
        readonly Action<bool> setReverseConveyor2;
        bool previousBaseAtPlace;

        public bool ModuleComplete { get; private set; }

        public AssemblerAnalogSecondBaseWithoutLidRecoveryModule(
            bool initialBaseAtPlace,
            Action stopForwardConveyors,
            Action<bool> setReverseConveyor1,
            Action<bool> setReverseConveyor2)
        {
            this.stopForwardConveyors = stopForwardConveyors
                ?? throw new ArgumentNullException(nameof(stopForwardConveyors));
            this.setReverseConveyor1 = setReverseConveyor1
                ?? throw new ArgumentNullException(nameof(setReverseConveyor1));
            this.setReverseConveyor2 = setReverseConveyor2
                ?? throw new ArgumentNullException(nameof(setReverseConveyor2));
            previousBaseAtPlace = initialBaseAtPlace;
        }

        public void Execute(bool baseAtPlace)
        {
            if (ModuleComplete)
                return;

            // Disable forward commands before enabling reverse commands.
            stopForwardConveyors();
            bool fallingEdge = previousBaseAtPlace && !baseAtPlace;
            previousBaseAtPlace = baseAtPlace;
            if (fallingEdge)
            {
                Stop();
                ModuleComplete = true;
                return;
            }

            setReverseConveyor1(true);
            setReverseConveyor2(true);
        }

        // Also called by the owner if recovery is interrupted.
        public void Stop()
        {
            stopForwardConveyors();
            setReverseConveyor1(false);
            setReverseConveyor2(false);
        }

        public AssemblerAnalogRecoveryDecision CreateContinuationDecision(int counter)
        {
            if (!ModuleComplete)
                throw new InvalidOperationException("Wait for the required base-at-place edge sequence before resuming.");
            return new AssemblerAnalogRecoveryDecision
            {
                LidsState = State.State0,
                BasesState = State.State0,
                Counter = counter,
                StateIdentificationSatisfied = true,
                IsPlaceholder = false,
                Reason = "Base reversal completed the required base-at-place edge sequence; resume states (0, 0)."
            };
        }
    }

    // Selected when BaseAtPlace is initially true: separate the waiting base first,
    // then delegate the original two-conveyor reversal to the existing module.
    public sealed class AssemblerAnalogFirstBaseWithoutLidRecoveryModule : IAssemblerAnalogBaseRecoveryModule
    {
        readonly Action stopForwardConveyors;
        readonly Action<bool> setReverseConveyor1;
        readonly Action<bool> setReverseConveyor2;
        bool previousBaseAtPlace;
        AssemblerAnalogSecondBaseWithoutLidRecoveryModule reverseBoth;

        public bool ModuleComplete => reverseBoth != null && reverseBoth.ModuleComplete;

        public AssemblerAnalogFirstBaseWithoutLidRecoveryModule(bool initialBaseAtPlace,
            Action stopForwardConveyors, Action<bool> setReverseConveyor1, Action<bool> setReverseConveyor2)
        {
            this.stopForwardConveyors = stopForwardConveyors
                ?? throw new ArgumentNullException(nameof(stopForwardConveyors));
            this.setReverseConveyor1 = setReverseConveyor1
                ?? throw new ArgumentNullException(nameof(setReverseConveyor1));
            this.setReverseConveyor2 = setReverseConveyor2
                ?? throw new ArgumentNullException(nameof(setReverseConveyor2));
            previousBaseAtPlace = initialBaseAtPlace;
        }

        public void Execute(bool baseAtPlace)
        {
            if (reverseBoth != null)
            {
                reverseBoth.Execute(baseAtPlace);
                return;
            }

            stopForwardConveyors();
            setReverseConveyor2(false);
            bool fallingEdge = previousBaseAtPlace && !baseAtPlace;
            previousBaseAtPlace = baseAtPlace;
            if (fallingEdge)
            {
                setReverseConveyor1(false);
                reverseBoth = new AssemblerAnalogSecondBaseWithoutLidRecoveryModule(false,
                    stopForwardConveyors, setReverseConveyor1, setReverseConveyor2);
                return;
            }
            setReverseConveyor1(true);
        }

        public void Stop()
        {
            stopForwardConveyors();
            setReverseConveyor1(false);
            setReverseConveyor2(false);
        }

        public AssemblerAnalogRecoveryDecision CreateContinuationDecision(int counter)
        {
            if (!ModuleComplete)
                throw new InvalidOperationException("Wait for base clearance and two-conveyor reversal before resuming.");
            return reverseBoth.CreateContinuationDecision(counter);
        }
    }

    public class AssemblerAnalogStackedLidsAdjacentBasesRecoveryModule : Controller, IDisposable
    {
        enum Phase
        {
            Retract, MoveHeldLidToStation, AdvanceBases, WaitForNextBase,
            PlaceTopLid, ReleaseTopLid, RetractAfterPlacement, Complete
        }

        public const int AdvanceBasesMilliseconds = 592;
        public const float StationX = 8.85f;
        public const float PlacementZ = 10f;
        public const float PlacementReachedZ = 8.4f;
        public const float PositionTolerance = 0.1f;

        protected virtual bool WaitForSecondBaseAfterAdvance => false;
        protected virtual bool AdvanceUntilPartLeavingFalls => false;

        readonly MemoryBit forward1, forward2, reverse1, reverse2;
        readonly MemoryBit lidConveyor1, lidConveyor2, blade, grab, baseAtPlace;
        readonly MemoryBit partLeaving;
        readonly MemoryFloat setX, setZ, positionX, positionZ;
        readonly MemoryInt counter;
        readonly float initialX;
        Phase phase;
        long advanceElapsed;
        bool previousPartLeaving;

        public bool ModuleComplete => phase == Phase.Complete;
        public string CurrentPhase => phase.ToString();

        public AssemblerAnalogStackedLidsAdjacentBasesRecoveryModule(
            string forward1Tag, string reverse1Tag, string forward2Tag, string reverse2Tag)
        {
            forward1 = Bit(forward1Tag, MemoryType.Output);
            forward2 = Bit(forward2Tag, MemoryType.Output);
            reverse1 = Bit(reverse1Tag, MemoryType.Output);
            reverse2 = Bit(reverse2Tag, MemoryType.Output);
            lidConveyor1 = Bit("Lids conveyor 1", MemoryType.Output);
            lidConveyor2 = Bit("Lids conveyor 2", MemoryType.Output);
            blade = Bit("Stop blade 2", MemoryType.Output);
            grab = Bit("Grab", MemoryType.Output);
            baseAtPlace = Bit("Base at place", MemoryType.Input);
            if (AdvanceUntilPartLeavingFalls)
                partLeaving = Bit("Part leaving", MemoryType.Input);
            setX = Float("Set X", MemoryType.Output);
            setZ = Float("Set Z", MemoryType.Output);
            positionX = Float("X", MemoryType.Input);
            positionZ = Float("Z", MemoryType.Input);
            counter = MemoryMap.Instance.GetInt("Counter", MemoryType.Output)
                ?? throw new InvalidOperationException("Missing Counter output.");
            // This recovery entry assumes the extra lid is already held.
            // Keep the grab command active throughout retraction and transfer.
            grab.Value = true;
            initialX = positionX.Value;
            StopConveyors();
            setX.Value = initialX;
            setZ.Value = 0f;
        }

        public override void Execute(int elapsedMilliseconds)
        {
            if (ModuleComplete)
                return;
            StopConveyors();
            switch (phase)
            {
                case Phase.Retract:
                    blade.Value = true;
                    grab.Value = true;
                    setX.Value = initialX;
                    setZ.Value = 0f;
                    if (At(positionZ.Value, 0f))
                        phase = Phase.MoveHeldLidToStation;
                    break;
                case Phase.MoveHeldLidToStation:
                    blade.Value = true;
                    grab.Value = true;
                    setZ.Value = 0f;
                    setX.Value = StationX;
                    if (At(positionZ.Value, 0f) && At(positionX.Value, StationX))
                    {
                        advanceElapsed = 0;
                        previousPartLeaving = partLeaving != null && partLeaving.Value;
                        blade.Value = false;
                        forward1.Value = true;
                        forward2.Value = true;
                        phase = Phase.AdvanceBases;
                    }
                    break;
                case Phase.AdvanceBases:
                    grab.Value = true;
                    setX.Value = StationX;
                    setZ.Value = 0f;
                    advanceElapsed += elapsedMilliseconds;
                    bool advanceComplete;
                    if (AdvanceUntilPartLeavingFalls)
                    {
                        bool currentPartLeaving = partLeaving.Value;
                        advanceComplete = previousPartLeaving && !currentPartLeaving;
                        previousPartLeaving = currentPartLeaving;
                    }
                    else
                        advanceComplete = advanceElapsed >= AdvanceBasesMilliseconds;
                    if (advanceComplete)
                    {
                        // Stop both belts before lowering the held lid.
                        if (!WaitForSecondBaseAfterAdvance)
                        {
                            blade.Value = false;
                            phase = Phase.PlaceTopLid;
                        }
                        else
                        {
                            blade.Value = true;
                            phase = Phase.WaitForNextBase;
                        }
                    }
                    else
                    {
                        blade.Value = false;
                        forward1.Value = true;
                        forward2.Value = true;
                    }
                    break;
                case Phase.WaitForNextBase:
                    blade.Value = true;
                    grab.Value = true;
                    setX.Value = StationX;
                    setZ.Value = 0f;
                    if (baseAtPlace.Value)
                        phase = Phase.PlaceTopLid;
                    else
                        forward1.Value = true;
                    break;
                case Phase.PlaceTopLid:
                    blade.Value = WaitForSecondBaseAfterAdvance;
                    grab.Value = true;
                    setX.Value = StationX;
                    setZ.Value = PlacementZ;
                    if (At(positionX.Value, StationX) && positionZ.Value > PlacementReachedZ)
                    {
                        counter.Value++;
                        phase = Phase.ReleaseTopLid;
                    }
                    break;
                case Phase.ReleaseTopLid:
                    blade.Value = false;
                    grab.Value = false;
                    setZ.Value = 0f;
                    phase = Phase.RetractAfterPlacement;
                    break;
                case Phase.RetractAfterPlacement:
                    blade.Value = false;
                    grab.Value = false;
                    setZ.Value = 0f;
                    if (At(positionZ.Value, 0f))
                    {
                        blade.Value = false;
                        forward1.Value = true;
                        forward2.Value = true;
                        phase = Phase.Complete;
                    }
                    break;
            }
        }

        void StopConveyors()
        {
            forward1.Value = false;
            forward2.Value = false;
            reverse1.Value = false;
            reverse2.Value = false;
            lidConveyor1.Value = false;
            lidConveyor2.Value = false;
        }

        public void Dispose()
        {
            if (!ModuleComplete)
                StopConveyors();
            else
            {
                reverse1.Value = false;
                reverse2.Value = false;
            }
        }

        public AssemblerAnalogRecoveryDecision CreateContinuationDecision()
        {
            if (!ModuleComplete)
                throw new InvalidOperationException("Finish transferring the extra lid before resuming.");
            return new AssemblerAnalogRecoveryDecision
            {
                LidsState = State.State5,
                BasesState = State.State2,
                Counter = counter.Value,
                StateIdentificationSatisfied = true,
                Reason = AdvanceUntilPartLeavingFalls
                    ? "First assembly left on PartLeaving falling edge; next base aligned and held lid placed; resume (5, 2)."
                    : "Bases advanced for 500 ms and held lid placed on second base; resume (5, 2)."
            };
        }

        static bool At(float actual, float target) => Math.Abs(actual - target) <= PositionTolerance;
        static MemoryBit Bit(string name, MemoryType type) => MemoryMap.Instance.GetBit(name, type)
            ?? throw new InvalidOperationException("Missing bit tag: " + name);
        static MemoryFloat Float(string name, MemoryType type) => MemoryMap.Instance.GetFloat(name, type)
            ?? throw new InvalidOperationException("Missing float tag: " + name);
    }

    // Wait for the first assembly to leave, then align the next base.
    public sealed class AssemblerAnalogStackedLidsSeparateBasesRecoveryModule : AssemblerAnalogStackedLidsAdjacentBasesRecoveryModule
    {
        protected override bool WaitForSecondBaseAfterAdvance => true;
        protected override bool AdvanceUntilPartLeavingFalls => true;

        public AssemblerAnalogStackedLidsSeparateBasesRecoveryModule(
            string forward1Tag, string reverse1Tag, string forward2Tag, string reverse2Tag)
            : base(forward1Tag, reverse1Tag, forward2Tag, reverse2Tag)
        {
        }
    }

    // Retrieve lid 2, lid 1, then both bases together with two sensor falling edges.
    public sealed class AssemblerAnalogFourPiecesBBLLRecoveryModule : Controller, IDisposable
    {
        enum Phase { Retract, Reverse, RaiseBladeForAlignment, Align, MoveToPickup, LowerPickup, Grip, LiftPickup,
            MoveToPark, LowerPark, ReleasePark, LiftPark, Place, Release, LiftPlaced, Discharge, Complete }
        readonly MemoryBit forward1, forward2, reverse1, reverse2, blade, lidBlade;
        readonly MemoryBit lid1, lid2, grab, baseAtPlace, itemDetected, partLeaving;
        readonly MemoryFloat setX, setZ, x, z;
        readonly MemoryInt counter;
        Phase phase;
        int retrieval; // 0: second lid, 1: first lid, 2: first base, 3: second base
        int retrievalFallingEdges;
        bool previousBase, previousLeaving, parkedPickup;
        long gripElapsed;
        float retractX;
        public bool ModuleComplete => phase == Phase.Complete;
        public string CurrentPhase => phase.ToString();
        public const float StationX = 8.85f, ParkX = 1.1f, PickupZ = 9f, PlacementZ = 10f;

        public AssemblerAnalogFourPiecesBBLLRecoveryModule(string f1, string r1, string f2, string r2)
        {
            forward1 = Bit(f1, MemoryType.Output); reverse1 = Bit(r1, MemoryType.Output);
            forward2 = Bit(f2, MemoryType.Output); reverse2 = Bit(r2, MemoryType.Output);
            blade = Bit("Stop blade 2", MemoryType.Output); lidBlade = Bit("Stop blade 1", MemoryType.Output);
            lid1 = Bit("Lids conveyor 1", MemoryType.Output); lid2 = Bit("Lids conveyor 2", MemoryType.Output);
            grab = Bit("Grab", MemoryType.Output); baseAtPlace = Bit("Base at place", MemoryType.Input);
            itemDetected = Bit("Item detected", MemoryType.Input); partLeaving = Bit("Part leaving", MemoryType.Input);
            setX = Float("Set X", MemoryType.Output); setZ = Float("Set Z", MemoryType.Output);
            x = Float("X", MemoryType.Input); z = Float("Z", MemoryType.Input);
            counter = MemoryMap.Instance.GetInt("Counter", MemoryType.Output)
                ?? throw new InvalidOperationException("Missing Counter output.");
            if (grab.Value) throw new InvalidOperationException("Two-base/two-lid recovery requires an initially empty gripper.");
            retractX = x.Value;
            Stop();
            setX.Value = retractX; setZ.Value = 0;
        }

        public override void Execute(int elapsedMilliseconds)
        {
            if (ModuleComplete) return;
            Stop();
            lidBlade.Value = true; // Keep the parked lid stationary.
            switch (phase)
            {
                case Phase.Retract:
                    setX.Value = retractX; setZ.Value = 0;
                    if (Near(z.Value, 0)) BeginRetrieval();
                    break;
                case Phase.Reverse:
                    setZ.Value = 0;
                    bool falling = previousBase && !baseAtPlace.Value;
                    previousBase = baseAtPlace.Value;
                    if (falling) retrievalFallingEdges++;
                    bool retrieved = retrievalFallingEdges >= (retrieval == 2 ? 2 : 1);
                    blade.Value = retrieved;
                    if (retrieved) phase = Phase.RaiseBladeForAlignment;
                    else { reverse1.Value = true; reverse2.Value = true; }
                    break;
                case Phase.RaiseBladeForAlignment:
                    blade.Value = true;
                    setZ.Value = 0;
                    phase = Phase.Align;
                    break;
                case Phase.Align:
                    blade.Value = true; setZ.Value = 0;
                    if (!baseAtPlace.Value) forward1.Value = true;
                    else if (retrieval < 2) { parkedPickup = false; phase = Phase.MoveToPickup; }
                    else if (retrieval == 2) phase = Phase.Place;
                    else { parkedPickup = true; phase = Phase.MoveToPickup; }
                    break;
                case Phase.MoveToPickup:
                    blade.Value = true; setZ.Value = 0; setX.Value = parkedPickup ? ParkX : StationX;
                    if (Near(x.Value, setX.Value) && Near(z.Value, 0)) phase = Phase.LowerPickup;
                    break;
                case Phase.LowerPickup:
                    blade.Value = true; setZ.Value = PickupZ;
                    if (itemDetected.Value) { grab.Value = true; gripElapsed = 0; phase = Phase.Grip; }
                    break;
                case Phase.Grip:
                    grab.Value = true; blade.Value = true;
                    gripElapsed += Math.Max(0, elapsedMilliseconds);
                    if (gripElapsed >= 1000) { setZ.Value = 0; phase = Phase.LiftPickup; }
                    break;
                case Phase.LiftPickup:
                    grab.Value = true; blade.Value = true; setZ.Value = 0;
                    if (Near(z.Value, 0))
                    {
                        if (parkedPickup) phase = Phase.Place;
                        else if (retrieval == 0) phase = Phase.MoveToPark;
                        else { retrieval = 2; BeginRetrieval(); }
                    }
                    break;
                case Phase.MoveToPark:
                    blade.Value = true; grab.Value = true; setX.Value = ParkX; setZ.Value = 0;
                    if (Near(x.Value, ParkX) && Near(z.Value, 0)) phase = Phase.LowerPark;
                    break;
                case Phase.LowerPark:
                    blade.Value = true; grab.Value = true; setZ.Value = PickupZ;
                    if (Near(z.Value, PickupZ)) phase = Phase.ReleasePark;
                    break;
                case Phase.ReleasePark:
                    blade.Value = true; grab.Value = false;
                    phase = Phase.LiftPark;
                    break;
                case Phase.LiftPark:
                    blade.Value = true; grab.Value = false; setZ.Value = 0;
                    if (Near(z.Value, 0)) { retrieval = 1; BeginRetrieval(); }
                    break;
                case Phase.Place:
                    blade.Value = true; grab.Value = true;
                    // Move horizontally while retracted before descending.
                    setX.Value = StationX;
                    setZ.Value = Near(x.Value, StationX) ? PlacementZ : 0;
                    if (Near(x.Value, StationX) && z.Value > 8.4f)
                    { counter.Value++; phase = Phase.Release; }
                    break;
                case Phase.Release:
                    blade.Value = true; grab.Value = false; phase = Phase.LiftPlaced;
                    break;
                case Phase.LiftPlaced:
                    blade.Value = true; grab.Value = false; setZ.Value = 0;
                    if (Near(z.Value, 0))
                    {
                        previousLeaving = partLeaving.Value;
                        blade.Value = false; forward1.Value = true; forward2.Value = true;
                        phase = Phase.Discharge;
                    }
                    break;
                case Phase.Discharge:
                    blade.Value = false; setZ.Value = 0; grab.Value = false;
                    bool left = previousLeaving && !partLeaving.Value;
                    previousLeaving = partLeaving.Value;
                    if (!left) { forward1.Value = true; forward2.Value = true; }
                    else if (retrieval == 2)
                    {
                        retrieval = 3;
                        blade.Value = true;
                        phase = Phase.RaiseBladeForAlignment;
                    }
                    else phase = Phase.Complete;
                    break;
            }
        }

        void BeginRetrieval()
        {
            previousBase = baseAtPlace.Value;
            retrievalFallingEdges = 0;
            phase = Phase.Reverse;
        }
        void Stop()
        {
            forward1.Value = forward2.Value = reverse1.Value = reverse2.Value = false;
            lid1.Value = lid2.Value = false;
        }
        public void Dispose() => Stop();
        public AssemblerAnalogRecoveryDecision CreateContinuationDecision()
        {
            if (!ModuleComplete) throw new InvalidOperationException("Finish both assemblies before resuming.");
            return new AssemblerAnalogRecoveryDecision {
                LidsState = State.State0, BasesState = State.State0, Counter = counter.Value,
                StateIdentificationSatisfied = true,
                Reason = "Both recovered lid/base pairs assembled and discharged; resume (0, 0)."
            };
        }
        static bool Near(float a, float b) => Math.Abs(a - b) <= 0.1f;
        static MemoryBit Bit(string n, MemoryType t) => MemoryMap.Instance.GetBit(n, t)
            ?? throw new InvalidOperationException("Missing bit tag: " + n);
        static MemoryFloat Float(string n, MemoryType t) => MemoryMap.Instance.GetFloat(n, t)
            ?? throw new InvalidOperationException("Missing float tag: " + n);
    }

    // Departure order is lid, base, lid, base; retrieve each pair with two falling edges.
    public sealed class AssemblerAnalogFourPiecesLBLBRecoveryModule : Controller, IDisposable
    {
        enum Phase { Retract, ReversePair, RaiseBlade, AlignLid, MovePickup, LowerPickup,
            Grip, LiftPickup, AlignBase, MovePlace, LowerPlace, ReleasePlace, LiftPlaced,
            MovePark, LowerPark, ReleasePark, LiftPark, Discharge, Complete }
        enum Pickup { LooseLid, FirstAssembly, ParkedAssembly }
        readonly MemoryBit f1, f2, r1, r2, blade, lidBlade, lid1, lid2, grab, atBase, detected, leaving;
        readonly MemoryFloat x, z, sx, sz;
        readonly MemoryInt counter;
        Phase phase;
        Pickup pickup;
        int pair, fallingEdges;
        bool previousBase, previousLeaving, returningAssembly;
        long gripElapsed;
        readonly float initialX;
        public const float StationX = 8.85f, ParkX = 1.1f, PickupZ = 9f, PlacementZ = 10f;
        public bool ModuleComplete => phase == Phase.Complete;
        public string CurrentPhase => phase.ToString();

        public AssemblerAnalogFourPiecesLBLBRecoveryModule(string forward1, string reverse1,
            string forward2, string reverse2)
        {
            f1 = Bit(forward1, MemoryType.Output); f2 = Bit(forward2, MemoryType.Output);
            r1 = Bit(reverse1, MemoryType.Output); r2 = Bit(reverse2, MemoryType.Output);
            blade = Bit("Stop blade 2", MemoryType.Output); lidBlade = Bit("Stop blade 1", MemoryType.Output);
            lid1 = Bit("Lids conveyor 1", MemoryType.Output); lid2 = Bit("Lids conveyor 2", MemoryType.Output);
            grab = Bit("Grab", MemoryType.Output); atBase = Bit("Base at place", MemoryType.Input);
            detected = Bit("Item detected", MemoryType.Input); leaving = Bit("Part leaving", MemoryType.Input);
            x = Float("X", MemoryType.Input); z = Float("Z", MemoryType.Input);
            sx = Float("Set X", MemoryType.Output); sz = Float("Set Z", MemoryType.Output);
            counter = MemoryMap.Instance.GetInt("Counter", MemoryType.Output)
                ?? throw new InvalidOperationException("Missing Counter output.");
            if (grab.Value) throw new InvalidOperationException("Alternating lid/base recovery requires an initially empty gripper.");
            initialX = x.Value; Stop(); sx.Value = initialX; sz.Value = 0;
        }

        public override void Execute(int elapsedMilliseconds)
        {
            if (ModuleComplete) return;
            Stop(); lidBlade.Value = true; blade.Value = true;
            switch (phase)
            {
                case Phase.Retract:
                    sx.Value = initialX; sz.Value = 0;
                    if (Near(z.Value, 0)) BeginPair();
                    break;
                case Phase.ReversePair:
                    sz.Value = 0;
                    if (previousBase && !atBase.Value) fallingEdges++;
                    previousBase = atBase.Value;
                    if (fallingEdges >= 2) phase = Phase.RaiseBlade;
                    else { blade.Value = false; r1.Value = r2.Value = true; }
                    break;
                case Phase.RaiseBlade:
                    sz.Value = 0; phase = Phase.AlignLid;
                    break;
                case Phase.AlignLid:
                    sz.Value = 0;
                    if (atBase.Value) { pickup = Pickup.LooseLid; phase = Phase.MovePickup; }
                    else f1.Value = true;
                    break;
                case Phase.MovePickup:
                    sx.Value = pickup == Pickup.ParkedAssembly ? ParkX : StationX; sz.Value = 0;
                    if (Near(x.Value, sx.Value) && Near(z.Value, 0)) phase = Phase.LowerPickup;
                    break;
                case Phase.LowerPickup:
                    sz.Value = PickupZ;
                    if (detected.Value) { grab.Value = true; gripElapsed = 0; phase = Phase.Grip; }
                    break;
                case Phase.Grip:
                    grab.Value = true; gripElapsed += Math.Max(0, elapsedMilliseconds);
                    if (gripElapsed >= 1000) { sz.Value = 0; phase = Phase.LiftPickup; }
                    break;
                case Phase.LiftPickup:
                    grab.Value = true; sz.Value = 0;
                    if (Near(z.Value, 0))
                        phase = pickup == Pickup.LooseLid ? Phase.AlignBase
                            : pickup == Pickup.FirstAssembly ? Phase.MovePark : Phase.MovePlace;
                    break;
                case Phase.AlignBase:
                    grab.Value = true; sz.Value = 0;
                    if (atBase.Value) phase = Phase.MovePlace;
                    else f1.Value = true;
                    break;
                case Phase.MovePlace:
                    grab.Value = true; sz.Value = 0; sx.Value = StationX;
                    if (Near(x.Value, StationX) && Near(z.Value, 0)) phase = Phase.LowerPlace;
                    break;
                case Phase.LowerPlace:
                    grab.Value = true; sz.Value = returningAssembly ? PickupZ : PlacementZ;
                    if (returningAssembly ? Near(z.Value, PickupZ) : z.Value > 8.4f)
                    {
                        if (!returningAssembly) counter.Value++;
                        phase = Phase.ReleasePlace;
                    }
                    break;
                case Phase.ReleasePlace:
                    grab.Value = false; phase = Phase.LiftPlaced;
                    break;
                case Phase.LiftPlaced:
                    grab.Value = false; sz.Value = 0;
                    if (Near(z.Value, 0))
                    {
                        if (pair == 0) { pickup = Pickup.FirstAssembly; phase = Phase.MovePickup; }
                        else BeginDischarge();
                    }
                    break;
                case Phase.MovePark:
                    grab.Value = true; sz.Value = 0; sx.Value = ParkX;
                    if (Near(x.Value, ParkX) && Near(z.Value, 0)) phase = Phase.LowerPark;
                    break;
                case Phase.LowerPark:
                    grab.Value = true; sz.Value = PickupZ;
                    if (Math.Abs(z.Value - PickupZ) <= 0.5f) phase = Phase.ReleasePark;
                    break;
                case Phase.ReleasePark:
                    grab.Value = false; phase = Phase.LiftPark;
                    break;
                case Phase.LiftPark:
                    grab.Value = false; sz.Value = 0;
                    if (Near(z.Value, 0)) { pair = 1; BeginPair(); }
                    break;
                case Phase.Discharge:
                    grab.Value = false; sz.Value = 0; blade.Value = false;
                    bool left = previousLeaving && !leaving.Value;
                    previousLeaving = leaving.Value;
                    if (!left) f1.Value = f2.Value = true;
                    else if (!returningAssembly)
                    {
                        blade.Value = true; returningAssembly = true;
                        pickup = Pickup.ParkedAssembly; phase = Phase.MovePickup;
                    }
                    else phase = Phase.Complete;
                    break;
            }
        }
        void BeginPair() { fallingEdges = 0; previousBase = atBase.Value; phase = Phase.ReversePair; }
        void BeginDischarge()
        {
            previousLeaving = leaving.Value; blade.Value = false;
            f1.Value = f2.Value = true; phase = Phase.Discharge;
        }
        void Stop() { f1.Value = f2.Value = r1.Value = r2.Value = lid1.Value = lid2.Value = false; }
        public void Dispose() => Stop();
        public AssemblerAnalogRecoveryDecision CreateContinuationDecision()
        {
            if (!ModuleComplete) throw new InvalidOperationException("Finish both assemblies before resuming.");
            return new AssemblerAnalogRecoveryDecision {
                LidsState = State.State0, BasesState = State.State0, Counter = counter.Value,
                StateIdentificationSatisfied = true,
                Reason = "Recovered alternating lid/base pairs, discharged second assembly then parked first assembly; resume (0, 0)."
            };
        }
        static bool Near(float a, float b) => Math.Abs(a - b) <= 0.1f;
        static MemoryBit Bit(string n, MemoryType t) => MemoryMap.Instance.GetBit(n, t)
            ?? throw new InvalidOperationException("Missing bit tag: " + n);
        static MemoryFloat Float(string n, MemoryType t) => MemoryMap.Instance.GetFloat(n, t)
            ?? throw new InvalidOperationException("Missing float tag: " + n);
    }

    public sealed class AssemblerAnalogHeldAssemblyBaseWithoutLidRecoveryModule : Controller, IDisposable
    {
        enum Phase { RetractHeld, MoveHeld, LowerHeld, ReleaseHeld, LiftEmpty, Reverse,
            RaiseBlade, AlignBase, MoveToLid, LowerToLid, GripLid, LiftLid,
            MoveToBase, PlaceLid, ReleaseLid, Retract, Deliver, Complete }
        readonly MemoryBit f1, f2, r1, r2, blade, lidBlade, lid1, lid2, grab, atBase, detected, leaving;
        readonly MemoryFloat x, z, sx, sz;
        readonly MemoryInt counter;
        readonly float initialX;
        Phase phase;
        bool previousBase, previousLeaving;
        int retrievalEdges, deliveryEdges;
        long gripElapsed;
        public bool ModuleComplete => phase == Phase.Complete;
        public string CurrentPhase => phase.ToString();
        public const float StationX = 8.85f, LidX = 1.1f, PickupZ = 9f;

        public AssemblerAnalogHeldAssemblyBaseWithoutLidRecoveryModule(string forward1, string reverse1,
            string forward2, string reverse2)
        {
            f1 = Bit(forward1, MemoryType.Output); f2 = Bit(forward2, MemoryType.Output);
            r1 = Bit(reverse1, MemoryType.Output); r2 = Bit(reverse2, MemoryType.Output);
            blade = Bit("Stop blade 2", MemoryType.Output); lidBlade = Bit("Stop blade 1", MemoryType.Output);
            lid1 = Bit("Lids conveyor 1", MemoryType.Output); lid2 = Bit("Lids conveyor 2", MemoryType.Output);
            grab = Bit("Grab", MemoryType.Output); atBase = Bit("Base at place", MemoryType.Input);
            detected = Bit("Item detected", MemoryType.Input); leaving = Bit("Part leaving", MemoryType.Input);
            x = Float("X", MemoryType.Input); z = Float("Z", MemoryType.Input);
            sx = Float("Set X", MemoryType.Output); sz = Float("Set Z", MemoryType.Output);
            counter = MemoryMap.Instance.GetInt("Counter", MemoryType.Output)
                ?? throw new InvalidOperationException("Missing Counter output.");
            initialX = x.Value; Stop(); grab.Value = true; sx.Value = initialX; sz.Value = 0;
        }
        public override void Execute(int elapsedMilliseconds)
        {
            if (ModuleComplete) return;
            Stop(); blade.Value = true; lidBlade.Value = true;
            switch (phase)
            {
                case Phase.RetractHeld:
                    grab.Value = true; sx.Value = initialX; sz.Value = 0;
                    if (Near(z.Value, 0)) phase = Phase.MoveHeld;
                    break;
                case Phase.MoveHeld:
                    grab.Value = true; sz.Value = 0; sx.Value = StationX;
                    if (Near(x.Value, StationX) && Near(z.Value, 0)) phase = Phase.LowerHeld;
                    break;
                case Phase.LowerHeld:
                    grab.Value = true; sz.Value = PickupZ;
                    if (Math.Abs(z.Value - PickupZ) <= 0.5f) phase = Phase.ReleaseHeld;
                    break;
                case Phase.ReleaseHeld:
                    grab.Value = false; phase = Phase.LiftEmpty;
                    break;
                case Phase.LiftEmpty:
                    grab.Value = false; sz.Value = 0;
                    if (Near(z.Value, 0))
                    { previousBase = atBase.Value; retrievalEdges = 0; phase = Phase.Reverse; }
                    break;
                case Phase.Reverse:
                    sz.Value = 0;
                    if (previousBase && !atBase.Value) retrievalEdges++;
                    previousBase = atBase.Value;
                    if (retrievalEdges >= 2) phase = Phase.RaiseBlade;
                    else { blade.Value = false; r1.Value = r2.Value = true; }
                    break;
                case Phase.RaiseBlade:
                    sz.Value = 0; phase = Phase.AlignBase;
                    break;
                case Phase.AlignBase:
                    sz.Value = 0;
                    if (atBase.Value) phase = Phase.MoveToLid;
                    else f1.Value = true;
                    break;
                case Phase.MoveToLid:
                    sz.Value = 0; sx.Value = LidX;
                    if (Near(x.Value, LidX) && Near(z.Value, 0)) phase = Phase.LowerToLid;
                    break;
                case Phase.LowerToLid:
                    sz.Value = PickupZ;
                    if (detected.Value) { grab.Value = true; gripElapsed = 0; phase = Phase.GripLid; }
                    break;
                case Phase.GripLid:
                    grab.Value = true; gripElapsed += Math.Max(0, elapsedMilliseconds);
                    if (gripElapsed >= 1000) { sz.Value = 0; phase = Phase.LiftLid; }
                    break;
                case Phase.LiftLid:
                    grab.Value = true; sz.Value = 0;
                    if (Near(z.Value, 0)) phase = Phase.MoveToBase;
                    break;
                case Phase.MoveToBase:
                    grab.Value = true; sz.Value = 0; sx.Value = StationX;
                    if (Near(x.Value, StationX) && Near(z.Value, 0)) phase = Phase.PlaceLid;
                    break;
                case Phase.PlaceLid:
                    grab.Value = true; sz.Value = 10;
                    if (z.Value > 8.4f) { counter.Value++; phase = Phase.ReleaseLid; }
                    break;
                case Phase.ReleaseLid:
                    grab.Value = false; phase = Phase.Retract;
                    break;
                case Phase.Retract:
                    grab.Value = false; sz.Value = 0;
                    if (Near(z.Value, 0))
                    {
                        previousLeaving = leaving.Value; deliveryEdges = 0;
                        blade.Value = false; f1.Value = f2.Value = true; phase = Phase.Deliver;
                    }
                    break;
                case Phase.Deliver:
                    blade.Value = false; sz.Value = 0; grab.Value = false;
                    if (previousLeaving && !leaving.Value) deliveryEdges++;
                    previousLeaving = leaving.Value;
                    if (deliveryEdges >= 2) phase = Phase.Complete;
                    else f1.Value = f2.Value = true;
                    break;
            }
        }
        void Stop() { f1.Value = f2.Value = r1.Value = r2.Value = lid1.Value = lid2.Value = false; }
        public void Dispose() => Stop();
        public AssemblerAnalogRecoveryDecision CreateContinuationDecision()
        {
            if (!ModuleComplete) throw new InvalidOperationException("Wait for two assemblies to leave.");
            return new AssemblerAnalogRecoveryDecision {
                LidsState = State.State0, BasesState = State.State0, Counter = counter.Value,
                StateIdentificationSatisfied = true,
                Reason = "Held assembly returned, two bases retrieved, remaining lid placed and two assemblies discharged; resume (0, 0)."
            };
        }
        static bool Near(float a, float b) => Math.Abs(a - b) <= 0.1f;
        static MemoryBit Bit(string n, MemoryType t) => MemoryMap.Instance.GetBit(n, t)
            ?? throw new InvalidOperationException("Missing bit tag: " + n);
        static MemoryFloat Float(string n, MemoryType t) => MemoryMap.Instance.GetFloat(n, t)
            ?? throw new InvalidOperationException("Missing float tag: " + n);
    }

    public sealed class AssemblerAnalogCorrectAssemblyRecoveryModule : Controller, IDisposable
    {
        enum Phase
        {
            Retract, ReverseToLid, RaiseBlade, FeedLidToBlade, MoveToLid, LowerToLid, GrabLid,
            LiftLid, MoveToPlacement, PlaceLid, ReleaseLid, RetractAfterPlacement, Complete
        }

        // Retrieve the assembly once, then reposition its lid while the base remains stopped.
        public const float StationX = 8.85f;
        public const float LidPickupZ = 9f;
        public const float PlacementZ = 10f;
        public const float PlacementReachedZ = 8.4f;
        public const float PositionTolerance = 0.1f;
        public const int GrabDelayMilliseconds = 1000;

        readonly MemoryBit forward1, forward2, reverse1, reverse2;
        readonly MemoryBit lidConveyor1, lidConveyor2, blade, grab, baseAtPlace, itemDetected;
        readonly MemoryFloat setX, setZ, positionX, positionZ;
        readonly MemoryInt counter;
        readonly float initialX;
        public float PickupX { get; }
        Phase phase;
        bool previousRetrievalBaseAtPlace;
        long grabElapsed;

        public bool ModuleComplete => phase == Phase.Complete;
        public string CurrentPhase => phase.ToString();

        public AssemblerAnalogCorrectAssemblyRecoveryModule(
            string forward1Tag, string reverse1Tag, string forward2Tag, string reverse2Tag, float pickupX)
        {
            if (float.IsNaN(pickupX) || float.IsInfinity(pickupX))
                throw new ArgumentOutOfRangeException(nameof(pickupX));
            PickupX = pickupX;
            forward1 = Bit(forward1Tag, MemoryType.Output);
            forward2 = Bit(forward2Tag, MemoryType.Output);
            reverse1 = Bit(reverse1Tag, MemoryType.Output);
            reverse2 = Bit(reverse2Tag, MemoryType.Output);
            lidConveyor1 = Bit("Lids conveyor 1", MemoryType.Output);
            lidConveyor2 = Bit("Lids conveyor 2", MemoryType.Output);
            blade = Bit("Stop blade 2", MemoryType.Output);
            grab = Bit("Grab", MemoryType.Output);
            baseAtPlace = Bit("Base at place", MemoryType.Input);
            itemDetected = Bit("Item detected", MemoryType.Input);
            setX = Float("Set X", MemoryType.Output);
            setZ = Float("Set Z", MemoryType.Output);
            positionX = Float("X", MemoryType.Input);
            positionZ = Float("Z", MemoryType.Input);
            counter = MemoryMap.Instance.GetInt("Counter", MemoryType.Output)
                ?? throw new InvalidOperationException("Missing Counter output.");
            if (grab.Value)
                throw new InvalidOperationException("Misplaced-assembly recovery requires an initially empty gripper.");
            initialX = positionX.Value;
            StopConveyors();
            setX.Value = initialX;
            setZ.Value = 0f;
        }

        public override void Execute(int elapsedMilliseconds)
        {
            if (ModuleComplete)
                return;
            StopConveyors();
            switch (phase)
            {
                case Phase.Retract:
                    setX.Value = initialX;
                    setZ.Value = 0f;
                    if (At(positionZ.Value, 0f))
                    {
                        previousRetrievalBaseAtPlace = baseAtPlace.Value;
                        phase = Phase.ReverseToLid;
                    }
                    break;
                case Phase.ReverseToLid:
                    setZ.Value = 0f;
                    if (ReverseUntilFallingEdge())
                        phase = Phase.RaiseBlade;
                    break;
                case Phase.RaiseBlade:
                    blade.Value = true;
                    setZ.Value = 0f;
                    phase = Phase.FeedLidToBlade;
                    break;
                case Phase.FeedLidToBlade:
                    setZ.Value = 0f;
                    if (FeedFirstConveyorUntilPresent())
                    {
                        setX.Value = PickupX;
                        phase = Phase.MoveToLid;
                    }
                    break;
                case Phase.MoveToLid:
                    blade.Value = true;
                    setZ.Value = 0f;
                    setX.Value = PickupX;
                    if (At(positionX.Value, PickupX) && At(positionZ.Value, 0f))
                        phase = Phase.LowerToLid;
                    break;
                case Phase.LowerToLid:
                    blade.Value = true;
                    setX.Value = PickupX;
                    setZ.Value = LidPickupZ;
                    if (itemDetected.Value)
                    {
                        grab.Value = true;
                        grabElapsed = 0;
                        phase = Phase.GrabLid;
                    }
                    break;
                case Phase.GrabLid:
                    grab.Value = true;
                    grabElapsed += Math.Max(0, elapsedMilliseconds);
                    if (grabElapsed >= GrabDelayMilliseconds)
                    {
                        setZ.Value = 0f;
                        phase = Phase.LiftLid;
                    }
                    break;
                case Phase.LiftLid:
                    grab.Value = true;
                    setZ.Value = 0f;
                    if (At(positionZ.Value, 0f))
                    {
                        phase = Phase.MoveToPlacement;
                    }
                    break;
                case Phase.MoveToPlacement:
                    blade.Value = true;
                    grab.Value = true;
                    setZ.Value = 0f;
                    setX.Value = StationX;
                    if (At(positionX.Value, StationX) && At(positionZ.Value, 0f))
                        phase = Phase.PlaceLid;
                    break;
                case Phase.PlaceLid:
                    blade.Value = true;
                    grab.Value = true;
                    setX.Value = StationX;
                    setZ.Value = PlacementZ;
                    if (At(positionX.Value, StationX) && positionZ.Value > PlacementReachedZ)
                    {
                        // Correcting an existing assembly does not increment its count.
                        phase = Phase.ReleaseLid;
                    }
                    break;
                case Phase.ReleaseLid:
                    blade.Value = true;
                    grab.Value = false;
                    setZ.Value = 0f;
                    phase = Phase.RetractAfterPlacement;
                    break;
                case Phase.RetractAfterPlacement:
                    blade.Value = true;
                    grab.Value = false;
                    setZ.Value = 0f;
                    if (At(positionZ.Value, 0f))
                    {
                        blade.Value = false;
                        forward1.Value = true;
                        forward2.Value = true;
                        phase = Phase.Complete;
                    }
                    break;
            }
        }

        bool ReverseUntilFallingEdge()
        {
            bool fallingEdge = previousRetrievalBaseAtPlace && !baseAtPlace.Value;
            previousRetrievalBaseAtPlace = baseAtPlace.Value;
            if (fallingEdge)
            {
                blade.Value = true;
                return true;
            }
            blade.Value = false;
            reverse1.Value = true;
            reverse2.Value = true;
            return false;
        }

        bool FeedFirstConveyorUntilPresent()
        {
            blade.Value = true;
            if (baseAtPlace.Value)
                return true;
            // StopConveyors() has already disabled both reverse outputs and conveyor 2.
            forward1.Value = true;
            return false;
        }

        void StopConveyors()
        {
            forward1.Value = false;
            forward2.Value = false;
            reverse1.Value = false;
            reverse2.Value = false;
            lidConveyor1.Value = false;
            lidConveyor2.Value = false;
        }

        public void Dispose()
        {
            if (!ModuleComplete)
                StopConveyors();
            else
            {
                // Preserve the forward discharge commands during nominal handover.
                reverse1.Value = false;
                reverse2.Value = false;
            }
        }

        public AssemblerAnalogRecoveryDecision CreateContinuationDecision()
        {
            if (!ModuleComplete)
                throw new InvalidOperationException("Finish assembly and gripper retraction before resuming discharge.");
            return new AssemblerAnalogRecoveryDecision
            {
                LidsState = State.State5,
                BasesState = State.State2,
                Counter = counter.Value,
                StateIdentificationSatisfied = true,
                Reason = "Retrieved assembly and corrected lid placement; resume (5, 2)."
            };
        }

        static bool At(float actual, float target) => Math.Abs(actual - target) <= PositionTolerance;
        static MemoryBit Bit(string name, MemoryType type) => MemoryMap.Instance.GetBit(name, type)
            ?? throw new InvalidOperationException("Missing bit tag: " + name);
        static MemoryFloat Float(string name, MemoryType type) => MemoryMap.Instance.GetFloat(name, type)
            ?? throw new InvalidOperationException("Missing float tag: " + name);
    }

    public sealed class AssemblerAnalogRepositionLidRecoveryModule : Controller, IDisposable
    {
        enum Phase
        {
            Retract, ReverseToLid, RaiseBlade, FeedLidToBlade, MoveToLid, LowerToLid, GrabLid,
            LiftLid, Complete
        }

        // Retrieve the base, pick up a lid from the configured location, and hand off to nominal placement.
        public const float StationX = 8.85f;
        public const float LidPickupZ = 9f;
        public const float PlacementZ = 10f;
        public const float PlacementReachedZ = 8.4f;
        public const float PositionTolerance = 0.1f;
        public const int GrabDelayMilliseconds = 1000;

        readonly MemoryBit forward1, forward2, reverse1, reverse2;
        readonly MemoryBit lidConveyor1, lidConveyor2, blade, grab, baseAtPlace, itemDetected;
        readonly MemoryFloat setX, setZ, positionX, positionZ;
        readonly MemoryInt counter;
        readonly float initialX;
        public float PickupX { get; }
        public float PickupZ { get; }
        Phase phase;
        bool previousRetrievalBaseAtPlace;
        long grabElapsed;

        public bool ModuleComplete => phase == Phase.Complete;
        public string CurrentPhase => phase.ToString();

        public AssemblerAnalogRepositionLidRecoveryModule(
            string forward1Tag, string reverse1Tag, string forward2Tag, string reverse2Tag, float pickupX, float pickupZ)
        {
            if (float.IsNaN(pickupX) || float.IsInfinity(pickupX))
                throw new ArgumentOutOfRangeException(nameof(pickupX));
            if (float.IsNaN(pickupZ) || float.IsInfinity(pickupZ))
                throw new ArgumentOutOfRangeException(nameof(pickupZ));
            PickupX = pickupX;
            PickupZ = pickupZ;
            forward1 = Bit(forward1Tag, MemoryType.Output);
            forward2 = Bit(forward2Tag, MemoryType.Output);
            reverse1 = Bit(reverse1Tag, MemoryType.Output);
            reverse2 = Bit(reverse2Tag, MemoryType.Output);
            lidConveyor1 = Bit("Lids conveyor 1", MemoryType.Output);
            lidConveyor2 = Bit("Lids conveyor 2", MemoryType.Output);
            blade = Bit("Stop blade 2", MemoryType.Output);
            grab = Bit("Grab", MemoryType.Output);
            baseAtPlace = Bit("Base at place", MemoryType.Input);
            itemDetected = Bit("Item detected", MemoryType.Input);
            setX = Float("Set X", MemoryType.Output);
            setZ = Float("Set Z", MemoryType.Output);
            positionX = Float("X", MemoryType.Input);
            positionZ = Float("Z", MemoryType.Input);
            counter = MemoryMap.Instance.GetInt("Counter", MemoryType.Output)
                ?? throw new InvalidOperationException("Missing Counter output.");
            if (grab.Value)
                throw new InvalidOperationException("Base-and-lid recovery requires an initially empty gripper.");
            initialX = positionX.Value;
            StopConveyors();
            setX.Value = initialX;
            setZ.Value = 0f;
        }

        public override void Execute(int elapsedMilliseconds)
        {
            if (ModuleComplete)
                return;
            StopConveyors();
            switch (phase)
            {
                case Phase.Retract:
                    setX.Value = initialX;
                    setZ.Value = 0f;
                    if (At(positionZ.Value, 0f))
                    {
                        previousRetrievalBaseAtPlace = baseAtPlace.Value;
                        phase = Phase.ReverseToLid;
                    }
                    break;
                case Phase.ReverseToLid:
                    setZ.Value = 0f;
                    if (ReverseUntilFallingEdge())
                        phase = Phase.RaiseBlade;
                    break;
                case Phase.RaiseBlade:
                    blade.Value = true;
                    setZ.Value = 0f;
                    phase = Phase.FeedLidToBlade;
                    break;
                case Phase.FeedLidToBlade:
                    setZ.Value = 0f;
                    if (FeedFirstConveyorUntilPresent())
                    {
                        setX.Value = PickupX;
                        phase = Phase.MoveToLid;
                    }
                    break;
                case Phase.MoveToLid:
                    blade.Value = true;
                    setZ.Value = 0f;
                    setX.Value = PickupX;
                    if (At(positionX.Value, PickupX) && At(positionZ.Value, 0f))
                        phase = Phase.LowerToLid;
                    break;
                case Phase.LowerToLid:
                    blade.Value = true;
                    setX.Value = PickupX;
                    setZ.Value = PickupZ;
                    if (itemDetected.Value)
                    {
                        grab.Value = true;
                        grabElapsed = 0;
                        phase = Phase.GrabLid;
                    }
                    break;
                case Phase.GrabLid:
                    grab.Value = true;
                    grabElapsed += Math.Max(0, elapsedMilliseconds);
                    if (grabElapsed >= GrabDelayMilliseconds)
                    {
                        setZ.Value = 0f;
                        phase = Phase.LiftLid;
                    }
                    break;
                case Phase.LiftLid:
                    blade.Value = true;
                    grab.Value = true;
                    setZ.Value = 0f;
                    if (At(positionZ.Value, 0f))
                        phase = Phase.Complete;
                    break;
            }
        }

        bool ReverseUntilFallingEdge()
        {
            bool fallingEdge = previousRetrievalBaseAtPlace && !baseAtPlace.Value;
            previousRetrievalBaseAtPlace = baseAtPlace.Value;
            if (fallingEdge)
            {
                blade.Value = true;
                return true;
            }
            blade.Value = false;
            reverse1.Value = true;
            reverse2.Value = true;
            return false;
        }

        bool FeedFirstConveyorUntilPresent()
        {
            blade.Value = true;
            if (baseAtPlace.Value)
                return true;
            // StopConveyors() has already disabled both reverse outputs and conveyor 2.
            forward1.Value = true;
            return false;
        }

        void StopConveyors()
        {
            forward1.Value = false;
            forward2.Value = false;
            reverse1.Value = false;
            reverse2.Value = false;
            lidConveyor1.Value = false;
            lidConveyor2.Value = false;
        }

        public void Dispose()
        {
            if (!ModuleComplete)
                StopConveyors();
            else
            {
                // Preserve the forward discharge commands during nominal handover.
                reverse1.Value = false;
                reverse2.Value = false;
            }
        }

        public AssemblerAnalogRecoveryDecision CreateContinuationDecision()
        {
            if (!ModuleComplete)
                throw new InvalidOperationException("Finish base retrieval, lid pickup and gripper retraction before resuming.");
            return new AssemblerAnalogRecoveryDecision
            {
                LidsState = State.State3,
                BasesState = State.State1,
                Counter = counter.Value,
                StateIdentificationSatisfied = true,
                Reason = "Base retrieved and lid held with gripper retracted; resume (3, 1)."
            };
        }

        static bool At(float actual, float target) => Math.Abs(actual - target) <= PositionTolerance;
        static MemoryBit Bit(string name, MemoryType type) => MemoryMap.Instance.GetBit(name, type)
            ?? throw new InvalidOperationException("Missing bit tag: " + name);
        static MemoryFloat Float(string name, MemoryType type) => MemoryMap.Instance.GetFloat(name, type)
            ?? throw new InvalidOperationException("Missing float tag: " + name);
    }

    public sealed class AssemblerAnalogRepositionBaseRecoveryModule : Controller, IDisposable
    {
        enum Phase
        {
            Retract, ReverseToBase, RaiseBlade, FeedBaseToBlade, MoveToBase, LowerToBase, GrabBase,
            LiftBase, MoveToPlacement, PlaceBase, ReleaseBase, RetractAfterPlacement, ReturnToPickup, Complete
        }

        // Retrieve and reposition a base, then return the empty gripper to the lid pickup station.
        public const float StationX = 8.85f;
        public float PickupZ { get; }
        public const float PlacementZ = 9f;
        public const float PositionTolerance = 0.1f;
        public const int GrabDelayMilliseconds = 1000;

        readonly MemoryBit forward1, forward2, reverse1, reverse2;
        readonly MemoryBit lidConveyor1, lidConveyor2, blade, grab, baseAtPlace, itemDetected;
        readonly MemoryFloat setX, setZ, positionX, positionZ;
        readonly MemoryInt counter;
        readonly float initialX;
        public float PickupX { get; }
        Phase phase;
        bool previousRetrievalBaseAtPlace;
        long grabElapsed;

        public bool ModuleComplete => phase == Phase.Complete;
        public string CurrentPhase => phase.ToString();

        public AssemblerAnalogRepositionBaseRecoveryModule(
            string forward1Tag, string reverse1Tag, string forward2Tag, string reverse2Tag, float pickupX, float pickupZ)
        {
            if (float.IsNaN(pickupX) || float.IsInfinity(pickupX))
                throw new ArgumentOutOfRangeException(nameof(pickupX));
            if (float.IsNaN(pickupZ) || float.IsInfinity(pickupZ))
                throw new ArgumentOutOfRangeException(nameof(pickupZ));
            PickupX = pickupX;
            PickupZ = pickupZ;
            forward1 = Bit(forward1Tag, MemoryType.Output);
            forward2 = Bit(forward2Tag, MemoryType.Output);
            reverse1 = Bit(reverse1Tag, MemoryType.Output);
            reverse2 = Bit(reverse2Tag, MemoryType.Output);
            lidConveyor1 = Bit("Lids conveyor 1", MemoryType.Output);
            lidConveyor2 = Bit("Lids conveyor 2", MemoryType.Output);
            blade = Bit("Stop blade 2", MemoryType.Output);
            grab = Bit("Grab", MemoryType.Output);
            baseAtPlace = Bit("Base at place", MemoryType.Input);
            itemDetected = Bit("Item detected", MemoryType.Input);
            setX = Float("Set X", MemoryType.Output);
            setZ = Float("Set Z", MemoryType.Output);
            positionX = Float("X", MemoryType.Input);
            positionZ = Float("Z", MemoryType.Input);
            counter = MemoryMap.Instance.GetInt("Counter", MemoryType.Output)
                ?? throw new InvalidOperationException("Missing Counter output.");
            if (grab.Value)
                throw new InvalidOperationException("Base-reposition recovery requires an initially empty gripper.");
            initialX = positionX.Value;
            StopConveyors();
            setX.Value = initialX;
            setZ.Value = 0f;
        }

        public override void Execute(int elapsedMilliseconds)
        {
            if (ModuleComplete)
                return;
            StopConveyors();
            switch (phase)
            {
                case Phase.Retract:
                    setX.Value = initialX;
                    setZ.Value = 0f;
                    if (At(positionZ.Value, 0f))
                    {
                        previousRetrievalBaseAtPlace = baseAtPlace.Value;
                        phase = Phase.ReverseToBase;
                    }
                    break;
                case Phase.ReverseToBase:
                    setZ.Value = 0f;
                    if (ReverseUntilFallingEdge())
                        phase = Phase.RaiseBlade;
                    break;
                case Phase.RaiseBlade:
                    blade.Value = true;
                    setZ.Value = 0f;
                    phase = Phase.FeedBaseToBlade;
                    break;
                case Phase.FeedBaseToBlade:
                    setZ.Value = 0f;
                    if (FeedFirstConveyorUntilPresent())
                    {
                        setX.Value = PickupX;
                        phase = Phase.MoveToBase;
                    }
                    break;
                case Phase.MoveToBase:
                    blade.Value = true;
                    setZ.Value = 0f;
                    setX.Value = PickupX;
                    if (At(positionX.Value, PickupX) && At(positionZ.Value, 0f))
                        phase = Phase.LowerToBase;
                    break;
                case Phase.LowerToBase:
                    blade.Value = true;
                    setX.Value = PickupX;
                    setZ.Value = PickupZ;
                    if (itemDetected.Value)
                    {
                        grab.Value = true;
                        grabElapsed = 0;
                        phase = Phase.GrabBase;
                    }
                    break;
                case Phase.GrabBase:
                    grab.Value = true;
                    grabElapsed += Math.Max(0, elapsedMilliseconds);
                    if (grabElapsed >= GrabDelayMilliseconds)
                    {
                        setZ.Value = 0f;
                        phase = Phase.LiftBase;
                    }
                    break;
                case Phase.LiftBase:
                    grab.Value = true;
                    setZ.Value = 0f;
                    if (At(positionZ.Value, 0f))
                    {
                        phase = Phase.MoveToPlacement;
                    }
                    break;
                case Phase.MoveToPlacement:
                    blade.Value = true;
                    grab.Value = true;
                    setZ.Value = 0f;
                    setX.Value = StationX;
                    if (At(positionX.Value, StationX) && At(positionZ.Value, 0f))
                        phase = Phase.PlaceBase;
                    break;
                case Phase.PlaceBase:
                    blade.Value = true;
                    grab.Value = true;
                    setX.Value = StationX;
                    setZ.Value = PlacementZ;
                    if (At(positionX.Value, StationX) && At(positionZ.Value, PlacementZ))
                    {
                        // Repositioning a base does not increment the assembly count.
                        phase = Phase.ReleaseBase;
                    }
                    break;
                case Phase.ReleaseBase:
                    blade.Value = true;
                    grab.Value = false;
                    setZ.Value = 0f;
                    phase = Phase.RetractAfterPlacement;
                    break;
                case Phase.RetractAfterPlacement:
                    blade.Value = true;
                    grab.Value = false;
                    setZ.Value = 0f;
                    if (At(positionZ.Value, 0f))
                    {
                        phase = Phase.ReturnToPickup;
                    }
                    break;
                case Phase.ReturnToPickup:
                    blade.Value = true;
                    grab.Value = false;
                    setZ.Value = 0f;
                    setX.Value = 1.1f;
                    if (At(positionX.Value, 1.1f) && At(positionZ.Value, 0f))
                        phase = Phase.Complete;
                    break;
            }
        }

        bool ReverseUntilFallingEdge()
        {
            bool fallingEdge = previousRetrievalBaseAtPlace && !baseAtPlace.Value;
            previousRetrievalBaseAtPlace = baseAtPlace.Value;
            if (fallingEdge)
            {
                blade.Value = true;
                return true;
            }
            blade.Value = false;
            reverse1.Value = true;
            reverse2.Value = true;
            return false;
        }

        bool FeedFirstConveyorUntilPresent()
        {
            blade.Value = true;
            if (baseAtPlace.Value)
                return true;
            // StopConveyors() has already disabled both reverse outputs and conveyor 2.
            forward1.Value = true;
            return false;
        }

        void StopConveyors()
        {
            forward1.Value = false;
            forward2.Value = false;
            reverse1.Value = false;
            reverse2.Value = false;
            lidConveyor1.Value = false;
            lidConveyor2.Value = false;
        }

        public void Dispose()
        {
            if (!ModuleComplete)
                StopConveyors();
            else
            {
                // Preserve the forward discharge commands during nominal handover.
                reverse1.Value = false;
                reverse2.Value = false;
            }
        }

        public AssemblerAnalogRecoveryDecision CreateContinuationDecision()
        {
            if (!ModuleComplete)
                throw new InvalidOperationException("Finish base repositioning and return the gripper before resuming.");
            return new AssemblerAnalogRecoveryDecision
            {
                LidsState = State.State1,
                BasesState = State.State1,
                Counter = counter.Value,
                StateIdentificationSatisfied = true,
                Reason = "Base retrieved and repositioned; empty gripper at lid pickup; resume (1, 1)."
            };
        }

        static bool At(float actual, float target) => Math.Abs(actual - target) <= PositionTolerance;
        static MemoryBit Bit(string name, MemoryType type) => MemoryMap.Instance.GetBit(name, type)
            ?? throw new InvalidOperationException("Missing bit tag: " + name);
        static MemoryFloat Float(string name, MemoryType type) => MemoryMap.Instance.GetFloat(name, type)
            ?? throw new InvalidOperationException("Missing float tag: " + name);
    }
}
