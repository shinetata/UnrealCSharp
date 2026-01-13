using System.Runtime.CompilerServices;

namespace Script.Library;

public static unsafe class FTaskGraphImplementation
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern int FTaskGraph_GetCurrentThreadIdImplementation();

    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern void FTaskGraph_ExecuteBatchBaselineImplementation(nint stateHandle, int taskCount, bool wait);

    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern void FTaskGraph_ExecuteBatchTestlineImplementation(nint stateHandle, int taskCount, bool wait);

    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern void FTaskGraph_ExecuteBatchImplementation(nint stateHandle, int taskCount, bool wait);

    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern void FTaskGraph_EnqueueProbeImplementation(int token);
}
