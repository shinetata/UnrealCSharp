using System;

namespace Script.Library;

public static class NativeBufferInternalCallDemo
{
    public static void RunInt32()
    {
        using var buf = new NativeBuffer<int>(length: 8);

        var span = buf.AsSpan();
        for (var i = 0; i < span.Length; i++)
        {
            span[i] = i;
        }

        var sum = FNativeBufferKernelImplementation.FNativeBufferKernel_AddOneAndSumInt32Implementation(buf.Ptr, buf.Length);

        Console.WriteLine($"{buf} sum={sum} first={span[0]} last={span[^1]}");
    }
}

