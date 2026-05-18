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
        public bool OverrideCounter { get; set; }
        public int RecoveryCounter { get; set; }
        public int ExitBox { get; set; }
        public int StopExitBox { get; set; } = 2;
        public bool StateIdentificationSatisfied { get; set; }
        public RecoveryModule RecoveryModule { get; set; }
        public bool OverrideSpZ { get; set; }
        public float RecoverySpZ { get; set; }
        public bool OverridePartConveyorBackward { get; set; }
        public bool RecoveryPartConveyorBackward { get; set; }
        public int SafeGrabCompletionThreshold { get; set; } = 6;
        public string Reason { get; set; }
    }

    public enum RecoveryModule
    {
        BenignResume,
        Overflow,
        MisalignmentBeltConveyor,
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
            recoveryDecision.OverrideCounter = true;
            recoveryDecision.RecoveryCounter = 0;
            recoveryDecision.Reason =
                $"{recoveryDecision.Reason} {reason} Misalignment belt conveyor counter override: counter = 0.";

            return new PickPlaceXYZRecoveryModuleDecision
            {
                RecoveryDecision = recoveryDecision,
                Reason = reason
            };
        }

        static PickPlaceXYZRecoveryDecision CopyDecision(PickPlaceXYZRecoveryDecision decision)
        {
            return new PickPlaceXYZRecoveryDecision
            {
                PickingState = decision.PickingState,
                GrabState = decision.GrabState,
                Counter = decision.Counter,
                OverrideCounter = decision.OverrideCounter,
                RecoveryCounter = decision.RecoveryCounter,
                ExitBox = decision.ExitBox,
                StopExitBox = decision.StopExitBox,
                StateIdentificationSatisfied = decision.StateIdentificationSatisfied,
                RecoveryModule = decision.RecoveryModule,
                OverrideSpZ = decision.OverrideSpZ,
                RecoverySpZ = decision.RecoverySpZ,
                OverridePartConveyorBackward = decision.OverridePartConveyorBackward,
                RecoveryPartConveyorBackward = decision.RecoveryPartConveyorBackward,
                SafeGrabCompletionThreshold = decision.SafeGrabCompletionThreshold,
                Reason = decision.Reason
            };
        }
    }
}
