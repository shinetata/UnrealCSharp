using System;
using System.Runtime.InteropServices;

namespace Script.Library;

public interface ITaskGraphTask
{
    void Execute();
}

public static class TaskGraphBatch
{
    public static int GetCurrentNativeThreadId() => FTaskGraphImplementation.FTaskGraph_GetCurrentThreadIdImplementation();

    private sealed class BatchState
    {
        public required Action<int> ExecuteIndex;
    }

    public static void ExecuteTasks(ITaskGraphTask[] tasks)
    {
        if (tasks == null) throw new ArgumentNullException(nameof(tasks));
        if (tasks.Length == 0) return;

        ExecuteBatch(i => tasks[i].Execute(), tasks.Length);
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
            FTaskGraphImplementation.FTaskGraph_ExecuteBatchImplementation(
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
            throw new InvalidOperationException("TaskGraphBatch state handle is invalid.");
        }

        state.ExecuteIndex(index);
    }
}
