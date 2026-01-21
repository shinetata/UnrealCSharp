using System.Runtime.CompilerServices;

namespace Script.Library;

public static unsafe class FTasksSliceImplementation
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern void FTasksSlice_ExecuteBatchImplementation(nint data, int length, int taskCount, bool wait);
}
