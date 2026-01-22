using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Script.Library;

public static class UETasksSlicePerfRunner
{
    public static void RunNativeBufferAddOneAndSumCompareByHandler(
        int length = 500_000,
        int taskCount = 16,
        int iterations = 64,
        int warmup = 5,
        int rounds = 5)
    {
        ValidateArgs(length, taskCount, iterations, warmup, rounds);

        var safeTaskCount = Math.Clamp(taskCount, 1, length);
        var ueTasksMs = new double[rounds];
        var parallelForMs = new double[rounds];
        var taskRunMs = new double[rounds];

        for (var r = 0; r < rounds; r++)
        {
            long sumUeTasks = 0;
            long sumParallelFor = 0;
            long sumTaskRun = 0;
            double ueMs;
            double pfMs;
            double trMs;
            const string order = "UE(Handler)->PF->TR";

            ForceGc();
            ueMs = MeasureUeTasksNativeByHandler(length, safeTaskCount, iterations, warmup, out sumUeTasks);
            ForceGc();
            pfMs = MeasureParallelForNative(length, safeTaskCount, iterations, warmup, out sumParallelFor);
            ForceGc();
            trMs = MeasureTaskRunNative(length, safeTaskCount, iterations, warmup, out sumTaskRun);

            ueTasksMs[r] = ueMs;
            parallelForMs[r] = pfMs;
            taskRunMs[r] = trMs;

            var sumOk = sumUeTasks == sumParallelFor && sumUeTasks == sumTaskRun;
            Console.WriteLine(
                $"[UETasksSliceNativeByHandlerRound] round={r + 1}/{rounds} order={order} " +
                $"ue={ueMs:F3}ms pf={pfMs:F3}ms tr={trMs:F3}ms sumOk={sumOk}");
        }

        PrintAverageSummary(
            tag: "UETasksSliceNativeByHandlerSummary",
            length: length,
            taskCount: safeTaskCount,
            iterations: iterations,
            warmup: warmup,
            rounds: rounds,
            ueTasksMs: ueTasksMs,
            parallelForMs: parallelForMs,
            taskRunMs: taskRunMs);
    }

    public static void RunManagedPinnedAddOneAndSumCompare(
        int length = 500_000,
        int taskCount = 16,
        int iterations = 64,
        int warmup = 5,
        int rounds = 5)
    {
        ValidateArgs(length, taskCount, iterations, warmup, rounds);

        var safeTaskCount = Math.Clamp(taskCount, 1, length);
        var ueTasksMs = new double[rounds];
        var parallelForMs = new double[rounds];
        var taskRunMs = new double[rounds];

        for (var r = 0; r < rounds; r++)
        {
            long sumUeTasks = 0;
            long sumParallelFor = 0;
            long sumTaskRun = 0;
            double ueMs;
            double pfMs;
            double trMs;
            const string order = "UE->PF->TR";

            ForceGc();
            ueMs = MeasureUeTasksManaged(length, safeTaskCount, iterations, warmup, out sumUeTasks);
            ForceGc();
            pfMs = MeasureParallelForManaged(length, safeTaskCount, iterations, warmup, out sumParallelFor);
            ForceGc();
            trMs = MeasureTaskRunManaged(length, safeTaskCount, iterations, warmup, out sumTaskRun);

            ueTasksMs[r] = ueMs;
            parallelForMs[r] = pfMs;
            taskRunMs[r] = trMs;

            var sumOk = sumUeTasks == sumParallelFor && sumUeTasks == sumTaskRun;
            Console.WriteLine(
                $"[UETasksSliceManagedRound] round={r + 1}/{rounds} order={order} " +
                $"ue={ueMs:F3}ms pf={pfMs:F3}ms tr={trMs:F3}ms sumOk={sumOk}");
        }

        PrintAverageSummary(
            tag: "UETasksSliceManagedSummary",
            length: length,
            taskCount: safeTaskCount,
            iterations: iterations,
            warmup: warmup,
            rounds: rounds,
            ueTasksMs: ueTasksMs,
            parallelForMs: parallelForMs,
            taskRunMs: taskRunMs);
    }

    public static void RunNativeBufferAddOneAndSumCompare(
        int length = 500_000,
        int taskCount = 16,
        int iterations = 64,
        int warmup = 5,
        int rounds = 5)
    {
        ValidateArgs(length, taskCount, iterations, warmup, rounds);

        var safeTaskCount = Math.Clamp(taskCount, 1, length);
        var ueTasksMs = new double[rounds];
        var parallelForMs = new double[rounds];
        var taskRunMs = new double[rounds];

        for (var r = 0; r < rounds; r++)
        {
            long sumUeTasks = 0;
            long sumParallelFor = 0;
            long sumTaskRun = 0;
            double ueMs;
            double pfMs;
            double trMs;
            const string order = "UE->PF->TR";

            ForceGc();
            ueMs = MeasureUeTasksNative(length, safeTaskCount, iterations, warmup, out sumUeTasks);
            ForceGc();
            pfMs = MeasureParallelForNative(length, safeTaskCount, iterations, warmup, out sumParallelFor);
            ForceGc();
            trMs = MeasureTaskRunNative(length, safeTaskCount, iterations, warmup, out sumTaskRun);

            ueTasksMs[r] = ueMs;
            parallelForMs[r] = pfMs;
            taskRunMs[r] = trMs;

            var sumOk = sumUeTasks == sumParallelFor && sumUeTasks == sumTaskRun;
            Console.WriteLine(
                $"[UETasksSliceNativeRound] round={r + 1}/{rounds} order={order} " +
                $"ue={ueMs:F3}ms pf={pfMs:F3}ms tr={trMs:F3}ms sumOk={sumOk}");
        }

        PrintAverageSummary(
            tag: "UETasksSliceNativeSummary",
            length: length,
            taskCount: safeTaskCount,
            iterations: iterations,
            warmup: warmup,
            rounds: rounds,
            ueTasksMs: ueTasksMs,
            parallelForMs: parallelForMs,
            taskRunMs: taskRunMs);
    }

    private static void ValidateArgs(int length, int taskCount, int iterations, int warmup, int rounds)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (taskCount <= 0) throw new ArgumentOutOfRangeException(nameof(taskCount));
        if (iterations <= 0) throw new ArgumentOutOfRangeException(nameof(iterations));
        if (warmup < 0) throw new ArgumentOutOfRangeException(nameof(warmup));
        if (rounds <= 0) throw new ArgumentOutOfRangeException(nameof(rounds));
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static double MeasureUeTasksManaged(
        int length,
        int taskCount,
        int iterations,
        int warmup,
        out long sum)
    {
        var data = BuildManagedData(length);
        sum = 0;

        for (var i = 0; i < warmup; i++)
        {
            sum = RunUeTasksSliceManaged(data, taskCount);
        }

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            sum = RunUeTasksSliceManaged(data, taskCount);
        }
        sw.Stop();

        return sw.Elapsed.TotalMilliseconds;
    }

    private static double MeasureUeTasksNativeByHandler(
        int length,
        int taskCount,
        int iterations,
        int warmup,
        out long sum)
    {
        using var data = BuildNativeBuffer(length);
        sum = 0;

        for (var i = 0; i < warmup; i++)
        {
            sum = RunUeTasksSliceNativeByHandler(data, taskCount);
        }

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            sum = RunUeTasksSliceNativeByHandler(data, taskCount);
        }
        sw.Stop();

        return sw.Elapsed.TotalMilliseconds;
    }

    private static double MeasureParallelForManaged(
        int length,
        int taskCount,
        int iterations,
        int warmup,
        out long sum)
    {
        var data = BuildManagedData(length);
        sum = 0;
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = taskCount
        };

        for (var i = 0; i < warmup; i++)
        {
            sum = RunParallelForOnceManaged(data, taskCount, options);
        }

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            sum = RunParallelForOnceManaged(data, taskCount, options);
        }
        sw.Stop();

        return sw.Elapsed.TotalMilliseconds;
    }

    private static double MeasureTaskRunManaged(
        int length,
        int taskCount,
        int iterations,
        int warmup,
        out long sum)
    {
        var data = BuildManagedData(length);
        sum = 0;

        for (var i = 0; i < warmup; i++)
        {
            sum = RunTaskRunOnceManaged(data, taskCount);
        }

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            sum = RunTaskRunOnceManaged(data, taskCount);
        }
        sw.Stop();

        return sw.Elapsed.TotalMilliseconds;
    }

    private static long RunParallelForOnceManaged(int[] data, int taskCount, ParallelOptions options)
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

    private static long RunTaskRunOnceManaged(int[] data, int taskCount)
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

    private static double MeasureUeTasksNative(
        int length,
        int taskCount,
        int iterations,
        int warmup,
        out long sum)
    {
        using var data = BuildNativeBuffer(length);
        sum = 0;

        for (var i = 0; i < warmup; i++)
        {
            sum = RunUeTasksSliceNative(data, taskCount);
        }

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            sum = RunUeTasksSliceNative(data, taskCount);
        }
        sw.Stop();

        return sw.Elapsed.TotalMilliseconds;
    }

    private static double MeasureParallelForNative(
        int length,
        int taskCount,
        int iterations,
        int warmup,
        out long sum)
    {
        using var data = BuildNativeBuffer(length);
        sum = 0;
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = taskCount
        };

        for (var i = 0; i < warmup; i++)
        {
            sum = RunParallelForOnceNative(data, taskCount, options);
        }

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            sum = RunParallelForOnceNative(data, taskCount, options);
        }
        sw.Stop();

        return sw.Elapsed.TotalMilliseconds;
    }

    private static double MeasureTaskRunNative(
        int length,
        int taskCount,
        int iterations,
        int warmup,
        out long sum)
    {
        using var data = BuildNativeBuffer(length);
        sum = 0;

        for (var i = 0; i < warmup; i++)
        {
            sum = RunTaskRunOnceNative(data, taskCount);
        }

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            sum = RunTaskRunOnceNative(data, taskCount);
        }
        sw.Stop();

        return sw.Elapsed.TotalMilliseconds;
    }

    private static unsafe long RunParallelForOnceNative(NativeBuffer<int> data, int taskCount, ParallelOptions options)
    {
        var len = data.Length;
        var chunkSize = (len + taskCount - 1) / taskCount;
        var ptr = (int*)data.Ptr;
        long sum = 0;

        Parallel.For(0, taskCount, options,
            () => 0L,
            (taskIndex, _, local) =>
            {
                var start = taskIndex * chunkSize;
                var end = Math.Min(start + chunkSize, len);

                for (var i = start; i < end; i++)
                {
                    ptr[i] += 1;
                    local += ptr[i];
                }

                return local;
            },
            local => Interlocked.Add(ref sum, local));

        return sum;
    }

    private static long RunUeTasksSliceManaged(int[] data, int taskCount)
    {
        var len = data.Length;
        var chunkSize = (len + taskCount - 1) / taskCount;
        long sum = 0;

        UETasksSliceBatch.RunManaged(data, taskCount, (arr, start, count) =>
        {
            var end = start + count;
            long local = 0;

            for (var i = start; i < end; i++)
            {
                arr[i] += 1;
                local += arr[i];
            }

            Interlocked.Add(ref sum, local);
        });

        return sum;
    }

    private static unsafe long RunUeTasksSliceNative(NativeBuffer<int> data, int taskCount)
    {
        var len = data.Length;
        var chunkSize = (len + taskCount - 1) / taskCount;
        long sum = 0;

        UETasksSliceBatch.RunNative(data, taskCount, (ptr, start, count) =>
        {
            var raw = (int*)ptr;
            var end = start + count;
            long local = 0;

            for (var i = start; i < end; i++)
            {
                raw[i] += 1;
                local += raw[i];
            }

            Interlocked.Add(ref sum, local);
        });

        return sum;
    }

    private static long RunUeTasksSliceNativeByHandler(NativeBuffer<int> data, int taskCount)
    {
        // 走“C# handler 作为参数传给 C++”的新入口（方案B）。
        return UETasksSliceBatch.RunNativeAddOneAndSumByHandler(data, taskCount);
    }

    private static unsafe long RunTaskRunOnceNative(NativeBuffer<int> data, int taskCount)
    {
        var len = data.Length;
        var chunkSize = (len + taskCount - 1) / taskCount;
        var ptr = (int*)data.Ptr;
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
                    ptr[i] += 1;
                    local += ptr[i];
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

    private static int[] BuildManagedData(int length)
    {
        var data = new int[length];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = i;
        }
        return data;
    }

    private static NativeBuffer<int> BuildNativeBuffer(int length)
    {
        var data = new NativeBuffer<int>(length: length);
        var span = data.AsSpan();
        for (var i = 0; i < span.Length; i++)
        {
            span[i] = i;
        }
        return data;
    }

    private static void PrintAverageSummary(
        string tag,
        int length,
        int taskCount,
        int iterations,
        int warmup,
        int rounds,
        double[] ueTasksMs,
        double[] parallelForMs,
        double[] taskRunMs)
    {
        var ueAvg = Average(ueTasksMs);
        var parallelForAvg = Average(parallelForMs);
        var taskRunAvg = Average(taskRunMs);
        var ratioParallelFor = parallelForAvg / Math.Max(0.000001, ueAvg);
        var ratioTaskRun = taskRunAvg / Math.Max(0.000001, ueAvg);

        Console.WriteLine(
            $"[{tag}] length={length} taskCount={taskCount} iterations={iterations} warmup={warmup} rounds={rounds} " +
            $"ueAvg={ueAvg:F3}ms pfAvg={parallelForAvg:F3}ms trAvg={taskRunAvg:F3}ms " +
            $"ratioPf={ratioParallelFor:F3} ratioTr={ratioTaskRun:F3}");
    }

    private static double Average(double[] values)
    {
        var sum = 0.0;
        for (var i = 0; i < values.Length; i++)
        {
            sum += values[i];
        }

        return sum / Math.Max(1, values.Length);
    }
}
