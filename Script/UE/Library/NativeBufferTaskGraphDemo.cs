using System;

namespace Script.Library;

public static class NativeBufferTaskGraphDemo
{
    public static void RunInt32Parallel()
    {
        using var buf = new NativeBuffer<int>(length: 100_000);

        var span = buf.AsSpan();
        for (var i = 0; i < span.Length; i++)
        {
            span[i] = i;
        }

        var taskCount = Math.Min(Environment.ProcessorCount, 16);
        var sum = FNativeBufferTaskGraphImplementation.FNativeBufferTaskGraph_AddOneAndSumInt32ParallelImplementation(
            buf.Ptr,
            buf.Length,
            taskCount);

        Console.WriteLine($"[NativeBufferTaskGraph] tasks={taskCount} sum={sum} first={span[0]} last={span[^1]}");
    }
}

