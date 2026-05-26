//-----------------------------------------------------------------------------
// FACTORY I/O (SDK)
//
// Test harness for measuring benign PickPlaceXYZ execution time.
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

        static readonly string sceneName = "PickPlaceXYZ";
        static readonly string logRoot = @"D:\Code\factoryio-sdk-master\factoryio-sdk-master\samples\Controllers\Logs\Benign";

        static void Main(string[] args)
        {
            InitializeBenignTimingLog();

            MemoryBit start = MemoryMap.Instance.GetBit(MemoryMap.BitCount - 16, MemoryType.Output);
            MemoryBit running = MemoryMap.Instance.GetBit(MemoryMap.BitCount - 16, MemoryType.Input);

            try
            {
                SwitchToRun(start);

                Controller controller = new PickPlaceXYZ();
                Debug.WriteLine($"Running benign controller: {sceneName}");

                Stopwatch cycleStopwatch = Stopwatch.StartNew();
                Stopwatch executionStopwatch = Stopwatch.StartNew();
                int executionCount = 0;

                while (!controller.stopSignal && executionCount < MaxExecutions)
                {
                    MemoryMap.Instance.Update();

                    if (running.Value)
                    {
                        cycleStopwatch.Stop();
                        controller.executionCount = executionCount;
                        controller.Execute((int)cycleStopwatch.ElapsedMilliseconds);
                        executionCount++;
                        cycleStopwatch.Restart();
                    }

                    Thread.Sleep(CycleTime);
                }

                executionStopwatch.Stop();

                Debug.WriteLine(
                    $"Benign execution complete: executions={executionCount}, " +
                    $"duration_ms={executionStopwatch.Elapsed.TotalMilliseconds:F3}");

                AppendBenignTimingLog(sceneName, executionCount, executionStopwatch.Elapsed);
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
