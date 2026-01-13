using System;
using System.Threading;

namespace Script.Library;

public static class TaskGraphProbe
{
    public static int LastToken;
    public static int LastManagedThreadId;
    public static int LastGameThreadNativeId;
    public static int LastWorkerThreadNativeId;

    // 主线程创建后赋值；worker 线程在 OnWorker 中遍历并修改。
    public static int[]? SharedInt32;

    public static void MainThreadCreateSharedInt32(int length)
    {
        var data = new int[length];

        for (var i = 0; i < data.Length; i++)
        {
            data[i] = i;
        }

        SharedInt32 = data;
    }

    public static void Enqueue(int token = 1) => FTaskGraphImplementation.FTaskGraph_EnqueueProbeImplementation(token);

    public static void OnWorker(int token, int gameThreadNativeId, int workerThreadNativeId)
    {
        LastToken = token;
        LastManagedThreadId = Thread.CurrentThread.ManagedThreadId;
        LastGameThreadNativeId = gameThreadNativeId;
        LastWorkerThreadNativeId = workerThreadNativeId;

        Console.WriteLine(
            $"[TaskGraphProbe] token={token} managedTid={LastManagedThreadId} GT={gameThreadNativeId} Worker={workerThreadNativeId}");

        var data = SharedInt32;

        if (data == null)
        {
            Console.WriteLine("[TaskGraphProbe] SharedInt32 is null (did you call MainThreadCreateSharedInt32?)");

            return;
        }

        long checksumBefore = 0;

        for (var i = 0; i < data.Length; i++)
        {
            checksumBefore += data[i];
            data[i] = data[i] * 2;
        }

        long checksumAfter = 0;

        for (var i = 0; i < data.Length; i++)
        {
            checksumAfter += data[i];
        }

        Console.WriteLine(
            $"[TaskGraphProbe] SharedInt32 processed: len={data.Length} before={checksumBefore} after={checksumAfter} first={data[0]} last={data[^1]}");
    }
}
