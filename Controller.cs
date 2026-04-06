//-----------------------------------------------------------------------------
// FACTORY I/O (SDK)
//
// Copyright (C) Real Games. All rights reserved.
//-----------------------------------------------------------------------------

namespace Controllers
{
    public abstract class Controller
    {
        public abstract void Execute(int elapsedMilliseconds);

        public int executionCount { get; set; } = 0;

        public bool stopSignal = false;

        public bool captureSignal = false;
    }
}
