using System;

namespace Script.Library;

public static class TaskGraphVsCSharpEcsPerfRunner
{
    public static void RunPosVelArchetypeCompare(
        int taskCount = 8,
        int iterations = 32,
        int warmup = 3,
        int rounds = 5,
        int minParallelChunkSize = 10_000,
        int dt = 1)
    {
        RunPosVelArchetypeCompareInternal(taskCount, iterations, warmup, rounds, minParallelChunkSize, dt);
    }

    public static void RunPosVelArchetypeCompareSweep(
        int[] taskCounts,
        int[] minParallelChunkSizes,
        int iterations = 32,
        int warmup = 3,
        int rounds = 5,
        int dt = 1)
    {
        if (taskCounts == null || taskCounts.Length == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(taskCounts));
        }

        if (minParallelChunkSizes == null || minParallelChunkSizes.Length == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minParallelChunkSizes));
        }

        for (var i = 0; i < taskCounts.Length; i++)
        {
            if (taskCounts[i] <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(taskCounts));
            }
        }

        for (var i = 0; i < minParallelChunkSizes.Length; i++)
        {
            if (minParallelChunkSizes[i] <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minParallelChunkSizes));
            }
        }

        for (var i = 0; i < taskCounts.Length; i++)
        {
            for (var j = 0; j < minParallelChunkSizes.Length; j++)
            {
                Console.WriteLine(
                    $"[EcsPerfSweep] taskCount={taskCounts[i]} minChunk={minParallelChunkSizes[j]} iterations={iterations} warmup={warmup} rounds={rounds} dt={dt}");
                RunPosVelArchetypeCompareInternal(taskCounts[i], iterations, warmup, rounds, minParallelChunkSizes[j], dt);
            }
        }
    }

    private static void RunPosVelArchetypeCompareInternal(
        int taskCount,
        int iterations,
        int warmup,
        int rounds,
        int minParallelChunkSize,
        int dt)
    {
        if (taskCount <= 0) throw new ArgumentOutOfRangeException(nameof(taskCount));
        if (iterations <= 0) throw new ArgumentOutOfRangeException(nameof(iterations));
        if (warmup < 0) throw new ArgumentOutOfRangeException(nameof(warmup));
        if (rounds <= 0) throw new ArgumentOutOfRangeException(nameof(rounds));
        if (minParallelChunkSize <= 0) throw new ArgumentOutOfRangeException(nameof(minParallelChunkSize));

        var taskGraphMs = new double[rounds];
        var parallelForMs = new double[rounds];
        var taskRunMs = new double[rounds];

        for (var r = 0; r < rounds; r++)
        {
            var orderIndex = r % 3;
            long sumTaskGraph = 0;
            long sumParallelFor = 0;
            long sumTaskRun = 0;
            double tgMs;
            double pfMs;
            double trMs;
            string order;

            if (orderIndex == 0)
            {
                order = "TG->PF->TR";
                tgMs = EcsArchetypePerf.MeasureTaskGraph(taskCount, iterations, warmup, minParallelChunkSize, dt, out sumTaskGraph);
                pfMs = EcsArchetypePerf.MeasureParallelFor(taskCount, iterations, warmup, minParallelChunkSize, dt, out sumParallelFor);
                trMs = EcsArchetypePerf.MeasureTaskRun(taskCount, iterations, warmup, minParallelChunkSize, dt, out sumTaskRun);
            }
            else if (orderIndex == 1)
            {
                order = "PF->TR->TG";
                pfMs = EcsArchetypePerf.MeasureParallelFor(taskCount, iterations, warmup, minParallelChunkSize, dt, out sumParallelFor);
                trMs = EcsArchetypePerf.MeasureTaskRun(taskCount, iterations, warmup, minParallelChunkSize, dt, out sumTaskRun);
                tgMs = EcsArchetypePerf.MeasureTaskGraph(taskCount, iterations, warmup, minParallelChunkSize, dt, out sumTaskGraph);
            }
            else
            {
                order = "TR->TG->PF";
                trMs = EcsArchetypePerf.MeasureTaskRun(taskCount, iterations, warmup, minParallelChunkSize, dt, out sumTaskRun);
                tgMs = EcsArchetypePerf.MeasureTaskGraph(taskCount, iterations, warmup, minParallelChunkSize, dt, out sumTaskGraph);
                pfMs = EcsArchetypePerf.MeasureParallelFor(taskCount, iterations, warmup, minParallelChunkSize, dt, out sumParallelFor);
            }

            taskGraphMs[r] = tgMs;
            parallelForMs[r] = pfMs;
            taskRunMs[r] = trMs;

            var sumOk = sumTaskGraph == sumParallelFor && sumTaskGraph == sumTaskRun;
            Console.WriteLine(
                $"[EcsPerfRound] round={r + 1}/{rounds} order={order} tg={tgMs:F3}ms pf={pfMs:F3}ms tr={trMs:F3}ms sumOk={sumOk}");
        }

        var taskGraphMedian = Median(taskGraphMs);
        var parallelForMedian = Median(parallelForMs);
        var taskRunMedian = Median(taskRunMs);
        var ratioParallelFor = parallelForMedian / Math.Max(0.000001, taskGraphMedian);
        var ratioTaskRun = taskRunMedian / Math.Max(0.000001, taskGraphMedian);

        Console.WriteLine(
            $"[EcsPerfSummary] taskCount={taskCount} iterations={iterations} warmup={warmup} rounds={rounds} minChunk={minParallelChunkSize} " +
            $"tgMedian={taskGraphMedian:F3}ms pfMedian={parallelForMedian:F3}ms trMedian={taskRunMedian:F3}ms " +
            $"ratioPf={ratioParallelFor:F3} ratioTr={ratioTaskRun:F3}");
    }

    private static double Median(double[] values)
    {
        var copy = new double[values.Length];
        Array.Copy(values, copy, values.Length);
        Array.Sort(copy);

        var mid = copy.Length / 2;
        if (copy.Length % 2 == 1)
        {
            return copy[mid];
        }

        return (copy[mid - 1] + copy[mid]) * 0.5;
    }
}
