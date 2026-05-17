//-----------------------------------------------------------------------------
// FACTORY I/O (SDK)
//
// State reconstruction logic for PickPlaceXYZ recovery.
//-----------------------------------------------------------------------------

using System;
using System.Reflection;

using EngineIO;

namespace Controllers
{
    public sealed class PickPlaceXYZSnapshot
    {
        public bool PartConveyorForward { get; private set; }
        public bool PartConveyorBackward { get; private set; }
        public bool BoxConveyorForward { get; private set; }
        public bool BoxConveyorBackward { get; private set; }
        public bool ExitConveyor { get; private set; }
        public bool Grab { get; private set; }
        public bool C { get; private set; }
        public float SpX { get; private set; }
        public float SpY { get; private set; }
        public float SpZ { get; private set; }
        public bool PartAtPlace { get; private set; }
        public bool BoxAtPlace { get; private set; }
        public bool Detected { get; private set; }
        public float PosX { get; private set; }
        public float PosY { get; private set; }
        public float PosZ { get; private set; }
        public int Counter { get; private set; }
        public int ExitBox { get; private set; }

        public static PickPlaceXYZSnapshot Read()
        {
            return Read(null);
        }

        public static PickPlaceXYZSnapshot Read(Controller pausedController)
        {
            MemoryMap.Instance.Update();

            return new PickPlaceXYZSnapshot
            {
                PartConveyorForward = ReadBit("Belt Conveyor (4m) 1 (+)", MemoryType.Output),
                PartConveyorBackward = ReadBit("Belt Conveyor (4m) 1 (-)", MemoryType.Output),
                BoxConveyorForward = ReadBit("Roller Conveyor (6m) 1 (+)", MemoryType.Output),
                BoxConveyorBackward = ReadBit("Roller Conveyor (6m) 1 (-)", MemoryType.Output),
                ExitConveyor = ReadBit("Exit conveyor", MemoryType.Output),
                Grab = ReadBit("Grab", MemoryType.Output),
                C = ReadBit("C +", MemoryType.Output),
                SpX = ReadFloat("SP X", MemoryType.Output),
                SpY = ReadFloat("SP Y", MemoryType.Output),
                SpZ = ReadFloat("SP Z", MemoryType.Output),
                PartAtPlace = ReadBit("Part at place", MemoryType.Input),
                BoxAtPlace = ReadBit("Box at place", MemoryType.Input),
                Detected = ReadBit("Detected", MemoryType.Input),
                PosX = ReadFloat("X", MemoryType.Input),
                PosY = ReadFloat("Y", MemoryType.Input),
                PosZ = ReadFloat("Z", MemoryType.Input),
                Counter = ReadControllerInt(pausedController, "counter"),
                ExitBox = ReadControllerInt(pausedController, "exitBox")
            };
        }

        static bool ReadBit(string name, MemoryType type)
        {
            return MemoryMap.Instance.GetBit(name, type).Value;
        }

        static float ReadFloat(string name, MemoryType type)
        {
            return MemoryMap.Instance.GetFloat(name, type).Value;
        }

        static int ReadControllerInt(Controller controller, string fieldName)
        {
            if (controller == null)
                return 0;

            FieldInfo field = controller.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(int))
                return 0;

            return (int)field.GetValue(controller);
        }
    }

    public sealed class PickPlaceXYZRecoveryDecision
    {
        public State PickingState { get; set; }
        public State GrabState { get; set; }
        public int Counter { get; set; }
        public int ExitBox { get; set; }
        public bool OverrideSpZ { get; set; }
        public float RecoverySpZ { get; set; }
        public string Reason { get; set; }
    }

    public static class PickPlaceXYZRecoveryStateDecider
    {
        const float SetpointDelta = 0.05f;
        const float PositionDelta = 0.15f;
        const float ZDelta = 0.15f;

        public static PickPlaceXYZRecoveryDecision Decide(PickPlaceXYZSnapshot s)
        {
            if (
                !s.Grab
                && (IsAtHomeXY(s) || IsAtOnePlaceXY(s))
                && IsAtHomeZ(s)
            )
                return Decision(s, State.State0, State.State0,
                    "State (0, 0): not holding, XY is at pickup or one of the three place locations, and Z is 0.");
            
            if (
                !s.Grab
                && IsMovingHorizontallyXY(s)
                && IsAtHomeZ(s)
            )
                return Decision(s, State.State1, State.State0,
                    "State (1, 0): not holding, moving horizontally, and Z is 0.");

            if (
                !s.Grab
                && IsAtPickupXY(s)
                && IsMovingVerticallyZ(s)
            )
                return Decision(s, State.State2, State.State1,
                    "State (2, 1): not holding, XY is at pickup, and Z is moving vertically for pickup.");

            if (
                s.Grab
                && IsAtPickupXY(s)
                && IsMovingVerticallyZ(s)
            )
                return Decision(s, State.State2, State.State3,
                    "State (2, 3): holding, XY is at pickup, Z is moving vertically, and retract command is active.");

            if (
                s.Grab
                && IsMovingHorizontallyXY(s)
                && IsAtHomeZ(s)
            )
                return Decision(s, State.State3, State.State0,
                    "State (3, 0): holding, moving horizontally toward placement, and Z is 0.");

            if (
                s.Grab
                && IsAtOnePlaceXY(s)
                && IsMovingVerticallyZ(s)
            )
                return Decision(s, State.State4, State.State0,
                    "State (4, 0): holding, XY is at a place location, and Z is moving vertically for placement.");

            if (
                !s.Grab
                && IsAtOnePlaceXY(s)
                && IsMovingVerticallyZ(s)
            )
                return Decision(s, State.State5, State.State0,
                    "State (5, 0): not holding, XY is at a place location, and Z is moving vertically after release.");

            if (!s.Grab)
                return DecisionWithSpZ(s, State.State0, State.State0, 0f,
                    "Fallback: grab is false, so recover to State (0, 0) and command SP Z to 0.");

            return Decision(s, State.State3, State.State0,
                "Fallback: grab is true, so recover to State (3, 0) and continue carrying toward placement.");
        }

        static PickPlaceXYZRecoveryDecision Decision(
            PickPlaceXYZSnapshot s,
            State pickingState,
            State grabState,
            string reason)
        {
            return new PickPlaceXYZRecoveryDecision
            {
                PickingState = pickingState,
                GrabState = grabState,
                Counter = s.Counter,
                ExitBox = s.ExitBox,
                Reason = reason
            };
        }

        static PickPlaceXYZRecoveryDecision DecisionWithSpZ(
            PickPlaceXYZSnapshot s,
            State pickingState,
            State grabState,
            float spZ,
            string reason)
        {
            PickPlaceXYZRecoveryDecision decision = Decision(s, pickingState, grabState, reason);
            decision.OverrideSpZ = true;
            decision.RecoverySpZ = spZ;
            return decision;
        }

        static bool IsAtHomeXY(PickPlaceXYZSnapshot s)
        {
            return NearPosition(s.PosX, 0f) && NearPosition(s.PosY, 0f);
        }

        static bool IsAtPickupXY(PickPlaceXYZSnapshot s)
        {
            return NearPosition(s.PosX, 8.3f) && NearPosition(s.PosY, 5.5f);
        }

        static bool IsAtOnePlaceXY(PickPlaceXYZSnapshot s)
        {
            return NearPosition(s.PosX, 3.1f)
                && (NearPosition(s.PosY, 3.8f)
                    || NearPosition(s.PosY, 6.7f)
                    || NearPosition(s.PosY, 5.3f));
        }

        static bool IsAtHomeZ(PickPlaceXYZSnapshot s)
        {
            return NearPosition(s.PosZ, 0f);
        }

        static bool IsMovingHorizontallyXY(PickPlaceXYZSnapshot s)
        {
            return InRange(s.PosX, 0f, 8.3f) && InRange(s.PosY, 0f, 5.5f);
        }

        static bool IsMovingVerticallyZ(PickPlaceXYZSnapshot s)
        {
            return InRange(s.PosZ, 0f, 10f);
        }

        static bool NearPosition(float val1, float val2)
        {
            return Math.Abs(val1 - val2) < PositionDelta;
        }

        static bool InRange(float val, float min, float max)
        {
            return val >= min && val <= max;
        }        
    }
}
