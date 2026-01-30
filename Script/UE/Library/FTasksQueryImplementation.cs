using System.Runtime.CompilerServices;

namespace Script.Library;

public static unsafe class FTasksQueryImplementation
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern void FTasksQuery_ExecuteBatchImplementation(
        nint stateHandle, int taskCount, bool wait);

    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern long FTasksQuery_ScheduleBatchImplementation(
        nint stateHandle, int taskCount);

    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern bool FTasksQuery_IsHandleCompletedImplementation(long handleId);

    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern void FTasksQuery_WaitHandleImplementation(long handleId);

    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern void FTasksQuery_ReleaseHandleImplementation(long handleId);

    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern long FTasksQuery_CombineHandlesImplementation(long[] handleIds);

    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern int FTasksQuery_GetNumWorkerThreadsImplementation();
    
    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern int FTasksQuery_GetCurrentNativeThreadIdImplementation();
}
