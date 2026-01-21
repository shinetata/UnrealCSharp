using System.Runtime.CompilerServices;

namespace Script.Library;

public static unsafe class FTasksImplementation
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern void FTasks_ExecuteBatchImplementation(nint stateHandle, int taskCount, bool wait);
}
