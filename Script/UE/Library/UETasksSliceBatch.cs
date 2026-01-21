using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Script.Library;

public static class UETasksSliceBatch
{
    private enum BatchMode
    {
        None,
        Managed,
        Native
    }

    private static BatchMode Mode = BatchMode.None;
    private static int[]? ManagedData;
    private static NativeBuffer<int>? NativeData;
    private static Action<int[], int, int>? ManagedHandler;
    private static Action<nint, int, int>? NativeHandler;
    private static long Sum;

    public static long RunManagedAddOneAndSum(int[] data, int taskCount)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (taskCount <= 0) throw new ArgumentOutOfRangeException(nameof(taskCount));

        ConfigureManaged(data, AddOneAndSumManaged);

        return ExecuteManagedPinned(taskCount);
    }

    public static long RunNativeAddOneAndSum(NativeBuffer<int> data, int taskCount)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (taskCount <= 0) throw new ArgumentOutOfRangeException(nameof(taskCount));

        ConfigureNative(data, AddOneAndSumNative);

        return ExecuteNative(taskCount);
    }

    public static void RunManaged(int[] data, int taskCount, Action<int[], int, int> handler)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        if (taskCount <= 0) throw new ArgumentOutOfRangeException(nameof(taskCount));

        ConfigureManaged(data, handler);
        ExecuteManagedPinned(taskCount);
    }

    public static void RunNative(NativeBuffer<int> data, int taskCount, Action<nint, int, int> handler)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        if (taskCount <= 0) throw new ArgumentOutOfRangeException(nameof(taskCount));

        ConfigureNative(data, handler);
        ExecuteNative(taskCount);
    }

    public static unsafe void ExecuteSlice(nint data, int start, int count)
    {
        if (count <= 0)
        {
            return;
        }

        var mode = Mode;
        if (mode == BatchMode.None)
        {
            throw new InvalidOperationException("UETasksSliceBatch is not configured.");
        }

        if (mode == BatchMode.Managed)
        {
            var array = ManagedData ?? throw new InvalidOperationException("Managed data is null.");
            var handler = ManagedHandler ?? throw new InvalidOperationException("Managed handler is null.");
            handler(array, start, count);
            return;
        }

        var nativeHandler = NativeHandler ?? throw new InvalidOperationException("Native handler is null.");
        nativeHandler(data, start, count);
    }

    private static void ConfigureManaged(int[] data, Action<int[], int, int> handler)
    {
        ManagedData = data;
        ManagedHandler = handler;
        NativeData = null;
        NativeHandler = null;
        Mode = BatchMode.Managed;
        Interlocked.Exchange(ref Sum, 0);
    }

    private static void ConfigureNative(NativeBuffer<int> data, Action<nint, int, int> handler)
    {
        ManagedData = null;
        ManagedHandler = null;
        NativeData = data;
        NativeHandler = handler;
        Mode = BatchMode.Native;
        Interlocked.Exchange(ref Sum, 0);
    }

    private static long ExecuteManagedPinned(int taskCount)
    {
        var data = ManagedData ?? throw new InvalidOperationException("Managed data is null.");
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var ptr = handle.AddrOfPinnedObject();
            FTasksSliceImplementation.FTasksSlice_ExecuteBatchImplementation(
                (nint)ptr,
                data.Length,
                taskCount,
                wait: true);
        }
        finally
        {
            handle.Free();
        }

        return Interlocked.Read(ref Sum);
    }

    private static long ExecuteNative(int taskCount)
    {
        var data = NativeData ?? throw new InvalidOperationException("Native data is null.");

        FTasksSliceImplementation.FTasksSlice_ExecuteBatchImplementation(
            data.Ptr,
            data.Length,
            taskCount,
            wait: true);

        return Interlocked.Read(ref Sum);
    }

    private static void AddOneAndSumManaged(int[] data, int start, int count)
    {
        var end = start + count;
        long local = 0;

        for (var i = start; i < end; i++)
        {
            data[i] += 1;
            local += data[i];
        }

        Interlocked.Add(ref Sum, local);
    }

    private static unsafe void AddOneAndSumNative(nint data, int start, int count)
    {
        var ptr = (int*)data;
        var end = start + count;
        long local = 0;

        for (var i = start; i < end; i++)
        {
            ptr[i] += 1;
            local += ptr[i];
        }

        Interlocked.Add(ref Sum, local);
    }
}
