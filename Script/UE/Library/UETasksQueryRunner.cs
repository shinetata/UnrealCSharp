using System;
using System.Runtime.InteropServices;

namespace Script.Library;

public interface IUETasksQueryRunner
{
    void ExecuteTask(int taskIndex);
}

public static class UETasksQueryRunner
{
    public static void ExecuteTask(nint stateHandle, int index)
    {
        var handle = GCHandle.FromIntPtr((IntPtr)stateHandle);
        if (handle.Target is not IUETasksQueryRunner runner)
        {
            throw new InvalidOperationException("UETasksQueryRunner state handle is invalid.");
        }

        runner.ExecuteTask(index);
    }
}
