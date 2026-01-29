using System;
using System.Runtime.InteropServices;

namespace Script.Library;

public sealed class UETasksJobHandle : IDisposable
{
    public static readonly UETasksJobHandle Completed = new(0, default, true);

    private long handleId;
    private GCHandle handle;
    private bool released;

    public UETasksJobHandle(long handleId, GCHandle handle)
        : this(handleId, handle, false)
    {
    }

    public UETasksJobHandle(long handleId, GCHandle handle, bool released)
    {
        this.handleId = handleId;
        this.handle = handle;
        this.released = released;
    }

    public bool IsCompleted
    {
        get
        {
            if (handleId == 0)
            {
                return true;
            }

            return FTasksQueryImplementation.FTasksQuery_IsHandleCompletedImplementation(handleId);
        }
    }

    public void Wait()
    {
        if (handleId == 0)
        {
            return;
        }

        FTasksQueryImplementation.FTasksQuery_WaitHandleImplementation(handleId);
    }

    public void Dispose()
    {
        if (released)
        {
            return;
        }

        released = true;

        if (handleId != 0)
        {
            FTasksQueryImplementation.FTasksQuery_ReleaseHandleImplementation(handleId);
            handleId = 0;
        }

        if (handle.IsAllocated)
        {
            handle.Free();
        }
    }
}
