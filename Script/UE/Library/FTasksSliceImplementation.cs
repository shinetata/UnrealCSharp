using System;
using System.Runtime.CompilerServices;

namespace Script.Library;

public static unsafe class FTasksSliceImplementation
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern void FTasksSlice_ExecuteBatchImplementation(nint data, int length, int taskCount, bool wait);

    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern void FTasksSlice_ExecuteBatchWithHandlerImplementation(
        nint data,
        int length,
        int taskCount,
        bool wait,
        Action<nint, int, int> handler);

    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern void FTasksSlice_ExecuteBatchWithDelegateInvokeImplementation(
        Action handler,
        bool wait);
}
