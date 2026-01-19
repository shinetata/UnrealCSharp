using System;
using System.Diagnostics;

namespace Script.Library;

public static class TaskGraphNativeKernelPerf
{
    public static void RunAddOneAndSumInt32Parallel(
        int length = 100_000,
        int taskCount = 8,
        int iterations = 32,
        int warmup = 3)
    {
        var totalMs = MeasureAddOneAndSumInt32Parallel(
            length,
            taskCount,
            iterations,
            warmup,
            out var sum);

        Console.WriteLine(
            $"[TaskGraphNativeKernel] length={length} taskCount={Math.Clamp(taskCount, 1, length)} iterations={iterations} warmup={warmup} total={totalMs:F3}ms sum={sum}");
    }

    public static double MeasureAddOneAndSumInt32Parallel(
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
            sum = FNativeBufferTaskGraphImplementation
                .FNativeBufferTaskGraph_AddOneAndSumInt32ParallelNoLogImplementation(
                    buf.Ptr,
                    buf.Length,
                    safeTaskCount);
        }

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            sum = FNativeBufferTaskGraphImplementation
                .FNativeBufferTaskGraph_AddOneAndSumInt32ParallelNoLogImplementation(
                    buf.Ptr,
                    buf.Length,
                    safeTaskCount);
        }
        sw.Stop();

        return sw.Elapsed.TotalMilliseconds;
    }
}
