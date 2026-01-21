using System;
using System.Runtime.InteropServices;

namespace Script.Library;

public static class UETasksBatch
{
    private sealed class BatchState
    {
        public required Action<int> ExecuteIndex;
    }

    public static void ExecuteBatch(Action<int> executeIndex, int taskCount)
    {
        if (executeIndex == null) throw new ArgumentNullException(nameof(executeIndex));
        if (taskCount <= 0) return;

        var state = new BatchState
        {
            ExecuteIndex = executeIndex
        };

        var handle = GCHandle.Alloc(state);

        try
        {
            FTasksImplementation.FTasks_ExecuteBatchImplementation(
                (nint)GCHandle.ToIntPtr(handle),
                taskCount,
                wait: true);
        }
        finally
        {
            handle.Free();
        }
    }

    public static void ExecuteTask(nint stateHandle, int index)
    {
        var handle = GCHandle.FromIntPtr((IntPtr)stateHandle);

        if (handle.Target is not BatchState state)
        {
            throw new InvalidOperationException("UETasksBatch state handle is invalid.");
        }

        state.ExecuteIndex(index);
    }
}
