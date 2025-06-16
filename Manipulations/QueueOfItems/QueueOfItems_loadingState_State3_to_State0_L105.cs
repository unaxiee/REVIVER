//-----------------------------------------------------------------------------
// FACTORY I/O (SDK)
//
// Copyright (C) Real Games. All rights reserved.
//-----------------------------------------------------------------------------

using EngineIO;

namespace Controllers
{
    public class QueueOfItems_loadingState_State3_to_State0_L105 : Controller
    {
        MemoryBit entryConveyor = MemoryMap.Instance.GetBit("Entry conveyor", MemoryType.Output);
        MemoryBit bufferConveyor = MemoryMap.Instance.GetBit("Buffer conveyor", MemoryType.Output);

        MemoryBit emitter = MemoryMap.Instance.GetBit("Emitter", MemoryType.Output);

        MemoryBit itemReady = MemoryMap.Instance.GetBit("Item ready", MemoryType.Input);
        MemoryBit atEntry = MemoryMap.Instance.GetBit("At entry", MemoryType.Input);
        MemoryBit atExit = MemoryMap.Instance.GetBit("At exit", MemoryType.Input);

        State watchState = State.State0;
        State loadingState = State.State0;
        State unloadingState = State.State0;

        bool loading = false;
        bool unloading = false;

        int counter = 0;

        int totalExit = 0;

        RTRIG rtAtEntry = new RTRIG();

        FTRIG ftAtEntry = new FTRIG();
        FTRIG ftAtExit = new FTRIG();

        private bool stopScene = false;

        public QueueOfItems_loadingState_State3_to_State0_L105()
        {
            entryConveyor.Value = false;
            bufferConveyor.Value = false;
            emitter.Value = false;
        }

        public override void Execute(int elapsedMilliseconds)
        {
            rtAtEntry.CLK(!atEntry.Value);
            ftAtEntry.CLK(!atEntry.Value);
            ftAtExit.CLK(!atExit.Value);

            if (executionCount % 300 == 0 | executionCount == 0)
            {
                emitter.Value = true;
            }
            if (!itemReady.Value)
            {
                emitter.Value = false;
            }

            //Master controller
            if (watchState == State.State0)
            {
                loading = true;
                unloading = false;

                if (counter == 3)
                    watchState = State.State1;
            }
            else if (watchState == State.State1)
            {
                loading = false;
                unloading = true;

                if (counter == 0)
                    watchState = State.State0;
            }

            //Loading controller
            if (loading)
            {
                if (loadingState == State.State0)
                {
                    entryConveyor.Value = true;
                    bufferConveyor.Value = false;

                    if (!itemReady.Value || !atEntry.Value)
                        loadingState = State.State1;
                }
                else if (loadingState == State.State1)
                {
                    entryConveyor.Value = true;
                    bufferConveyor.Value = false;

                    if (rtAtEntry.Q)
                        loadingState = State.State2;
                }
                else if (loadingState == State.State2)
                {
                    entryConveyor.Value = true;
                    bufferConveyor.Value = true;

                    if (ftAtEntry.Q)
                        loadingState = State.State0;
                }
                else if (loadingState == State.State3)
                {
                    counter++;

                    loadingState = State.State0;
                }
            }

            //Unloading controller
            if (unloading)
            {
                if (unloadingState == State.State0)
                {
                    entryConveyor.Value = false;
                    bufferConveyor.Value = true;

                    if (ftAtExit.Q)
                        unloadingState = State.State1;
                }
                else if (unloadingState == State.State1)
                {
                    counter--;
                    unloadingState = State.State0;
                }
            }

            if (ftAtExit.Q) {
                totalExit++;
                if (totalExit == 6)
                    stopScene = true;
            }

        }
        public override bool stopSignal => stopScene;
    }
}
