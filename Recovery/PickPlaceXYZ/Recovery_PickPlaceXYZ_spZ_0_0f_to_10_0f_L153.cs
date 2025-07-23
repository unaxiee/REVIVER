//-----------------------------------------------------------------------------
// FACTORY I/O (SDK)
//
// Copyright (C) Real Games. All rights reserved.
//-----------------------------------------------------------------------------

using System;

using EngineIO;

namespace Controllers
{
    public class Recovery_PickPlaceXYZ_spZ_0_0f_to_10_0f_L153 : Controller
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
        // MemoryBit exitYellow = MemoryMap.Instance.GetBit("Exit yellow", MemoryType.Output);
        // MemoryBit exitGreen = MemoryMap.Instance.GetBit("Exit green", MemoryType.Output);

        MemoryBit partEmitter = MemoryMap.Instance.GetBit("Part emitter", MemoryType.Output);

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

        State recoveryState = State.State0;

        TON grabTimer = new TON();

        int counter;

        int exitBox = 0;

        private bool stopScene = false;

        private int currRecoveryIndex = 0;
        private Func<bool>[] recoverySteps;

        public Recovery_PickPlaceXYZ_spZ_0_0f_to_10_0f_L153()
        {
            partConveyorForward.Value = false;
            partConveyorBackward.Value = false;
            boxConveyorForward.Value = false;
            boxConveyorBackward.Value = false;
            // exitYellow.Value = false;
            // exitGreen.Value = true

            partEmitter.Value = false;

            spX.Value = 8f;
            spY.Value = 5.5f;
            spZ.Value = 0;
            grab.Value = false;

            counter = 1;

            grabTimer.PT = 1000;

            recoverySteps = new Func<bool>[]
            {
                () => recoveryLogicBeltForward(),
                () => recoveryLogicMove(8.3f, 5.5f, 5.3f, false, 3.1f, 6.7f, 10f, false, true),
                () => recoveryLogicBeltForward(),
                () => recoveryLogicMove(8.3f, 5.5f, 5.3f, false, 3.1f, 5.3f, 5f, true, true),
                () => recoveryLogicRollerForward(),
                () => recoveryLogicBeltForward(),
                () => recoveryLogicMove(8.3f, 5.5f, 5.3f, false, 3.1f, 3.8f, 10f, false, true),
            };
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
            // exitYellow.Value = false;
            // exitGreen.Value = true;

            partEmitter.Value = false;

            if (currRecoveryIndex < recoverySteps.Length)
            {
                bool done = recoverySteps[currRecoveryIndex]();
                if (done)
                    currRecoveryIndex++;
                return;
            }

            partEmitter.Value = true;

            #region X & Y Movement

            if (pickingState == State.State0) //Waiting for part and box
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

            #endregion

            #region Grab

            if (grabState == State.State0)
            {
                //Idle state
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

            #endregion

            #region Conveyors

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

            // if (pickingState == State.State0)
            // {
            //     exitYellow.Value = false;
            //     exitGreen.Value = true;
            // }
            // else
            // {
            //     exitYellow.Value = true;
            //     exitGreen.Value = false;
            // }

            if (exitBox == 1) {
                stopScene = true;
            }

            #endregion
        }

        bool Near(float val1, float val2, float delta)
        {
            return Math.Abs(val1 - val2) < delta;
        }

        public override bool stopSignal => stopScene;

        private bool recoveryLogicMove(float curr_spX, float curr_spY, float curr_spZ, bool grab_c, float tar_spX, float tar_spY, float tar_spZ, bool drop_c, bool beltBack)
        {
            if (recoveryState == State.State0)
            {
                c.Value = grab_c;

                spX.Value = curr_spX;
                spY.Value = curr_spY;
                
                if (Near(posX.Value, spX.Value, 0.01f) && Near(posY.Value, spY.Value, 0.01f))
                {
                    recoveryState = State.State1;
                }
            }
            else if (recoveryState == State.State1)
            {
                spZ.Value = curr_spZ;

                if (detected.Value)
                {
                    spZ.Value = posZ.Value;
                    recoveryState = State.State2;
                }
            }
            else if (recoveryState == State.State2)
            {
                grab.Value = true;

                grabTimer.IN = true;

                partConveyorBackward.Value = beltBack;

                if (grabTimer.Q)
                {
                    grabTimer.IN = false;
                    partConveyorBackward.Value = false;
                    recoveryState = State.State3;
                }
            }
            else if (recoveryState == State.State3)
            {
                spZ.Value = 0f;

                if (Near(spZ.Value, posZ.Value, 0.01f))
                {
                    recoveryState = State.State4;
                }
            }
            else if (recoveryState == State.State4)
            {
                c.Value = drop_c;
                
                spX.Value = tar_spX;
                spY.Value = tar_spY;

                if (Near(posX.Value, spX.Value, 0.01f) && Near(posY.Value, spY.Value, 0.01f))
                {
                    recoveryState = State.State5;
                }
            }
            else if (recoveryState == State.State5)
            {
                spZ.Value = tar_spZ;

                if (Near(posZ.Value, spZ.Value, 0.01f))
                {
                    grab.Value = false;
                    recoveryState = State.State6;
                }
            }
            else if (recoveryState == State.State6)
            {
                spZ.Value = 0;

                if (Near(posZ.Value, spZ.Value, 0.01f))
                {
                    recoveryState = State.State0;
                    return true;
                }
            }
            return false;
        }

        private bool recoveryLogicRollerBack()
        {
            boxConveyorBackward.Value = true;
            if (ftBoxAtPlace.Q)
            {
                return true;
            }
            return false;
        }

        private bool recoveryLogicRollerForward()
        {
            boxConveyorForward.Value = true;
            if (rtBoxAtPlace.Q)
            {
                return true;
            }
            return false;
        }

        private bool recoveryLogicBeltForward()
        {
            partConveyorForward.Value = true;
            if (!partAtPlace.Value)
            {
                return true;
            }
            return false;
        }

        
    }
}
