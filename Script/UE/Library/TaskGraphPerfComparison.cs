using System;
using System.Diagnostics;

namespace Script.Library;

/// <summary>
/// 最小性能对比：同等数据量下重复执行 N 次批次遍历，对比 baseline/testline 总耗时。
/// 后续每次做性能相关改动时，只改 testline；若提升明显，再把改动同步到 baseline。
/// </summary>
public static class TaskGraphPerfComparison
{
    public sealed class QueryState
    {
        public required int[] Data;
        public required int[] Starts;
        public required int[] Lengths;
    }

    private static QueryState? CurrentState;

    private static readonly Action<int> WorkAction = ExecuteIndex;

    public static void Run(
        int length = 10000,
        int taskCount = 8,
        int queryCount = 5,
        int iterations = 32,
        int warmup = 3)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (taskCount <= 0) throw new ArgumentOutOfRangeException(nameof(taskCount));
        if (queryCount <= 0) throw new ArgumentOutOfRangeException(nameof(queryCount));
        if (iterations <= 0) throw new ArgumentOutOfRangeException(nameof(iterations));
        if (warmup < 0) throw new ArgumentOutOfRangeException(nameof(warmup));

        var baselineStates = BuildStates(length, taskCount, queryCount);
        var testlineStates = BuildStates(length, taskCount, queryCount);

        var baselineMs = RunOnce("baseline", baselineStates, taskCount, iterations, warmup, ExecuteBatchBaseline);
        var testlineMs = RunOnce("testline", testlineStates, taskCount, iterations, warmup, ExecuteBatchTestline);

        var ratio = testlineMs / Math.Max(0.000001, baselineMs);
        Console.WriteLine(
            $"[TaskGraphPerf] length={length} taskCount={taskCount} queryCount={queryCount} iterations={iterations} warmup={warmup} " +
            $"baseline={baselineMs:F3}ms testline={testlineMs:F3}ms ratio={ratio:F3}");
    }

    private static QueryState[] BuildStates(int length, int taskCount, int queryCount)
    {
        var starts = new int[taskCount];
        var lengths = new int[taskCount];

        var baseChunk = length / taskCount;
        var remainder = length % taskCount;

        var offset = 0;
        for (var i = 0; i < taskCount; i++)
        {
            var len = baseChunk + (i < remainder ? 1 : 0);
            starts[i] = offset;
            lengths[i] = len;
            offset += len;
        }

        var states = new QueryState[queryCount];
        for (var q = 0; q < queryCount; q++)
        {
            var data = new int[length];
            for (var i = 0; i < data.Length; i++)
            {
                data[i] = i;
            }

            states[q] = new QueryState
            {
                Data = data,
                Starts = starts,
                Lengths = lengths
            };
        }

        return states;
    }

    private static double RunOnce(
        string name,
        QueryState[] states,
        int taskCount,
        int iterations,
        int warmup,
        Action<int, Action<int>> executeBatch)
    {
        for (var w = 0; w < warmup; w++)
        {
            RunQueriesOnce(states, taskCount, executeBatch);
        }

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            RunQueriesOnce(states, taskCount, executeBatch);
        }
        sw.Stop();

        var ms = sw.Elapsed.TotalMilliseconds;

        Console.WriteLine($"[TaskGraphPerf] {name} total={ms:F3}ms");
        return ms;
    }

    private static void RunQueriesOnce(QueryState[] states, int taskCount, Action<int, Action<int>> executeBatch)
    {
        for (var q = 0; q < states.Length; q++)
        {
            CurrentState = states[q];
            executeBatch(taskCount, WorkAction);
        }
    }

    internal static QueryState GetCurrentStateOrThrow()
    {
        var state = CurrentState;
        if (state == null) throw new InvalidOperationException("TaskGraphPerfComparison.CurrentState is null.");
        return state;
    }
    private static void ExecuteIndex(int index)
    {
        var state = GetCurrentStateOrThrow();
        var start = state.Starts[index];
        var len = state.Lengths[index];

        var span = state.Data.AsSpan(start, len);

        for (var i = 0; i < span.Length; i++)
        {
            span[i] = unchecked(span[i] + 1);
        }
    }

    private static void ExecuteBatchBaseline(int taskCount, Action<int> action) =>
        TaskGraphBatchBaseline.ExecuteBatch(action, taskCount);

    private static void ExecuteBatchTestline(int taskCount, Action<int> action) =>
        TaskGraphBatchTestline.ExecuteBatch(action, taskCount);
}
