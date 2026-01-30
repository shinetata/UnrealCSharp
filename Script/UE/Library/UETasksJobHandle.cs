using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Script.Library;

public sealed class UETasksJobHandle : IDisposable
{
    public static readonly UETasksJobHandle Completed = new(0, default, null, true);

    private long handleId;
    private GCHandle handle;
    private GCHandle[] extraHandles;
    private bool released;

    public long HandleId => handleId;

    public UETasksJobHandle(long handleId, GCHandle handle)
        : this(handleId, handle, null, false)
    {
    }

    private UETasksJobHandle(long handleId, GCHandle handle, GCHandle[] extraHandles, bool released)
    {
        this.handleId = handleId;
        this.handle = handle;
        this.extraHandles = extraHandles;
        this.released = released;
    }

    public static UETasksJobHandle CreateCombined(long handleId, GCHandle[] extraHandles)
    {
        if (handleId == 0)
        {
            if (extraHandles != null)
            {
                for (int i = 0; i < extraHandles.Length; i++)
                {
                    if (extraHandles[i].IsAllocated)
                    {
                        extraHandles[i].Free();
                    }
                }
            }
            return Completed;
        }

        return new UETasksJobHandle(handleId, default, extraHandles, false);
    }

    public void DetachForCombine(List<GCHandle> target)
    {
        if (released)
        {
            return;
        }

        released = true;
        handleId = 0;

        if (handle.IsAllocated)
        {
            target.Add(handle);
            handle = default;
        }

        if (extraHandles != null)
        {
            for (int i = 0; i < extraHandles.Length; i++)
            {
                if (extraHandles[i].IsAllocated)
                {
                    target.Add(extraHandles[i]);
                }
            }
            extraHandles = null;
        }
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

        if (extraHandles != null)
        {
            for (int i = 0; i < extraHandles.Length; i++)
            {
                if (extraHandles[i].IsAllocated)
                {
                    extraHandles[i].Free();
                }
            }
            extraHandles = null;
        }
    }
}
