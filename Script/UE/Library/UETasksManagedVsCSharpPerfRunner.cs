using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Script.Library;

public static class UETasksManagedVsCSharpPerfRunner
{
    public static void RunManagedAddOneAndSumCompare(
        int length = 100_000,
        int taskCount = 8,
        int iterations = 32,
        int warmup = 3,
        int rounds = 5)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (taskCount <= 0) throw new ArgumentOutOfRangeException(nameof(taskCount));
        if (iterations <= 0) throw new ArgumentOutOfRangeException(nameof(iterations));
        if (warmup < 0) throw new ArgumentOutOfRangeException(nameof(warmup));
        if (rounds <= 0) throw new ArgumentOutOfRangeException(nameof(rounds));

        var safeTaskCount = Math.Clamp(taskCount, 1, length);
        var ueTasksMs = new double[rounds];
        var parallelForMs = new double[rounds];
        var taskRunMs = new double[rounds];

        for (var r = 0; r < rounds; r++)
        {
            var orderIndex = r % 3;
            long sumUeTasks = 0;
            long sumParallelFor = 0;
            long sumTaskRun = 0;
            double ueMs;
            double pfMs;
            double trMs;
            string order;

            if (orderIndex == 0)
            {
                order = "UE->PF->TR";
                ForceGc();
                ueMs = MeasureUeTasks(length, safeTaskCount, iterations, warmup, out sumUeTasks);
                ForceGc();
                pfMs = MeasureParallelFor(length, safeTaskCount, iterations, warmup, out sumParallelFor);
                ForceGc();
                trMs = MeasureTaskRun(length, safeTaskCount, iterations, warmup, out sumTaskRun);
            }
            else if (orderIndex == 1)
            {
                order = "PF->TR->UE";
                ForceGc();
                pfMs = MeasureParallelFor(length, safeTaskCount, iterations, warmup, out sumParallelFor);
                ForceGc();
                trMs = MeasureTaskRun(length, safeTaskCount, iterations, warmup, out sumTaskRun);
                ForceGc();
                ueMs = MeasureUeTasks(length, safeTaskCount, iterations, warmup, out sumUeTasks);
            }
            else
            {
                order = "TR->UE->PF";
                ForceGc();
                trMs = MeasureTaskRun(length, safeTaskCount, iterations, warmup, out sumTaskRun);
                ForceGc();
                ueMs = MeasureUeTasks(length, safeTaskCount, iterations, warmup, out sumUeTasks);
                ForceGc();
                pfMs = MeasureParallelFor(length, safeTaskCount, iterations, warmup, out sumParallelFor);
            }

            ueTasksMs[r] = ueMs;
            parallelForMs[r] = pfMs;
            taskRunMs[r] = trMs;

            var sumOk = sumUeTasks == sumParallelFor && sumUeTasks == sumTaskRun;
            Console.WriteLine(
                $"[UETasksPerfRound] round={r + 1}/{rounds} order={order} " +
                $"ue={ueMs:F3}ms pf={pfMs:F3}ms tr={trMs:F3}ms sumOk={sumOk}");
        }

        var ueMedian = Median(ueTasksMs);
        var parallelForMedian = Median(parallelForMs);
        var taskRunMedian = Median(taskRunMs);
        var ratioParallelFor = parallelForMedian / Math.Max(0.000001, ueMedian);
        var ratioTaskRun = taskRunMedian / Math.Max(0.000001, ueMedian);

        Console.WriteLine(
            $"[UETasksPerfSummary] length={length} taskCount={safeTaskCount} iterations={iterations} warmup={warmup} rounds={rounds} " +
            $"ueMedian={ueMedian:F3}ms pfMedian={parallelForMedian:F3}ms trMedian={taskRunMedian:F3}ms " +
            $"ratioPf={ratioParallelFor:F3} ratioTr={ratioTaskRun:F3}");
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static double MeasureUeTasks(
        int length,
        int taskCount,
        int iterations,
        int warmup,
        out long sum)
    {
        var data = BuildData(length);
        sum = 0;

        for (var i = 0; i < warmup; i++)
        {
            sum = RunUeTasksOnce(data, taskCount);
        }

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            sum = RunUeTasksOnce(data, taskCount);
        }
        sw.Stop();

        return sw.Elapsed.TotalMilliseconds;
    }

    private static double MeasureParallelFor(
        int length,
        int taskCount,
        int iterations,
        int warmup,
        out long sum)
    {
        var data = BuildData(length);
        sum = 0;
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = taskCount
        };

        for (var i = 0; i < warmup; i++)
        {
            sum = RunParallelForOnce(data, taskCount, options);
        }

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            sum = RunParallelForOnce(data, taskCount, options);
        }
        sw.Stop();

        return sw.Elapsed.TotalMilliseconds;
    }

    private static double MeasureTaskRun(
        int length,
        int taskCount,
        int iterations,
        int warmup,
        out long sum)
    {
        var data = BuildData(length);
        sum = 0;

        for (var i = 0; i < warmup; i++)
        {
            sum = RunTaskRunOnce(data, taskCount);
        }

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            sum = RunTaskRunOnce(data, taskCount);
        }
        sw.Stop();

        return sw.Elapsed.TotalMilliseconds;
    }

    private static long RunUeTasksOnce(int[] data, int taskCount)
    {
        var len = data.Length;
        var chunkSize = (len + taskCount - 1) / taskCount;
        long sum = 0;

        UETasksBatch.ExecuteBatch(index =>
        {
            var start = index * chunkSize;
            var end = Math.Min(start + chunkSize, len);
            long local = 0;

            for (var i = start; i < end; i++)
            {
                data[i] += 1;
                local += data[i];
            }

            Interlocked.Add(ref sum, local);
        }, taskCount);

        return sum;
    }

    private static long RunParallelForOnce(int[] data, int taskCount, ParallelOptions options)
    {
        var len = data.Length;
        var chunkSize = (len + taskCount - 1) / taskCount;
        long sum = 0;

        Parallel.For(0, taskCount, options,
            () => 0L,
            (taskIndex, _, local) =>
            {
                var start = taskIndex * chunkSize;
                var end = Math.Min(start + chunkSize, len);

                for (var i = start; i < end; i++)
                {
                    data[i] += 1;
                    local += data[i];
                }

                return local;
            },
            local => Interlocked.Add(ref sum, local));

        return sum;
    }

    private static long RunTaskRunOnce(int[] data, int taskCount)
    {
        var len = data.Length;
        var chunkSize = (len + taskCount - 1) / taskCount;
        var tasks = new Task<long>[taskCount];

        for (var taskIndex = 0; taskIndex < taskCount; taskIndex++)
        {
            var start = taskIndex * chunkSize;
            var end = Math.Min(start + chunkSize, len);
            tasks[taskIndex] = Task.Run(() =>
            {
                long local = 0;
                for (var i = start; i < end; i++)
                {
                    data[i] += 1;
                    local += data[i];
                }
                return local;
            });
        }

        Task.WaitAll(tasks);

        long sum = 0;
        for (var i = 0; i < tasks.Length; i++)
        {
            sum += tasks[i].Result;
        }

        return sum;
    }

    private static int[] BuildData(int length)
    {
        var data = new int[length];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = i;
        }
        return data;
    }

    private static double Median(double[] values)
    {
        var copy = new double[values.Length];
        Array.Copy(values, copy, values.Length);
        Array.Sort(copy);

        var mid = copy.Length / 2;
        if (copy.Length % 2 == 1)
        {
            return copy[mid];
        }

        return (copy[mid - 1] + copy[mid]) * 0.5;
    }
}
