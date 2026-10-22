//-----------------------------------------------------------------------------
// FACTORY I/O (SDK)
//
// Runs the ProductionLine scene controller with optional timing.
//-----------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

using EngineIO;

namespace Controllers
{
    class Program_Benign
    {
        public const int CycleTime = 8;
        public const int MaxExecutions = 4000;
        public static readonly bool EnableTiming = false;

        static readonly string sceneName = "ProductionLine";
        static readonly string logRoot = @"D:\Code\factoryio-sdk-master\factoryio-sdk-master\samples\Controllers\Logs\Benign";

        static void Main(string[] args)
        {
            if (EnableTiming)
                InitializeBenignTimingLog();

            MemoryBit start = MemoryMap.Instance.GetBit(MemoryMap.BitCount - 16, MemoryType.Output);
            MemoryBit running = MemoryMap.Instance.GetBit(MemoryMap.BitCount - 16, MemoryType.Input);

            try
            {
                SwitchToRun(start);

                Controller controller = new ProductionLine();
                Stopwatch cycleStopwatch = EnableTiming ? Stopwatch.StartNew() : null;
                Stopwatch executionStopwatch = EnableTiming ? Stopwatch.StartNew() : null;

                while (!controller.stopSignal && (!EnableTiming || controller.executionCount < MaxExecutions))
                {
                    MemoryMap.Instance.Update();

                    if (running.Value)
                    {
                        int elapsedMilliseconds = CycleTime;

                        if (EnableTiming)
                        {
                            cycleStopwatch.Stop();
                            elapsedMilliseconds = (int)cycleStopwatch.ElapsedMilliseconds;
                            cycleStopwatch.Restart();
                        }

                        controller.Execute(elapsedMilliseconds);
                        controller.executionCount++;
                    }

                    Thread.Sleep(CycleTime);
                }

                if (EnableTiming)
                {
                    executionStopwatch.Stop();
                    AppendBenignTimingLog(sceneName, controller.executionCount, executionStopwatch.Elapsed);
                }

                Shutdown(start);
            }
            finally
            {
                MemoryMap.Instance.Dispose();
            }
        }

        static void SwitchToRun(MemoryBit start)
        {
            start.Value = false;
            MemoryMap.Instance.Update();
            Thread.Sleep(500);

            start.Value = true;
            MemoryMap.Instance.Update();
            Thread.Sleep(500);
        }

        static void Shutdown(MemoryBit start)
        {
            start.Value = false;
            MemoryMap.Instance.Update();
            Thread.Sleep(500);
        }

        static void InitializeBenignTimingLog()
        {
            Directory.CreateDirectory(logRoot);
            string path = Path.Combine(logRoot, "benign_timing.csv");
            EnsureCsvHeader(path, "scene,executions,duration_ms");
        }

        static void EnsureCsvHeader(string path, string header)
        {
            if (File.Exists(path) && new FileInfo(path).Length > 0)
            {
                if (File.ReadLines(path).Any(line => line == header))
                    return;

                File.AppendAllText(path, header + Environment.NewLine);
                return;
            }

            File.WriteAllText(path, header + Environment.NewLine);
        }

        static void AppendBenignTimingLog(string scene, int executions, TimeSpan totalExecutionElapsed)
        {
            string path = Path.Combine(logRoot, "benign_timing.csv");
            string[] values =
            {
                scene,
                executions.ToString(CultureInfo.InvariantCulture),
                totalExecutionElapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture)
            };

            File.AppendAllText(path, string.Join(",", values.Select(EscapeCsv)) + Environment.NewLine);
        }

        static string EscapeCsv(string value)
        {
            if (value.Contains(",") || value.Contains("\"") || value.Contains(Environment.NewLine))
                return $"\"{value.Replace("\"", "\"\"")}\"";

            return value;
        }
    }
}
