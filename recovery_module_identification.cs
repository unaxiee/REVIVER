//-----------------------------------------------------------------------------
// FACTORY I/O (SDK)
//
// Stage 2 recovery module identification for PickPlaceXYZ recovery.
//-----------------------------------------------------------------------------

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
        MisalignmentPallet,
        Placeholder
    }

    public sealed class PickPlaceXYZRecoveryModuleDecision
    {
        public PickPlaceXYZRecoveryDecision RecoveryDecision { get; set; }
        public string Reason { get; set; }
    }

    public static class PickPlaceXYZRecoveryModuleIdentifier
    {
        public static PickPlaceXYZRecoveryModuleDecision Decide(
            PickPlaceXYZSnapshot snapshot,
            PickPlaceXYZRecoveryDecision stateDecision,
            string recoveryCase)
        {
            if (IsOverflowRecoveryCase(snapshot, stateDecision, recoveryCase))
                return OverflowDecision(snapshot, stateDecision,
                    "Stage 2 recovery module placeholder: overflow module selected.");

            if (IsMisalignmentBeltConveyorRecoveryCase(snapshot, stateDecision, recoveryCase))
                return MisalignmentBeltConveyorDecision(stateDecision,
                    "Stage 2 recovery module: misalignment_beltconveyor module selected.");

            if (IsUnderflowRecoveryCase(snapshot, stateDecision, recoveryCase))
                return UnderflowDecision(snapshot, stateDecision,
                    "Stage 2 recovery module: underflow module selected.");

            if (IsMisalignmentPalletRecoveryCase(snapshot, stateDecision, recoveryCase))
                return MisalignmentPalletDecision(snapshot, stateDecision,
                    "Stage 2 recovery module: misalignment_pallet module selected.");

            if (IsPlaceholderRecoveryCase(snapshot, stateDecision, recoveryCase))
                return ModuleDecision(stateDecision, RecoveryModule.Placeholder,
                    "Stage 2 recovery module placeholder: placeholder module selected.");

            return ModuleDecision(stateDecision, RecoveryModule.BenignResume,
                "Stage 2 recovery module placeholder: no additional recovery module selected.");
        }

        static bool IsOverflowRecoveryCase(
            PickPlaceXYZSnapshot snapshot,
            PickPlaceXYZRecoveryDecision stateDecision,
            string recoveryCase)
        {
            // Placeholder: Program_Recovery currently routes match.csv label "overflow" here.
            return !stateDecision.StateIdentificationSatisfied && recoveryCase == "overflow";
        }

        static bool IsMisalignmentBeltConveyorRecoveryCase(
            PickPlaceXYZSnapshot snapshot,
            PickPlaceXYZRecoveryDecision stateDecision,
            string recoveryCase)
        {
            return !stateDecision.StateIdentificationSatisfied && recoveryCase == "misalignment_beltconveyor";
        }

        static bool IsPlaceholderRecoveryCase(
            PickPlaceXYZSnapshot snapshot,
            PickPlaceXYZRecoveryDecision stateDecision,
            string recoveryCase)
        {
            // Placeholder for a future additional recovery module.
            return !stateDecision.StateIdentificationSatisfied && recoveryCase == "placeholder";
        }

        static bool IsUnderflowRecoveryCase(
            PickPlaceXYZSnapshot snapshot,
            PickPlaceXYZRecoveryDecision stateDecision,
            string recoveryCase)
        {
            return !stateDecision.StateIdentificationSatisfied && recoveryCase == "underflow";
        }

        static bool IsMisalignmentPalletRecoveryCase(
            PickPlaceXYZSnapshot snapshot,
            PickPlaceXYZRecoveryDecision stateDecision,
            string recoveryCase)
        {
            return !stateDecision.StateIdentificationSatisfied && recoveryCase == "misalignment_pallet";
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

        static PickPlaceXYZRecoveryModuleDecision MisalignmentPalletDecision(
            PickPlaceXYZSnapshot snapshot,
            PickPlaceXYZRecoveryDecision stateDecision,
            string reason)
        {
            PickPlaceXYZRecoveryDecision recoveryDecision = CopyDecision(stateDecision);
            recoveryDecision.RecoveryModule = RecoveryModule.MisalignmentPallet;
            recoveryDecision.PickingState = State.State0;
            recoveryDecision.GrabState = State.State0;
            recoveryDecision.Counter = 0;
            recoveryDecision.ExitBox = 0;
            recoveryDecision.GrabReleaseOperations = BuildMisalignmentPalletOperations(snapshot);
            recoveryDecision.Reason =
                $"{recoveryDecision.Reason} {reason} Misalignment pallet recovery will run {recoveryDecision.GrabReleaseOperations.Length} configured grab-release operations before benign resume.";

            return new PickPlaceXYZRecoveryModuleDecision
            {
                RecoveryDecision = recoveryDecision,
                Reason = reason
            };
        }

        static PickPlaceXYZGrabReleaseOperation[] BuildMisalignmentPalletOperations(PickPlaceXYZSnapshot snapshot)
        {
            if (IsThreeBoxMisaligned(snapshot))
                return BuildThreeBoxMisalignedOperations();

            if (IsBottomBoxMisaligned(snapshot))
                return BuildBottomBoxMisalignedOperations();

            if (IsTopBoxMisaligned(snapshot))
                return BuildTopBoxMisalignedOperations();

            return BuildThreeBoxMisalignedOperations();
        }

        static bool IsTopBoxMisaligned(PickPlaceXYZSnapshot snapshot)
        {
            // Case 0 placeholder: only the top box is misaligned on the pallet.
            // The top box uses the third pallet coordinate in the normal stacking order.
            return false;
        }

        static bool IsBottomBoxMisaligned(PickPlaceXYZSnapshot snapshot)
        {
            // Case 2 placeholder: the bottom box is misaligned, so all three pallet boxes are relocated.
            return false;
        }

        static bool IsThreeBoxMisaligned(PickPlaceXYZSnapshot snapshot)
        {
            // Case 3 placeholder: all three pallet boxes are misaligned and require four relocations.
            return false;
        }

        static PickPlaceXYZGrabReleaseOperation[] BuildTopBoxMisalignedOperations()
        {
            return new[]
            {
                new PickPlaceXYZGrabReleaseOperation(3.1f, 5.7f, 5f, 3.1f, 5.3f, 5f, true, true)
            };
        }

        static PickPlaceXYZGrabReleaseOperation[] BuildBottomBoxMisalignedOperations()
        {
            return new[]
            {
                new PickPlaceXYZGrabReleaseOperation(3.1f, 5.3f, 5f, 8.3f, 5.5f, 0.2f, true, false),
                new PickPlaceXYZGrabReleaseOperation(3.1f, 9f, 10f, 3.1f, 6.7f, 10f, false, false),
                new PickPlaceXYZGrabReleaseOperation(8.3f, 5.5f, 0.2f, 3.1f, 5.3f, 5f, false, true)
            };
        }

        static PickPlaceXYZGrabReleaseOperation[] BuildThreeBoxMisalignedOperations()
        {
            return new[]
            {
                new PickPlaceXYZGrabReleaseOperation(3.1f, 4.1f, 5f, 8.3f, 5.5f, 0.2f, true, false),
                new PickPlaceXYZGrabReleaseOperation(4f, 3.8f, 10f, 3.1f, 3.8f, 10f, false, false),
                new PickPlaceXYZGrabReleaseOperation(3.7f, 6.7f, 10f, 3.1f, 6.7f, 10f, false, false),
                new PickPlaceXYZGrabReleaseOperation(8.3f, 5.5f, 0.2f, 3.1f, 5.3f, 5f, false, true)
            };
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
}
