using System.Runtime.CompilerServices;

namespace Script.Library;

public static unsafe class FTasksQueryImplementation
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern void FTasksQuery_ExecuteBatchImplementation(
        nint stateHandle, int taskCount, bool wait);

    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern int FTasksQuery_GetNumWorkerThreadsImplementation();
    
    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern int FTasksQuery_GetCurrentNativeThreadIdImplementation();
}
