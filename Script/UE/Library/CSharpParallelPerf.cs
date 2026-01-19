using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Script.Library;

public static class CSharpParallelPerf
{
    public static void RunAddOneAndSumInt32ParallelFor(
        int length = 100_000,
        int taskCount = 8,
        int iterations = 32,
        int warmup = 3)
    {
        var totalMs = MeasureAddOneAndSumInt32ParallelFor(
            length,
            taskCount,
            iterations,
            warmup,
            out var sum);

        Console.WriteLine(
            $"[CSharpParallelFor] length={length} taskCount={Math.Clamp(taskCount, 1, length)} iterations={iterations} warmup={warmup} total={totalMs:F3}ms sum={sum}");
    }

    public static double MeasureAddOneAndSumInt32ParallelFor(
        int length,
        int taskCount,
        int iterations,
        int warmup,
        out long sum)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (taskCount <= 0) throw new ArgumentOutOfRangeException(nameof(taskCount));
        if (iterations <= 0) throw new ArgumentOutOfRangeException(nameof(iterations));
        if (warmup < 0) throw new ArgumentOutOfRangeException(nameof(warmup));

        var safeTaskCount = Math.Clamp(taskCount, 1, length);
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = safeTaskCount
        };

        using var buf = new NativeBuffer<int>(length: length);
        var span = buf.AsSpan();
        for (var i = 0; i < span.Length; i++)
        {
            span[i] = i;
        }

        sum = 0;
        for (var i = 0; i < warmup; i++)
        {
            sum = RunOnceChunked(buf, options, safeTaskCount);
        }

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            sum = RunOnceChunked(buf, options, safeTaskCount);
        }
        sw.Stop();

        return sw.Elapsed.TotalMilliseconds;
    }

    private static unsafe long RunOnceChunked(NativeBuffer<int> buf, ParallelOptions options, int taskCount)
    {
        var len = buf.Length;
        var data = (int*)buf.Ptr;
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

    public static void RunAddOneAndSumInt32TaskRun(
        int length = 100_000,
        int taskCount = 8,
        int iterations = 32,
        int warmup = 3)
    {
        var totalMs = MeasureAddOneAndSumInt32TaskRun(
            length,
            taskCount,
            iterations,
            warmup,
            out var sum);

        Console.WriteLine(
            $"[CSharpTaskRun] length={length} taskCount={Math.Clamp(taskCount, 1, length)} iterations={iterations} warmup={warmup} total={totalMs:F3}ms sum={sum}");
    }

    public static double MeasureAddOneAndSumInt32TaskRun(
        int length,
        int taskCount,
        int iterations,
        int warmup,
        out long sum)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (taskCount <= 0) throw new ArgumentOutOfRangeException(nameof(taskCount));
        if (iterations <= 0) throw new ArgumentOutOfRangeException(nameof(iterations));
        if (warmup < 0) throw new ArgumentOutOfRangeException(nameof(warmup));

        var safeTaskCount = Math.Clamp(taskCount, 1, length);

        using var buf = new NativeBuffer<int>(length: length);
        var span = buf.AsSpan();
        for (var i = 0; i < span.Length; i++)
        {
            span[i] = i;
        }

        sum = 0;
        for (var i = 0; i < warmup; i++)
        {
            sum = RunOnceTaskRunChunked(buf, safeTaskCount);
        }

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            sum = RunOnceTaskRunChunked(buf, safeTaskCount);
        }
        sw.Stop();

        return sw.Elapsed.TotalMilliseconds;
    }

    private static unsafe long RunOnceTaskRunChunked(NativeBuffer<int> buf, int taskCount)
    {
        var len = buf.Length;
        var data = (int*)buf.Ptr;
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
}
