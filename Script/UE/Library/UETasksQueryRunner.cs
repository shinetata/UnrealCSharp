using System;
using System.Runtime.InteropServices;

namespace Script.Library;

public interface IUETasksQueryRunner
{
    /// <summary>
    /// 执行任务。
    /// </summary>
    /// <param name="taskIndex">任务索引，实际对应 chunkIndex（用于从 chunks 数组中获取对应的 ArchetypeChunk）。</param>
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
