using System.Runtime.CompilerServices;

namespace Script.Library;

public static class FNativeBufferTaskGraphImplementation
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern long FNativeBufferTaskGraph_AddOneAndSumInt32ParallelImplementation(nint data, int length, int taskCount);
}

