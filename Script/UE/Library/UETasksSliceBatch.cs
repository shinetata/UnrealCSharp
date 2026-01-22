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
    private static readonly Action<nint, int, int> NativeThunkHandler = AddOneAndSumNative;
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

    public static long RunNativeAddOneAndSumByHandler(NativeBuffer<int> data, int taskCount)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (taskCount <= 0) throw new ArgumentOutOfRangeException(nameof(taskCount));

        Interlocked.Exchange(ref Sum, 0);

        // 通过 internal call 参数直接把 C# handler 传给 C++（C++ 侧 delegate->MonoMethod->thunk）。
        FTasksSliceImplementation.FTasksSlice_ExecuteBatchWithHandlerImplementation(
            data.Ptr,
            data.Length,
            taskCount,
            wait: true,
            handler: AddOneAndSumNative);

        return Interlocked.Read(ref Sum);
    }

    public static unsafe long RunManagedPinnedAddOneAndSumByHandler(int[] data, int taskCount)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (taskCount <= 0) throw new ArgumentOutOfRangeException(nameof(taskCount));

        Interlocked.Exchange(ref Sum, 0);

        fixed (int* p = data)
        {
            // handler 以指针访问数据；Managed 数组必须处于 pinned/fixed 范围内。
            FTasksSliceImplementation.FTasksSlice_ExecuteBatchWithHandlerImplementation(
                (nint)p,
                data.Length,
                taskCount,
                wait: true,
                handler: NativeThunkHandler);
        }

        return Interlocked.Read(ref Sum);
    }

    public static void RunNativeByHandler(NativeBuffer<int> data, int taskCount, Action<nint, int, int> handler)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        if (taskCount <= 0) throw new ArgumentOutOfRangeException(nameof(taskCount));

        // 极致性能版本：要求 static / 不捕获（否则 C++ 侧会拒绝 instance method）。
        if (handler.Target != null)
        {
            throw new ArgumentException("handler must be a static method (no closure / no instance target).", nameof(handler));
        }

        FTasksSliceImplementation.FTasksSlice_ExecuteBatchWithHandlerImplementation(
            data.Ptr,
            data.Length,
            taskCount,
            wait: true,
            handler: handler);
    }

    public static unsafe void RunManagedPinnedByHandler(int[] data, int taskCount, Action<nint, int, int> handler)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        if (taskCount <= 0) throw new ArgumentOutOfRangeException(nameof(taskCount));

        if (handler.Target != null)
        {
            throw new ArgumentException("handler must be a static method (no closure / no instance target).", nameof(handler));
        }

        fixed (int* p = data)
        {
            FTasksSliceImplementation.FTasksSlice_ExecuteBatchWithHandlerImplementation(
                (nint)p,
                data.Length,
                taskCount,
                wait: true,
                handler: handler);
        }
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
            for (var j = 0; j < 1000; j++)
            {
                var temp = j;
                temp++;
            }
        }

        Interlocked.Add(ref Sum, local);
    }
}
