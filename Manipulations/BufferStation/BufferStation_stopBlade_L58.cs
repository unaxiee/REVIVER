//-----------------------------------------------------------------------------
// FACTORY I/O (SDK)
//
// Copyright (C) Real Games. All rights reserved.
//-----------------------------------------------------------------------------

using EngineIO;

namespace Controllers
{
    public class BufferStation_stopBlade_L58 : Controller
    {
        MemoryFloat bufferConveyor = MemoryMap.Instance.GetFloat("Buffer conveyor", MemoryType.Output);
        MemoryFloat exitConveyor = MemoryMap.Instance.GetFloat("Exit conveyor", MemoryType.Output);
        MemoryBit stopBlade = MemoryMap.Instance.GetBit("Stop blade", MemoryType.Output);

        MemoryBit atBufferExit = MemoryMap.Instance.GetBit("At buffer exit", MemoryType.Input);
        MemoryBit atExit = MemoryMap.Instance.GetBit("At exit", MemoryType.Input);
        MemoryFloat bufferVel = MemoryMap.Instance.GetFloat("Buffer Vel.", MemoryType.Input);

        FTRIG ftAtBufferExit = new FTRIG();

        State bufferState = State.State0;

        int totalExit = 0;

        private bool stopScene = false;

        public BufferStation_stopBlade_L58()
        {
            bufferConveyor.Value = 0;
            exitConveyor.Value = 10;
            stopBlade.Value = false;
        }

        public override void Execute(int elapsedMilliseconds)
        {
            ftAtBufferExit.CLK(!atBufferExit.Value);

            if (bufferState == 0)
            {
                //bufferConveyor.Value = bufferVel.Value;
                bufferConveyor.Value = 7;
                exitConveyor.Value = 0;
                stopBlade.Value = true;

                if (!atBufferExit.Value)
                {
                    stopBlade.Value = false;
                    exitConveyor.Value = 10;
                    bufferState = State.State1;
                }
            }
            else if (bufferState == State.State1)
            {
                if (ftAtBufferExit.Q)
                {
                    stopBlade.Value = !(true);
                    bufferState = State.State2;
                    totalExit++;
                }
            }
            else if (bufferState == State.State2)
            {
                //bufferConveyor.Value = atBufferExit.Value ? bufferVel.Value : 0;
                // bufferConveyor.Value = atBufferExit.Value ? 7 : 0;
                if (atBufferExit.Value)
                {
                    bufferConveyor.Value = 7;
                }
                else
                {
                    bufferConveyor.Value = 0;
                }

                if (!atExit.Value)
                    bufferState = State.State0;
            }

            if (totalExit == 5)
            {
                stopScene = true;
            }
        }

        public override bool stopSignal => stopScene;
    }
}
