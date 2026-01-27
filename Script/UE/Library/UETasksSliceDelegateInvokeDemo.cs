using System;
using System.Threading;

namespace Script.Library;

public static class UETasksSliceDelegateInvokeDemo
{
    // 极简示例：把一个 static Action 作为参数传给 C++，由 C++ 在 UE::Tasks worker 上 Runtime_Invoke 调用。
    //
    // 约束（C++ 侧会强制检查）：
    // - handler 必须是 static（不能是实例方法/不能捕获 lambda）
    // - handler 签名固定为：void Handler()
    //
    // 注意：这个 demo 走的是 Runtime_Invoke（通用但比 thunk 慢），只用于演示“参数传 delegate”这条能力链路。
    public static void RunLogOnWorkerByRuntimeInvoke()
    {
        FTasksSliceImplementation.FTasksSlice_ExecuteBatchWithDelegateInvokeImplementation(
            handler: LogFromWorker,
            wait: true);
    }

    private static void LogFromWorker()
    {
        Console.WriteLine(
            $"[UETasksDelegateInvokeDemo] managedTid={Thread.CurrentThread.ManagedThreadId} " +
            $"TaskThreadId={""}");
    }
}
