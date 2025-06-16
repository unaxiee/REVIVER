//-----------------------------------------------------------------------------
// FACTORY I/O (SDK)
//
// Copyright (C) Real Games. All rights reserved.
//-----------------------------------------------------------------------------

using EngineIO;

namespace Controllers
{
    public class FromAtoBSetReset_conveyor_L26 : Controller
    {
        MemoryBit conveyor = MemoryMap.Instance.GetBit("Conveyor", MemoryType.Output);

        MemoryBit sensorA = MemoryMap.Instance.GetBit("Sensor A", MemoryType.Input);
        MemoryBit sensorB = MemoryMap.Instance.GetBit("Sensor B", MemoryType.Input);

        public FromAtoBSetReset_conveyor_L26()
        {
            conveyor.Value = false;
        }

        public override void Execute(int elapsedMilliseconds)
        {
            if (!sensorA.Value)
                conveyor.Value = !(true);

            if (!sensorB.Value)
                conveyor.Value = false;
        }
    }
}
